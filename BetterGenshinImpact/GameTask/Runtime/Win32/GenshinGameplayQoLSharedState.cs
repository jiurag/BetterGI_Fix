using System.Runtime.InteropServices;

namespace BetterGenshinImpact.GameTask.Runtime.Win32;

/// <summary>
/// BetterGI 与游戏内 DLL 共用的游戏体验优化状态。
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
internal struct GenshinGameplayQoLSharedState
{
    /// <summary>
    /// 功能数量。
    /// </summary>
    public const int FeatureCount = 6;

    /// <summary>
    /// 共享状态签名。
    /// </summary>
    public int Magic;

    /// <summary>
    /// 共享状态布局版本。
    /// </summary>
    public int LayoutVersion;

    /// <summary>
    /// 目标原神进程 ID。
    /// </summary>
    public int GameProcessId;

    /// <summary>
    /// BetterGI 定期更新的心跳计数。
    /// </summary>
    public int Heartbeat;

    /// <summary>
    /// 运行命令；1 表示运行，0 表示恢复并停止。
    /// </summary>
    public int Command;

    /// <summary>
    /// 六项游戏体验优化功能位。
    /// </summary>
    public int FeatureFlags;

    /// <summary>
    /// 每项功能的安装状态。
    /// </summary>
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = FeatureCount)]
    public int[] Status;

    /// <summary>
    /// 每项功能的错误码。
    /// </summary>
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = FeatureCount)]
    public int[] ErrorCode;

    /// <summary>
    /// 每项功能的入口命中次数。
    /// </summary>
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = FeatureCount)]
    public int[] HookHitCount;

    /// <summary>
    /// 每项功能的实际替换或隐藏次数。
    /// </summary>
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = FeatureCount)]
    public int[] ReplaceCount;

    /// <summary>
    /// 每项功能的恢复次数。
    /// </summary>
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = FeatureCount)]
    public int[] RestoreCount;

    /// <summary>
    /// 每项功能命中的游戏主模块 RVA。
    /// </summary>
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = FeatureCount)]
    public int[] FeatureRva;

    /// <summary>
    /// 预留字段，使共享状态固定为 256 字节。
    /// </summary>
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 22)]
    public int[] Reserved;
}
