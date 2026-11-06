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

    /// <summary>
    /// 共享状态布局版本；用于阻止新旧 DLL 错配后误写字段
    /// </summary>
    public int LayoutVersion;

    /// <summary>
    /// 运行期功能位；第 0 位为去雾，第 1 位为帧率解锁，第 2 位为 FOV 调整，
    /// 第 3 位为强制 FOV 模式
    /// </summary>
    public int FeatureFlags;

    /// <summary>
    /// 帧率功能状态
    /// </summary>
    public int FramerateStatus;

    /// <summary>
    /// 去雾功能状态
    /// </summary>
    public int FogStatus;

    /// <summary>
    /// 已定位的雾参数复制函数相对游戏主模块的 RVA；未定位时为 0
    /// </summary>
    public int FogRva;

    /// <summary>
    /// 雾参数复制 Hook 被调用的次数
    /// </summary>
    public int FogHookHitCount;

    /// <summary>
    /// 无法安全读取雾参数源数据的次数
    /// </summary>
    public int FogContextFailureCount;

    /// <summary>
    /// Hook 实际把调用级临时副本中的雾启用值改为 false 的次数
    /// </summary>
    public int FogChangeCount;

    /// <summary>
    /// 最近一次读取到的雾启用原始值
    /// </summary>
    public int FogLastEnabledValue;

    /// <summary>
    /// 最近一次雾参数块末字节；安装失败时复用为系统错误码
    /// </summary>
    public int FogLastLayer;

    /// <summary>
    /// FOV 功能状态
    /// </summary>
    public int FovStatus;

    /// <summary>
    /// 已定位的 FOV 写入函数相对游戏主模块的 RVA；未定位时为 0
    /// </summary>
    public int FovRva;

    /// <summary>
    /// 当前合法 FOV 目标值
    /// </summary>
    public int FovTarget;

    /// <summary>
    /// FOV 写入函数入口被命中的次数
    /// </summary>
    public int FovHookHitCount;

    /// <summary>
    /// FOV 参数实际被替换的次数
    /// </summary>
    public int FovReplaceCount;

    /// <summary>
    /// 最近一次 FOV 硬件断点安装或恢复失败阶段
    /// </summary>
    public int FovInstallStage;

    /// <summary>
    /// 最近一次 FOV 硬件断点安装或恢复错误码
    /// </summary>
    public int FovInstallError;
}
