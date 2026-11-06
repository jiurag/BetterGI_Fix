namespace BetterGenshinImpact.GameTask.Runtime.Win32;

/// <summary>
/// 游戏内帧率与画面增强功能的运行状态。
/// </summary>
public enum GenshinFpsUnlockFeatureStatus
{
    /// <summary>
    /// 当前会话没有请求该功能。
    /// </summary>
    NotRequested,

    /// <summary>
    /// 功能已经启用。
    /// </summary>
    Ready,

    /// <summary>
    /// 功能初始化或恢复失败。
    /// </summary>
    Failed,

    /// <summary>
    /// 功能已经恢复并停止。
    /// </summary>
    Stopped,
}

/// <summary>
/// Snap 同类六项游戏体验优化功能的运行状态。
/// </summary>
/// <param name="HideQuestBanner">隐藏地图任务横幅状态</param>
/// <param name="RemoveCameraMove">移除事件镜头状态</param>
/// <param name="RemoveTeamProgress">移除队伍配置进度条状态</param>
/// <param name="SpeedUpClockAnimation">加速调整时间动画状态</param>
/// <param name="RemoveDiveMosaic">移除水下马赛克状态</param>
/// <param name="RedirectSynthesis">合成入口重定向状态</param>
public readonly record struct GenshinGameplayQoLRuntimeStatus(
    GenshinFpsUnlockFeatureStatus HideQuestBanner,
    GenshinFpsUnlockFeatureStatus RemoveCameraMove,
    GenshinFpsUnlockFeatureStatus RemoveTeamProgress,
    GenshinFpsUnlockFeatureStatus SpeedUpClockAnimation,
    GenshinFpsUnlockFeatureStatus RemoveDiveMosaic,
    GenshinFpsUnlockFeatureStatus RedirectSynthesis);

/// <summary>
/// 一次游戏内 Hook 会话对外公开的功能状态快照。
/// </summary>
/// <param name="ProcessId">原神进程 ID</param>
/// <param name="Framerate">帧率功能状态</param>
/// <param name="FogRemoval">去雾功能状态</param>
/// <param name="FovAdjustment">FOV 功能状态</param>
public readonly record struct GenshinFpsUnlockRuntimeStatus(
    int ProcessId,
    GenshinFpsUnlockFeatureStatus Framerate,
    GenshinFpsUnlockFeatureStatus FogRemoval,
    GenshinFpsUnlockFeatureStatus FovAdjustment)
{
    /// <summary>
    /// 六项游戏体验优化功能状态。
    /// </summary>
    public GenshinGameplayQoLRuntimeStatus GameplayQoL { get; init; }
}
