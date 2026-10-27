using System.Runtime.InteropServices;

namespace BetterGenshinImpact.GameTask.Runtime.Win32;

/// <summary>
/// BetterGI 与游戏内 Hook DLL 共用的四字节字段状态。
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct GenshinFpsUnlockSharedState
{
    /// <summary>
    /// Hook 当前阶段
    /// </summary>
    public int State;

    /// <summary>
    /// Hook 失败原因代码
    /// </summary>
    public int ErrorCode;

    /// <summary>
    /// 目标原神进程 ID
    /// </summary>
    public int GameProcessId;

    /// <summary>
    /// 当前目标帧率
    /// </summary>
    public int TargetFramerate;

    /// <summary>
    /// 运行命令；1 表示写入，0 表示恢复并停止
    /// </summary>
    public int Command;

    /// <summary>
    /// Hook DLL 找到的动态帧率原值
    /// </summary>
    public int OriginalFramerate;

    /// <summary>
    /// 动态帧率字段相对游戏主模块的 RVA
    /// </summary>
    public int FramerateRva;

    /// <summary>
    /// BetterGI 定期更新的心跳计数
    /// </summary>
    public int Heartbeat;
}
