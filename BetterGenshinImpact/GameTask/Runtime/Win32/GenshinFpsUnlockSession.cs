using BetterGenshinImpact.Core.Config;
using Microsoft.Extensions.Logging;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.GameTask.Runtime.Win32;

/// <summary>
/// 一次原神进程的帧率解锁会话。
/// </summary>
internal sealed class GenshinFpsUnlockSession : IAsyncDisposable
{
    /// <summary>
    /// Hook DLL 文件名
    /// </summary>
    private const string HookDllName = "GenshinFpsUnlockHook.dll";

    /// <summary>
    /// 等待 Hook DLL 找到帧率字段的最长时间
    /// </summary>
    private static readonly TimeSpan InjectionTimeout = TimeSpan.FromSeconds(15);

    /// <summary>
    /// 等待游戏内 Hook 恢复原帧率的最长时间
    /// </summary>
    private static readonly TimeSpan RestoreTimeout = TimeSpan.FromSeconds(2);

    /// <summary>
    /// 帧率写入和心跳更新周期
    /// </summary>
    private static readonly TimeSpan WriteInterval = TimeSpan.FromMilliseconds(62);

    /// <summary>
    /// 去雾 Hook 安装后等待首次命中的时间
    /// </summary>
    private static readonly TimeSpan FogHookObservationWindow = TimeSpan.FromSeconds(5);

    /// <summary>
    /// FOV Hook 安装后等待首次命中的时间
    /// </summary>
    private static readonly TimeSpan FovHookObservationWindow = TimeSpan.FromSeconds(5);

    /// <summary>
    /// 目标原神进程 ID
    /// </summary>
    private readonly int _processId;

    /// <summary>
    /// 目标原神主窗口句柄
    /// </summary>
    private readonly nint _gameWindowHandle;

    /// <summary>
    /// 当前原神启动配置
    /// </summary>
    private readonly GenshinStartConfig _config;

    /// <summary>
    /// 日志记录器
    /// </summary>
    private readonly ILogger _logger;

    /// <summary>
    /// 取消写入和等待操作的令牌源
    /// </summary>
    private readonly CancellationTokenSource _cancellationTokenSource = new();

    /// <summary>
    /// 防止重复启动同一会话
    /// </summary>
    private readonly object _startLocker = new();

    /// <summary>
    /// 后台会话完成状态
    /// </summary>
    private Task<GenshinFpsUnlockSessionResult>? _completion;

    /// <summary>
    /// 是否已经启动后台任务
    /// </summary>
    private bool _started;

    /// <summary>
    /// 是否已经释放
    /// </summary>
    private int _disposed;

    /// <summary>
    /// 最近一次已经通知给宿主的运行状态
    /// </summary>
    private GenshinFpsUnlockRuntimeStatus? _lastPublishedStatus;

    /// <summary>
    /// 最近一次去雾功能状态
    /// </summary>
    private int _lastFogStatus = -1;

    /// <summary>
    /// 最近一次记录的雾参数复制 Hook 调用次数
    /// </summary>
    private int _lastFogHookHitCount = -1;

    /// <summary>
    /// 最近一次记录的安全替换临时参数次数
    /// </summary>
    private int _lastFogChangeCount = -1;

    /// <summary>
    /// 去雾 Hook 进入就绪状态的时间
    /// </summary>
    private DateTime _fogReadySince = DateTime.MinValue;

    /// <summary>
    /// 是否已经提示过去雾 Hook 未命中
    /// </summary>
    private bool _fogNoHitWarningLogged;

    /// <summary>
    /// 最近一次 FOV 功能状态
    /// </summary>
    private int _lastFovStatus = -1;

    /// <summary>
    /// 最近一次记录的 FOV Hook 命中次数
    /// </summary>
    private int _lastFovHookHitCount = -1;

    /// <summary>
    /// 最近一次记录的 FOV 替换次数
    /// </summary>
    private int _lastFovReplaceCount = -1;

    /// <summary>
    /// FOV Hook 进入就绪状态的时间
    /// </summary>
    private DateTime _fovReadySince = DateTime.MinValue;

    /// <summary>
    /// 是否已经提示过 FOV Hook 未命中
    /// </summary>
    private bool _fovNoHitWarningLogged;

    /// <summary>
    /// 最近一次记录到日志的 FOV 目标值
    /// </summary>
    private int _lastLoggedFovTarget = -1;

    /// <summary>
    /// 最近一次记录到日志的强制 FOV 模式状态；-1 表示尚未记录
    /// </summary>
    private int _lastLoggedFovForceMode = -1;

    /// <summary>
    /// 最近一次记录到日志的七项体验优化状态
    /// </summary>
    private GenshinGameplayQoLRuntimeStatus? _lastLoggedGameplayQoLStatus;

    /// <summary>
    /// 最近一次记录到日志的七项体验优化计数
    /// </summary>
    private int[]? _lastLoggedGameplayQoLMetrics;

    /// <summary>
    /// 最近一次输出体验优化计数变化日志的时间
    /// </summary>
    private DateTime _lastGameplayQoLMetricLogAt = DateTime.MinValue;

    /// <summary>
    /// 游戏内 Hook 的功能状态发生变化。
    /// </summary>
    internal event EventHandler<GenshinFpsUnlockRuntimeStatus>? StatusChanged;

    /// <summary>
    /// 创建指定游戏进程的解锁会话。
    /// </summary>
    /// <param name="processId">目标游戏进程 ID</param>
    /// <param name="gameWindowHandle">目标游戏窗口句柄</param>
    /// <param name="config">当前原神启动配置</param>
    /// <param name="logger">日志记录器</param>
    public GenshinFpsUnlockSession(
        int processId,
        nint gameWindowHandle,
        GenshinStartConfig config,
        ILogger logger)
    {
        _processId = processId;
        _gameWindowHandle = gameWindowHandle;
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// 目标游戏进程 ID
    /// </summary>
    public int ProcessId => _processId;

    /// <summary>
    /// 会话完成状态；重复读取返回同一个任务
    /// </summary>
    public Task<GenshinFpsUnlockSessionResult> Completion =>
        _completion ?? throw new InvalidOperationException("帧率解锁会话尚未启动");

    /// <summary>
    /// 在后台启动 Hook 会话；重复调用无副作用。
    /// </summary>
    public void Start()
    {
        lock (_startLocker)
        {
            if (_started)
            {
                return;
            }

            _started = true;
            _completion = Task.Run(RunAsync);
        }
    }

    /// <summary>
    /// 请求停止写入；后台会话会在退出前通知游戏内 Hook 恢复原值。
    /// </summary>
    public void RequestStop()
    {
        try
        {
            _cancellationTokenSource.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // 会话已结束并释放时，停止请求无需重复处理。
        }
    }

    /// <summary>
    /// 请求停止并等待后台会话结束。
    /// </summary>
    /// <returns>异步释放任务</returns>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        RequestStop();
        if (_completion is not null)
        {
            await _completion.ConfigureAwait(false);
        }

        _cancellationTokenSource.Dispose();
    }

    /// <summary>
    /// 安装窗口消息 Hook，等待游戏内 DLL 定位字段并持续更新目标帧率。
    /// </summary>
    /// <returns>本次会话的结束状态</returns>
    private async Task<GenshinFpsUnlockSessionResult> RunAsync()
    {
        var token = _cancellationTokenSource.Token;
        GenshinFpsUnlockHookChannel? hookChannel = null;
        GenshinGameplayQoLChannel? gameplayQoLChannel = null;
        GenshinFpsUnlockHookScope? hookScope = null;
        GenshinFpsUnlockHookModuleLease? hookModuleLease = null;
        string? failureReason = null;
        var gameExited = false;
        var hasInjected = false;
        var restoreFailed = false;

        try
        {
            using var gameProcess = Process.GetProcessById(_processId);
            if (gameProcess.HasExited)
            {
                return new GenshinFpsUnlockSessionResult(null, GameExited: true);
            }

            if (!IsLocalGenshinProcess(gameProcess))
            {
                failureReason = "目标进程不是本机原神";
                return new GenshinFpsUnlockSessionResult(failureReason, GameExited: false);
            }

            if (_gameWindowHandle == 0)
            {
                failureReason = "无法获取原神窗口句柄";
                return new GenshinFpsUnlockSessionResult(failureReason, GameExited: false);
            }

            var hookDllSourcePath = Path.Combine(
                AppContext.BaseDirectory,
                HookDllName);
            if (!File.Exists(hookDllSourcePath))
            {
                failureReason = $"缺少运行期 Hook DLL：{hookDllSourcePath}";
                return new GenshinFpsUnlockSessionResult(failureReason, GameExited: false);
            }

            try
            {
                hookModuleLease = GenshinFpsUnlockHookModuleLease.Create(
                    hookDllSourcePath);
            }
            catch (Exception exception)
            {
                failureReason = $"无法准备运行期 Hook 模块：{exception.Message}";
                return new GenshinFpsUnlockSessionResult(failureReason, GameExited: false);
            }

            var isChineseClient = IsChineseClientProcess(gameProcess);
            var featureFlags = CreateFeatureFlags(isChineseClient);
            hookChannel = new GenshinFpsUnlockHookChannel(
                _processId,
                _config.FpsUnlockTarget,
                featureFlags,
                _config.FovTarget);
            try
            {
                gameplayQoLChannel = new GenshinGameplayQoLChannel(
                    _processId,
                    CreateGameplayQoLFlags(_config, isChineseClient));
            }
            catch (Exception exception)
            {
                // 七项体验优化不可用时不能拖垮帧率、去雾和 FOV。
                _logger.LogWarning(
                    exception,
                    "游戏体验优化共享状态创建失败，本次会话跳过七项功能：PID={ProcessId}",
                    _processId);
            }

            _logger.LogInformation(
                "开始向原神进程安装帧率 Hook：PID={ProcessId}，目标帧率={TargetFps}",
                _processId,
                _config.FpsUnlockTarget);

            if (!GenshinFpsUnlockHookScope.TryCreate(
                    hookModuleLease.ModulePath,
                    _gameWindowHandle,
                    _processId,
                    out hookScope,
                    out var hookError))
            {
                failureReason = $"安装原神窗口消息 Hook 失败，Win32 错误码 {hookError}";
                return new GenshinFpsUnlockSessionResult(failureReason, GameExited: false);
            }

            if (!hookScope.TryActivate(out hookError))
            {
                failureReason = $"触发原神窗口消息 Hook 失败，Win32 错误码 {hookError}";
                return new GenshinFpsUnlockSessionResult(failureReason, GameExited: false);
            }

            var injectionDeadline = DateTime.UtcNow + InjectionTimeout;
            while (DateTime.UtcNow < injectionDeadline)
            {
                token.ThrowIfCancellationRequested();
                if (HasExited(gameProcess))
                {
                    gameExited = true;
                    return new GenshinFpsUnlockSessionResult(null, GameExited: true);
                }

                var snapshot = hookChannel.ReadSnapshot();
                if (snapshot.State == GenshinFpsUnlockHookChannel.ReadyState)
                {
                    hasInjected = true;
                    var gameplayQoL =
                        gameplayQoLChannel?.ReadSnapshot();
                    PublishStatus(snapshot, gameplayQoL);
                    LogFogDiagnostics(snapshot);
                    LogFovForceModeDiagnostics(snapshot);
                    LogFovDiagnostics(snapshot);
                    LogGameplayQoLDiagnostics(gameplayQoL);
                    _logger.LogInformation(
                        "游戏内帧率 Hook 已就绪：PID={ProcessId}，字段RVA=0x{FramerateRva:X}，原值={OriginalFps}",
                        _processId,
                        snapshot.FramerateRva,
                        snapshot.OriginalFramerate);
                    break;
                }

                if (snapshot.State == GenshinFpsUnlockHookChannel.FailedState)
                {
                    failureReason = GetHookFailureReason(snapshot.ErrorCode);
                    return new GenshinFpsUnlockSessionResult(failureReason, GameExited: false);
                }

                await Task.Delay(50, token).ConfigureAwait(false);
            }

            if (!hasInjected)
            {
                failureReason = "游戏内帧率 Hook 在 15 秒内没有完成初始化";
                return new GenshinFpsUnlockSessionResult(failureReason, GameExited: false);
            }

            while (!token.IsCancellationRequested)
            {
                if (HasExited(gameProcess))
                {
                    gameExited = true;
                    break;
                }

                var targetFps = _config.FpsUnlockTarget;
                if (GenshinStartConfig.IsValidFpsUnlockTarget(targetFps))
                {
                    hookChannel.UpdateTargetFramerate(targetFps);
                }

                if (_config.IsFovTargetInputValid
                    && GenshinStartConfig.IsValidFovTarget(_config.FovTarget))
                {
                    hookChannel.UpdateFovTarget(_config.FovTarget);
                }

                hookChannel.UpdateFeatureFlags(
                    CreateFeatureFlags(isChineseClient));
                hookChannel.SignalHeartbeat();
                gameplayQoLChannel?.UpdateFeatureFlags(
                    CreateGameplayQoLFlags(
                        _config,
                        isChineseClient));
                gameplayQoLChannel?.SignalHeartbeat();
                var snapshot = hookChannel.ReadSnapshot();
                var gameplayQoL =
                    gameplayQoLChannel?.ReadSnapshot();
                PublishStatus(snapshot, gameplayQoL);
                LogFogDiagnostics(snapshot);
                LogFovForceModeDiagnostics(snapshot);
                LogFovDiagnostics(snapshot);
                LogGameplayQoLDiagnostics(gameplayQoL);
                await Task.Delay(WriteInterval, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // 开关关闭或程序退出属于正常停止，不需要提示失败。
        }
        catch (Exception exception)
        {
            failureReason = exception switch
            {
                Win32Exception win32Exception => $"安装原神帧率 Hook 失败：{win32Exception.Message}",
                _ => $"帧率解锁会话异常：{exception.Message}",
            };
            _logger.LogWarning(exception, "原神帧率解锁会话异常，PID={ProcessId}", _processId);
        }
        finally
        {
            hookScope?.Dispose();

            if (hookChannel is not null
                && hasInjected
                && !gameExited)
            {
                hookChannel.RequestStop();
                if (!await WaitForHookStoppedAsync(hookChannel).ConfigureAwait(false))
                {
                    restoreFailed = true;
                    _logger.LogWarning(
                        "原神帧率 Hook 未在限定时间内恢复原值：PID={ProcessId}",
                        _processId);
                }
                else
                {
                    _logger.LogInformation(
                        "已通知原神帧率 Hook 恢复原帧率：PID={ProcessId}，原值={OriginalFps}",
                        _processId,
                        hookChannel.ReadOriginalFramerate());
                }
            }

            if (gameplayQoLChannel is not null
                && hasInjected
                && !gameExited)
            {
                gameplayQoLChannel.RequestStop();
                if (!await WaitForGameplayQoLStoppedAsync(
                        gameplayQoLChannel)
                    .ConfigureAwait(false))
                {
                    _logger.LogWarning(
                        "游戏体验优化 Hook 未在限定时间内恢复：PID={ProcessId}",
                        _processId);
                }
            }

            hookChannel?.Dispose();
            gameplayQoLChannel?.Dispose();
            hookModuleLease?.Dispose();
        }

        if (gameExited)
        {
            _logger.LogInformation("原神已退出，帧率解锁会话结束：PID={ProcessId}", _processId);
        }
        else if (failureReason is null)
        {
            _logger.LogInformation("帧率解锁写入已停止：PID={ProcessId}", _processId);
        }

        if (failureReason is null && restoreFailed)
        {
            failureReason = "停止写入后恢复原帧率失败";
        }

        return new GenshinFpsUnlockSessionResult(
            failureReason,
            GameExited: gameExited,
            RestoreFailed: restoreFailed);
    }

    /// <summary>
    /// 生成只对国服有效的画面增强功能位。
    /// </summary>
    /// <param name="isChineseClient">目标进程是否为国服原神</param>
    /// <returns>共享状态中的功能位</returns>
    private int CreateFeatureFlags(bool isChineseClient)
    {
        return CreateFeatureFlags(_config, isChineseClient);
    }

    /// <summary>
    /// 生成不依赖帧率解锁总开关的画面与帧率功能位。
    /// </summary>
    /// <param name="config">当前原神启动配置</param>
    /// <param name="isChineseClient">目标进程是否为国服原神</param>
    /// <returns>共享状态中的功能位</returns>
    internal static int CreateFeatureFlags(
        GenshinStartConfig config,
        bool isChineseClient)
    {
        var flags = 0;
        if (config.FpsUnlockEnabled
            && GenshinStartConfig.IsValidFpsUnlockTarget(config.FpsUnlockTarget))
        {
            flags |= GenshinFpsUnlockHookChannel.FramerateFeatureFlag;
        }

        if (!isChineseClient)
        {
            return flags;
        }

        if (config.FogRemovalEnabled)
        {
            flags |= GenshinFpsUnlockHookChannel.FogRemovalFeatureFlag;
        }

        if (config.FovAdjustmentEnabled
            && config.IsFovTargetInputValid
            && GenshinStartConfig.IsValidFovTarget(config.FovTarget))
        {
            flags |= GenshinFpsUnlockHookChannel.FovAdjustmentFeatureFlag;
            if (config.FovForceModeEnabled)
            {
                flags |= GenshinFpsUnlockHookChannel.FovForceModeFeatureFlag;
            }
        }

        return flags;
    }

    /// <summary>
    /// 生成国服七项游戏体验优化功能位。
    /// </summary>
    /// <param name="config">当前原神启动配置</param>
    /// <param name="isChineseClient">目标进程是否为国服原神</param>
    /// <returns>独立共享状态中的功能位</returns>
    internal static int CreateGameplayQoLFlags(
        GenshinStartConfig config,
        bool isChineseClient)
    {
        if (!isChineseClient)
        {
            return 0;
        }

        var flags = 0;
        if (config.HideQuestBannerEnabled)
        {
            flags |= GenshinGameplayQoLChannel.HideQuestBannerFeatureFlag;
        }

        if (config.RemoveCameraMoveEnabled)
        {
            flags |= GenshinGameplayQoLChannel.RemoveCameraMoveFeatureFlag;
        }

        if (config.RemoveTeamProgressEnabled)
        {
            flags |= GenshinGameplayQoLChannel.RemoveTeamProgressFeatureFlag;
        }

        if (config.SpeedUpClockAnimationEnabled)
        {
            flags |= GenshinGameplayQoLChannel.SpeedUpClockAnimationFeatureFlag;
        }

        if (config.RemoveDiveMosaicEnabled)
        {
            flags |= GenshinGameplayQoLChannel.RemoveDiveMosaicFeatureFlag;
        }

        if (config.RedirectSynthesisEnabled)
        {
            flags |= GenshinGameplayQoLChannel.RedirectSynthesisFeatureFlag;
        }

        if (config.RemoveCharacterFadeEnabled)
        {
            flags |= GenshinGameplayQoLChannel.RemoveCharacterFadeFeatureFlag;
        }

        return flags;
    }

    /// <summary>
    /// 记录强制 FOV 模式在运行期的开启或关闭变化。
    /// </summary>
    /// <param name="snapshot">当前共享状态快照</param>
    private void LogFovForceModeDiagnostics(
        GenshinFpsUnlockSharedState snapshot)
    {
        var enabled =
            (snapshot.FeatureFlags
                & GenshinFpsUnlockHookChannel.FovForceModeFeatureFlag)
            != 0;
        var current = enabled ? 1 : 0;
        if (_lastLoggedFovForceMode < 0)
        {
            _lastLoggedFovForceMode = current;
            if (enabled)
            {
                _logger.LogInformation(
                    "FOV 强制模式已开启：PID={ProcessId}，目标={TargetFov}",
                    _processId,
                    snapshot.FovTarget);
            }

            return;
        }

        if (current == _lastLoggedFovForceMode)
        {
            return;
        }

        _lastLoggedFovForceMode = current;
        if (enabled)
        {
            _logger.LogInformation(
                "FOV 强制模式已开启：PID={ProcessId}，目标={TargetFov}",
                _processId,
                snapshot.FovTarget);
        }
        else
        {
            _logger.LogInformation(
                "FOV 强制模式已关闭，恢复普通视角替换：PID={ProcessId}",
                _processId);
        }
    }

    /// <summary>
    /// 把共享状态变化转换为宿主可订阅的状态事件。
    /// </summary>
    /// <param name="snapshot">当前共享状态快照</param>
    private void PublishStatus(
        GenshinFpsUnlockSharedState snapshot,
        GenshinGameplayQoLSharedState? gameplayQoL)
    {
        var status = new GenshinFpsUnlockRuntimeStatus(
            _processId,
            MapFeatureStatus(snapshot.FramerateStatus),
            MapFeatureStatus(snapshot.FogStatus),
            MapFeatureStatus(snapshot.FovStatus))
        {
            GameplayQoL = CreateGameplayQoLRuntimeStatus(gameplayQoL),
        };
        if (_lastPublishedStatus == status)
        {
            return;
        }

        _lastPublishedStatus = status;
        StatusChanged?.Invoke(this, status);
    }

    /// <summary>
    /// 把七项共享状态转换为公开的运行状态。
    /// </summary>
    /// <param name="snapshot">体验优化共享状态</param>
    /// <returns>七项公开状态</returns>
    private static GenshinGameplayQoLRuntimeStatus
        CreateGameplayQoLRuntimeStatus(
            GenshinGameplayQoLSharedState? snapshot)
    {
        if (snapshot is not { } gameplayQoL)
        {
            // 共享状态不可用时，七项功能全部按未请求展示，不影响主功能状态。
            return new GenshinGameplayQoLRuntimeStatus(
                GenshinFpsUnlockFeatureStatus.NotRequested,
                GenshinFpsUnlockFeatureStatus.NotRequested,
                GenshinFpsUnlockFeatureStatus.NotRequested,
                GenshinFpsUnlockFeatureStatus.NotRequested,
                GenshinFpsUnlockFeatureStatus.NotRequested,
                GenshinFpsUnlockFeatureStatus.NotRequested,
                GenshinFpsUnlockFeatureStatus.NotRequested);
        }

        return new GenshinGameplayQoLRuntimeStatus(
            MapFeatureStatus(gameplayQoL.Status[0]),
            MapFeatureStatus(gameplayQoL.Status[1]),
            MapFeatureStatus(gameplayQoL.Status[2]),
            MapFeatureStatus(gameplayQoL.Status[3]),
            MapFeatureStatus(gameplayQoL.Status[4]),
            MapFeatureStatus(gameplayQoL.Status[5]),
            MapFeatureStatus(gameplayQoL.Status[6]));
    }

    /// <summary>
    /// 在任一体验优化状态变化时记录一次诊断日志。
    /// </summary>
    /// <param name="snapshot">体验优化共享状态</param>
    private void LogGameplayQoLDiagnostics(
        GenshinGameplayQoLSharedState? snapshot)
    {
        if (snapshot is not { } gameplayQoL)
        {
            return;
        }

        var current = CreateGameplayQoLRuntimeStatus(gameplayQoL);
        var metrics = CreateGameplayQoLMetrics(gameplayQoL);
        var statusChanged =
            _lastLoggedGameplayQoLStatus != current;
        var metricsChanged =
            _lastLoggedGameplayQoLMetrics == null
            || !_lastLoggedGameplayQoLMetrics.SequenceEqual(metrics);
        var metricLogDue =
            DateTime.UtcNow - _lastGameplayQoLMetricLogAt
            >= TimeSpan.FromSeconds(2);
        if (!statusChanged
            && (!metricsChanged || !metricLogDue))
        {
            return;
        }

        _lastLoggedGameplayQoLStatus = current;
        _lastLoggedGameplayQoLMetrics = metrics;
        _lastGameplayQoLMetricLogAt = DateTime.UtcNow;
        _logger.LogInformation(
            "游戏体验优化状态更新：PID={ProcessId}，任务横幅={QuestBanner}，事件镜头={CameraMove}，队伍进度={TeamProgress}，调整时间={ClockAnimation}，水下马赛克={DiveMosaic}，合成重定向={Synthesis}，角色去虚化={CharacterFade}",
            _processId,
            current.HideQuestBanner,
            current.RemoveCameraMove,
            current.RemoveTeamProgress,
            current.SpeedUpClockAnimation,
            current.RemoveDiveMosaic,
            current.RedirectSynthesis,
            current.RemoveCharacterFade);
        _logger.LogInformation(
            "游戏体验优化诊断：PID={ProcessId}，错误码={ErrorCodes}，命中计数={HookHitCounts}，替换计数={ReplaceCounts}，未挂起线程={SkippedThreads}，RVA={FeatureRvas}",
            _processId,
            string.Join(",", gameplayQoL.ErrorCode),
            string.Join(",", gameplayQoL.HookHitCount),
            string.Join(",", gameplayQoL.ReplaceCount),
            gameplayQoL.Reserved[0],
            string.Join(
                ",",
                gameplayQoL.FeatureRva.Select(rva => $"0x{rva:X}")));
    }

    /// <summary>
    /// 复制七项体验优化计数，用于判断是否需要补写增量诊断日志。
    /// </summary>
    /// <param name="snapshot">体验优化共享状态</param>
    /// <returns>命中、替换和恢复计数组成的固定快照</returns>
    private static int[] CreateGameplayQoLMetrics(
        GenshinGameplayQoLSharedState snapshot)
    {
        var metrics = new int[
            GenshinGameplayQoLSharedState.FeatureCount * 3];
        Array.Copy(
            snapshot.HookHitCount,
            0,
            metrics,
            0,
            GenshinGameplayQoLSharedState.FeatureCount);
        Array.Copy(
            snapshot.ReplaceCount,
            0,
            metrics,
            GenshinGameplayQoLSharedState.FeatureCount,
            GenshinGameplayQoLSharedState.FeatureCount);
        Array.Copy(
            snapshot.RestoreCount,
            0,
            metrics,
            GenshinGameplayQoLSharedState.FeatureCount * 2,
            GenshinGameplayQoLSharedState.FeatureCount);
        return metrics;
    }

    /// <summary>
    /// 记录去雾参数复制 Hook 是否已命中并替换调用级临时参数。
    /// </summary>
    /// <param name="snapshot">当前共享状态快照</param>
    private void LogFogDiagnostics(GenshinFpsUnlockSharedState snapshot)
    {
        if (snapshot.FogStatus != GenshinFpsUnlockHookChannel.ReadyFeatureStatus)
        {
            _fogReadySince = DateTime.MinValue;
            _fogNoHitWarningLogged = false;
            if (snapshot.FogStatus != _lastFogStatus
                && snapshot.FogStatus == GenshinFpsUnlockHookChannel.FailedFeatureStatus)
            {
                _logger.LogWarning(
                    "去雾 Hook 启用失败：PID={ProcessId}，错误码={ErrorCode}，安装阶段={InstallStage}，系统错误={InstallError}，RVA=0x{FogRva:X}",
                    _processId,
                    snapshot.ErrorCode,
                    snapshot.FogContextFailureCount,
                    snapshot.FogLastLayer,
                    snapshot.FogRva);
            }
            _lastFogStatus = snapshot.FogStatus;
            return;
        }

        _lastFogStatus = snapshot.FogStatus;
        if (_fogReadySince == DateTime.MinValue)
        {
            _fogReadySince = DateTime.UtcNow;
        }

        if (_lastFogHookHitCount <= 0
            && snapshot.FogHookHitCount > 0)
        {
            _lastFogHookHitCount = snapshot.FogHookHitCount;
            _logger.LogInformation(
                "去雾参数复制 Hook 已命中：PID={ProcessId}，调用次数={HitCount}，临时参数替换次数={ChangeCount}，最近雾启用值={OriginalValue}，RVA=0x{FogRva:X}",
                _processId,
                snapshot.FogHookHitCount,
                snapshot.FogChangeCount,
                snapshot.FogLastEnabledValue,
                snapshot.FogRva);
        }

        if (_lastFogChangeCount <= 0
            && snapshot.FogChangeCount > 0)
        {
            _lastFogChangeCount = snapshot.FogChangeCount;
            _logger.LogInformation(
                "去雾参数复制 Hook 已实际替换调用级临时参数：PID={ProcessId}，替换次数={ChangeCount}",
                _processId,
                snapshot.FogChangeCount);
        }

        if (!_fogNoHitWarningLogged
            && snapshot.FogHookHitCount == 0
            && DateTime.UtcNow - _fogReadySince >= FogHookObservationWindow)
        {
            _fogNoHitWarningLogged = true;
            _logger.LogWarning(
                "去雾 Hook 已安装，但 {Seconds} 秒内没有命中雾参数复制函数：PID={ProcessId}，RVA=0x{FogRva:X}",
                FogHookObservationWindow.TotalSeconds,
                _processId,
                snapshot.FogRva);
        }
    }

    /// <summary>
    /// 记录 FOV 硬件断点状态、首次命中和首次替换。
    /// </summary>
    /// <param name="snapshot">当前共享状态快照</param>
    private void LogFovDiagnostics(GenshinFpsUnlockSharedState snapshot)
    {
        if (snapshot.FovStatus != GenshinFpsUnlockHookChannel.ReadyFeatureStatus)
        {
            _fovReadySince = DateTime.MinValue;
            _lastLoggedFovTarget = -1;
            if (snapshot.FovStatus != _lastFovStatus
                && snapshot.FovStatus == GenshinFpsUnlockHookChannel.FailedFeatureStatus)
            {
                _logger.LogWarning(
                    "FOV Hook 启用失败：PID={ProcessId}，错误码={ErrorCode}，安装阶段={InstallStage}，安装错误={InstallError}，RVA=0x{FovRva:X}",
                    _processId,
                    snapshot.ErrorCode,
                    snapshot.FovInstallStage,
                    snapshot.FovInstallError,
                    snapshot.FovRva);
            }

            _lastFovStatus = snapshot.FovStatus;
            return;
        }

        _lastFovStatus = snapshot.FovStatus;
        if (_fovReadySince == DateTime.MinValue)
        {
            _fovReadySince = DateTime.UtcNow;
            _logger.LogInformation(
                "FOV 硬件断点已安装：PID={ProcessId}，目标={TargetFov}，RVA=0x{FovRva:X}",
                _processId,
                snapshot.FovTarget,
                snapshot.FovRva);
            _lastLoggedFovTarget = snapshot.FovTarget;
        }
        else if (snapshot.FovTarget != _lastLoggedFovTarget)
        {
            _logger.LogInformation(
                "FOV 目标已更新：PID={ProcessId}，原目标={PreviousFov}，新目标={TargetFov}",
                _processId,
                _lastLoggedFovTarget,
                snapshot.FovTarget);
            _lastLoggedFovTarget = snapshot.FovTarget;
        }

        if (_lastFovHookHitCount <= 0
            && snapshot.FovHookHitCount > 0)
        {
            _lastFovHookHitCount = snapshot.FovHookHitCount;
            _logger.LogInformation(
                "FOV Hook 已命中：PID={ProcessId}，调用次数={HitCount}，替换次数={ReplaceCount}，目标={TargetFov}，RVA=0x{FovRva:X}",
                _processId,
                snapshot.FovHookHitCount,
                snapshot.FovReplaceCount,
                snapshot.FovTarget,
                snapshot.FovRva);
        }

        if (_lastFovReplaceCount <= 0
            && snapshot.FovReplaceCount > 0)
        {
            _lastFovReplaceCount = snapshot.FovReplaceCount;
            _logger.LogInformation(
                "FOV Hook 已实际替换视野参数：PID={ProcessId}，替换次数={ReplaceCount}，目标={TargetFov}",
                _processId,
                snapshot.FovReplaceCount,
                snapshot.FovTarget);
        }

        if (!_fovNoHitWarningLogged
            && snapshot.FovHookHitCount == 0
            && DateTime.UtcNow - _fovReadySince >= FovHookObservationWindow)
        {
            _fovNoHitWarningLogged = true;
            _logger.LogWarning(
                "FOV Hook 已安装，但 {Seconds} 秒内没有命中 FOV 写入函数：PID={ProcessId}，RVA=0x{FovRva:X}",
                FovHookObservationWindow.TotalSeconds,
                _processId,
                snapshot.FovRva);
        }
    }

    /// <summary>
    /// 把共享内存中的整数状态转换为公开枚举。
    /// </summary>
    /// <param name="status">共享内存状态值</param>
    /// <returns>公开功能状态</returns>
    private static GenshinFpsUnlockFeatureStatus MapFeatureStatus(int status)
    {
        return status switch
        {
            GenshinFpsUnlockHookChannel.ReadyFeatureStatus => GenshinFpsUnlockFeatureStatus.Ready,
            GenshinFpsUnlockHookChannel.FailedFeatureStatus => GenshinFpsUnlockFeatureStatus.Failed,
            GenshinFpsUnlockHookChannel.StoppedFeatureStatus => GenshinFpsUnlockFeatureStatus.Stopped,
            _ => GenshinFpsUnlockFeatureStatus.NotRequested,
        };
    }

    /// <summary>
    /// 等待游戏内 Hook 完成原帧率恢复。
    /// </summary>
    /// <param name="hookChannel">当前共享内存通道</param>
    /// <returns>在限定时间内恢复时返回 true</returns>
    private static async Task<bool> WaitForHookStoppedAsync(
        GenshinFpsUnlockHookChannel hookChannel)
    {
        var deadline = DateTime.UtcNow + RestoreTimeout;
        while (DateTime.UtcNow < deadline)
        {
            if (hookChannel.IsStopped())
            {
                return true;
            }

            await Task.Delay(50).ConfigureAwait(false);
        }

        return hookChannel.IsStopped();
    }

    /// <summary>
    /// 等待游戏内工作线程恢复并停止七项体验优化功能。
    /// </summary>
    /// <param name="channel">体验优化共享内存通道</param>
    /// <returns>在限定时间内停止时返回 true</returns>
    private static async Task<bool> WaitForGameplayQoLStoppedAsync(
        GenshinGameplayQoLChannel channel)
    {
        var deadline = DateTime.UtcNow + RestoreTimeout;
        while (DateTime.UtcNow < deadline)
        {
            if (channel.IsStopped())
            {
                return true;
            }

            await Task.Delay(50).ConfigureAwait(false);
        }

        return channel.IsStopped();
    }

    /// <summary>
    /// 把游戏内 Hook 错误码转换为可读失败原因。
    /// </summary>
    /// <param name="errorCode">Hook DLL 返回的错误码</param>
    /// <returns>中文失败原因</returns>
    private static string GetHookFailureReason(int errorCode)
    {
        return errorCode switch
        {
            1 => "Hook 目标不是原神进程",
            2 => "无法打开帧率解锁共享内存",
            3 => "无法映射帧率解锁共享内存",
            4 => "原神主模块中找不到所需代码节",
            5 => "原神主模块中找不到帧率特征或可写字段",
            6 => "原神帧率字段不可写",
            7 => "共享状态与目标游戏进程不匹配",
            8 => "运行期 Hook 版本不匹配",
            9 => "当前国服版本中找不到唯一的雾参数复制特征",
            10 => "安装雾参数复制内联 Hook 失败",
            11 => "恢复游戏原始雾参数复制函数失败",
            12 => "恢复游戏原始代码或画面状态失败",
            13 => "当前国服版本中找不到唯一的 FOV 写入特征",
            14 => "安装 FOV 硬件断点失败",
            15 => "恢复 FOV 硬件断点失败",
            _ => $"游戏内帧率 Hook 失败，错误码 {errorCode}",
        };
    }

    /// <summary>
    /// 确认目标进程名属于国服或国际服原神。
    /// </summary>
    /// <param name="process">目标进程</param>
    /// <returns>属于本机原神时返回 true</returns>
    private static bool IsLocalGenshinProcess(Process process)
    {
        var processName = process.ProcessName;
        return string.Equals(processName, "YuanShen", StringComparison.OrdinalIgnoreCase)
               || string.Equals(processName, "GenshinImpact", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 确认目标进程属于国服客户端；画面增强只在该客户端启用。
    /// </summary>
    /// <param name="process">目标进程</param>
    /// <returns>国服原神返回 true</returns>
    private static bool IsChineseClientProcess(Process process)
    {
        return string.Equals(
            process.ProcessName,
            "YuanShen",
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 安全地检查进程是否已经退出。
    /// </summary>
    /// <param name="process">目标进程</param>
    /// <returns>进程已退出或无法继续查询时返回 true</returns>
    private static bool HasExited(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
        catch (Win32Exception)
        {
            return true;
        }
    }
}
