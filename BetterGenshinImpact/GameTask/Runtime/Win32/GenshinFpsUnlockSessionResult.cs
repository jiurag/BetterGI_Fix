namespace BetterGenshinImpact.GameTask.Runtime.Win32;

/// <summary>
/// 帧率解锁会话的结束状态。
/// </summary>
/// <param name="FailureReason">失败原因；正常停止时为 null</param>
/// <param name="GameExited">会话结束时游戏是否已经退出</param>
/// <param name="RestoreFailed">是否未能恢复接管前的原帧率</param>
internal readonly record struct GenshinFpsUnlockSessionResult(
    string? FailureReason,
    bool GameExited,
    bool RestoreFailed = false);
