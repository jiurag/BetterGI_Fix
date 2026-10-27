using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.Service.I18n;
using BetterGenshinImpact.Service.Interface;
using BetterGenshinImpact.View.Windows;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;

namespace BetterGenshinImpact.GameTask.Runtime.Win32;

/// <summary>
/// 管理由 BetterGI 启动的原神帧率解锁会话。
/// </summary>
public sealed class GenshinFpsUnlockService : IDisposable, IAsyncDisposable
{
    private readonly object _syncLocker = new();
    private readonly GenshinStartConfig _config;
    private readonly ILogger<GenshinFpsUnlockService> _logger;
    private readonly HashSet<int> _attemptedProcessIds = [];
    private readonly HashSet<int> _notifiedFailureProcessIds = [];
    private GenshinFpsUnlockSession? _activeSession;
    private bool _disposed;

    /// <summary>
    /// 创建帧率解锁服务并订阅开关变化。
    /// </summary>
    /// <param name="configService">配置服务</param>
    /// <param name="logger">日志记录器</param>
    public GenshinFpsUnlockService(
        IConfigService configService,
        ILogger<GenshinFpsUnlockService> logger)
    {
        _logger = logger;
        _config = configService.Get().GenshinStartConfig;
        _config.PropertyChanged += OnConfigPropertyChanged;
    }

    /// <summary>
    /// 尝试为刚由 BetterGI 拉起的原神进程创建解锁会话。
    /// </summary>
    /// <param name="processId">原神进程 ID</param>
    /// <param name="gameWindowHandle">原神主窗口句柄</param>
    /// <returns>创建了新的解锁会话时返回 true</returns>
    public bool TryStart(int processId, nint gameWindowHandle)
    {
        if (processId <= 0 || gameWindowHandle == 0)
        {
            return false;
        }

        GenshinFpsUnlockSession session;
        lock (_syncLocker)
        {
            if (_disposed)
            {
                return false;
            }

            if (!_config.FpsUnlockEnabled)
            {
                return false;
            }

            if (!_config.IsFpsUnlockTargetInputValid
                || !GenshinStartConfig.IsValidFpsUnlockTarget(_config.FpsUnlockTarget))
            {
                _logger.LogWarning("帧率解锁目标输入无效，本次启动不启用解锁：PID={ProcessId}", processId);
                return false;
            }

            if (_activeSession is not null)
            {
                if (_activeSession.ProcessId == processId || !_activeSession.Completion.IsCompleted)
                {
                    return false;
                }
            }

            if (!_attemptedProcessIds.Add(processId))
            {
                return false;
            }

            session = new GenshinFpsUnlockSession(
                processId,
                gameWindowHandle,
                _config,
                _logger);
            _activeSession = session;
            session.Start();
        }

        _logger.LogInformation(
            "原神由 BetterGI 启动，准备解锁帧率：PID={ProcessId}，目标帧率={TargetFps}",
            processId,
            _config.FpsUnlockTarget);
        _ = ObserveSessionAsync(session);
        return true;
    }

    /// <summary>
    /// 请求当前解锁会话停止；仅供开关关闭和正常释放流程使用。
    /// </summary>
    /// <returns>等待后台会话结束的任务</returns>
    public async Task StopAsync()
    {
        GenshinFpsUnlockSession? session;
        lock (_syncLocker)
        {
            session = _activeSession;
        }

        if (session is null)
        {
            return;
        }

        session.RequestStop();
        try
        {
            await session.Completion.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "等待帧率解锁会话结束时发生异常：PID={ProcessId}", session.ProcessId);
        }
    }

    /// <summary>
    /// 等待会话结束、清理当前会话并处理一次性失败提示。
    /// </summary>
    /// <param name="session">刚启动的会话</param>
    /// <returns>异步监视任务</returns>
    private async Task ObserveSessionAsync(GenshinFpsUnlockSession session)
    {
        try
        {
            var result = await session.Completion.ConfigureAwait(false);
            lock (_syncLocker)
            {
                if (ReferenceEquals(_activeSession, session))
                {
                    _activeSession = null;
                }
            }

            if (result.FailureReason is { } failureReason)
            {
                _logger.LogWarning(
                    "原神帧率解锁结束：PID={ProcessId}，失败原因={FailureReason}，游戏已退出={GameExited}",
                    session.ProcessId,
                    failureReason,
                    result.GameExited);
                await ShowFailureOnceAsync(session.ProcessId, failureReason).ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "监视原神帧率解锁会话失败：PID={ProcessId}", session.ProcessId);
        }
        finally
        {
            await session.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 同一进程只显示一次解锁失败提示。
    /// </summary>
    /// <param name="processId">原神进程 ID</param>
    /// <param name="failureReason">失败原因</param>
    /// <returns>提示处理任务</returns>
    private async Task ShowFailureOnceAsync(int processId, string failureReason)
    {
        lock (_syncLocker)
        {
            if (_disposed || !_notifiedFailureProcessIds.Add(processId))
            {
                return;
            }
        }

        try
        {
            await ThemedMessageBox.WarningAsync(
                    I18nService.Instance.Translate("帧率解锁失败：{0}\n\n原神仍会正常运行。")
                        .Replace("{0}", failureReason),
                    I18nService.Instance.Translate("BetterGI 帧率解锁"))
                .ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "显示帧率解锁失败提示失败：PID={ProcessId}", processId);
        }
    }

    /// <summary>
    /// 开关关闭时请求停止当前会话。
    /// </summary>
    /// <param name="sender">配置对象</param>
    /// <param name="e">属性变化参数</param>
    private void OnConfigPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GenshinStartConfig.FpsUnlockEnabled)
            && !_config.FpsUnlockEnabled)
        {
            _ = StopAsync();
        }
    }

    /// <summary>
    /// 同步释放服务；退出流程只做短时间等待。
    /// </summary>
    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    /// <summary>
    /// 取消并等待当前会话结束，然后解除配置订阅。
    /// </summary>
    /// <returns>异步释放任务</returns>
    public async ValueTask DisposeAsync()
    {
        GenshinFpsUnlockSession? session;
        lock (_syncLocker)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _config.PropertyChanged -= OnConfigPropertyChanged;
            session = _activeSession;
        }

        if (session is null)
        {
            return;
        }

        session.RequestStop();
        try
        {
            await session.Completion.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("等待帧率解锁会话停止超时：PID={ProcessId}", session.ProcessId);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "停止帧率解锁会话失败：PID={ProcessId}", session.ProcessId);
        }

        await session.DisposeAsync().ConfigureAwait(false);
    }
}
