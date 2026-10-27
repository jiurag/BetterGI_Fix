using BetterGenshinImpact.Core.Config;
using Microsoft.Extensions.Logging;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
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
        GenshinFpsUnlockHookScope? hookScope = null;
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

            var hookDllPath = Path.Combine(AppContext.BaseDirectory, HookDllName);
            if (!File.Exists(hookDllPath))
            {
                failureReason = $"缺少帧率 Hook DLL：{hookDllPath}";
                return new GenshinFpsUnlockSessionResult(failureReason, GameExited: false);
            }

            hookChannel = new GenshinFpsUnlockHookChannel(
                _processId,
                _config.FpsUnlockTarget);

            _logger.LogInformation(
                "开始向原神进程安装帧率 Hook：PID={ProcessId}，目标帧率={TargetFps}",
                _processId,
                _config.FpsUnlockTarget);

            if (!GenshinFpsUnlockHookScope.TryCreate(
                    hookDllPath,
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

                hookChannel.SignalHeartbeat();
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

            hookChannel?.Dispose();
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
            4 => "原神主模块中找不到 il2cpp 节",
            5 => "原神主模块中找不到帧率特征或可写字段",
            6 => "原神帧率字段不可写",
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
