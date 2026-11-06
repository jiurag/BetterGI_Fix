using System;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Threading;

namespace BetterGenshinImpact.GameTask.Runtime.Win32;

/// <summary>
/// 通过命名共享内存与游戏内 Hook DLL 交换帧率和停止命令。
/// </summary>
internal sealed class GenshinFpsUnlockHookChannel : IDisposable
{
    /// <summary>
    /// Hook 尚未完成初始化
    /// </summary>
    public const int PendingState = 0;

    /// <summary>
    /// Hook 已经找到字段并开始写入
    /// </summary>
    public const int ReadyState = 1;

    /// <summary>
    /// Hook 初始化失败
    /// </summary>
    public const int FailedState = 2;

    /// <summary>
    /// Hook 已恢复原帧率并停止
    /// </summary>
    public const int StoppedState = 3;

    /// <summary>
    /// 当前共享状态布局版本
    /// </summary>
    public const int CurrentLayoutVersion = 6;

    /// <summary>
    /// 去雾功能位
    /// </summary>
    public const int FogRemovalFeatureFlag = 1 << 0;

    /// <summary>
    /// 帧率解锁功能位
    /// </summary>
    public const int FramerateFeatureFlag = 1 << 1;

    /// <summary>
    /// FOV 调整功能位
    /// </summary>
    public const int FovAdjustmentFeatureFlag = 1 << 2;

    /// <summary>
    /// 强制所有经过 FOV Hook 的镜头写入使用目标值
    /// </summary>
    public const int FovForceModeFeatureFlag = 1 << 3;

    /// <summary>
    /// 功能尚未请求
    /// </summary>
    public const int NotRequestedFeatureStatus = 0;

    /// <summary>
    /// 功能已经启用
    /// </summary>
    public const int ReadyFeatureStatus = 1;

    /// <summary>
    /// 功能初始化失败
    /// </summary>
    public const int FailedFeatureStatus = 2;

    /// <summary>
    /// 功能已经恢复并停止
    /// </summary>
    public const int StoppedFeatureStatus = 3;

    /// <summary>
    /// 允许 Hook 持续写入
    /// </summary>
    private const int RunCommand = 1;

    /// <summary>
    /// 请求 Hook 恢复原值并停止
    /// </summary>
    private const int StopCommand = 0;

    /// <summary>
    /// 共享内存名称前缀
    /// </summary>
    private const string MappingNamePrefix = "Local\\UnityPlayer.RuntimeState.";

    /// <summary>
    /// 状态结构的内存偏移
    /// </summary>
    private static readonly int StateOffset = Marshal.OffsetOf<GenshinFpsUnlockSharedState>(nameof(GenshinFpsUnlockSharedState.State)).ToInt32();

    /// <summary>
    /// 原帧率字段的内存偏移
    /// </summary>
    private static readonly int OriginalFramerateOffset = Marshal.OffsetOf<GenshinFpsUnlockSharedState>(nameof(GenshinFpsUnlockSharedState.OriginalFramerate)).ToInt32();

    /// <summary>
    /// 帧率字段 RVA 的内存偏移
    /// </summary>
    private static readonly int FramerateRvaOffset = Marshal.OffsetOf<GenshinFpsUnlockSharedState>(nameof(GenshinFpsUnlockSharedState.FramerateRva)).ToInt32();

    /// <summary>
    /// 目标帧率字段的内存偏移
    /// </summary>
    private static readonly int TargetFramerateOffset = Marshal.OffsetOf<GenshinFpsUnlockSharedState>(nameof(GenshinFpsUnlockSharedState.TargetFramerate)).ToInt32();

    /// <summary>
    /// 命令字段的内存偏移
    /// </summary>
    private static readonly int CommandOffset = Marshal.OffsetOf<GenshinFpsUnlockSharedState>(nameof(GenshinFpsUnlockSharedState.Command)).ToInt32();

    /// <summary>
    /// 心跳字段的内存偏移
    /// </summary>
    private static readonly int HeartbeatOffset = Marshal.OffsetOf<GenshinFpsUnlockSharedState>(nameof(GenshinFpsUnlockSharedState.Heartbeat)).ToInt32();

    /// <summary>
    /// 画面增强功能位的内存偏移
    /// </summary>
    private static readonly int FeatureFlagsOffset = Marshal.OffsetOf<GenshinFpsUnlockSharedState>(nameof(GenshinFpsUnlockSharedState.FeatureFlags)).ToInt32();

    /// <summary>
    /// FOV 目标值的内存偏移
    /// </summary>
    private static readonly int FovTargetOffset = Marshal.OffsetOf<GenshinFpsUnlockSharedState>(nameof(GenshinFpsUnlockSharedState.FovTarget)).ToInt32();

    /// <summary>
    /// 共享内存对象
    /// </summary>
    private readonly MemoryMappedFile _memoryMappedFile;

    /// <summary>
    /// 共享内存写入视图
    /// </summary>
    private readonly MemoryMappedViewAccessor _viewAccessor;

    /// <summary>
    /// 当前进程内的心跳计数
    /// </summary>
    private int _heartbeat;

    /// <summary>
    /// 创建指定原神进程的共享内存并写入初始状态。
    /// </summary>
    /// <param name="processId">原神进程 ID</param>
    /// <param name="targetFramerate">初始目标帧率</param>
    /// <param name="featureFlags">初始运行期功能位</param>
    /// <param name="fovTarget">初始 FOV 目标</param>
    public GenshinFpsUnlockHookChannel(
        int processId,
        int targetFramerate,
        int featureFlags,
        int fovTarget)
    {
        MappingName = $"{MappingNamePrefix}{processId}";
        var initialState = new GenshinFpsUnlockSharedState
        {
            State = PendingState,
            ErrorCode = 0,
            GameProcessId = processId,
            TargetFramerate = targetFramerate,
            Command = RunCommand,
            OriginalFramerate = 0,
            FramerateRva = 0,
            Heartbeat = 1,
            LayoutVersion = CurrentLayoutVersion,
            FeatureFlags = featureFlags,
            FramerateStatus = NotRequestedFeatureStatus,
            FogStatus = NotRequestedFeatureStatus,
            FogRva = 0,
            FogHookHitCount = 0,
            FogContextFailureCount = 0,
            FogChangeCount = 0,
            FogLastEnabledValue = 0,
            FogLastLayer = 0,
            FovStatus = NotRequestedFeatureStatus,
            FovRva = 0,
            FovTarget = fovTarget,
            FovHookHitCount = 0,
            FovReplaceCount = 0,
            FovInstallStage = 0,
            FovInstallError = 0,
        };

        _memoryMappedFile = MemoryMappedFile.CreateNew(
            MappingName,
            Marshal.SizeOf<GenshinFpsUnlockSharedState>(),
            MemoryMappedFileAccess.ReadWrite);
        _viewAccessor = _memoryMappedFile.CreateViewAccessor(
            0,
            Marshal.SizeOf<GenshinFpsUnlockSharedState>(),
            MemoryMappedFileAccess.ReadWrite);
        _viewAccessor.Write(0, ref initialState);
        _heartbeat = 1;
    }

    /// <summary>
    /// 当前共享内存名称
    /// </summary>
    public string MappingName { get; }

    /// <summary>
    /// 读取一份当前 Hook 状态快照。
    /// </summary>
    /// <returns>当前共享状态</returns>
    public GenshinFpsUnlockSharedState ReadSnapshot()
    {
        _viewAccessor.Read(0, out GenshinFpsUnlockSharedState snapshot);
        return snapshot;
    }

    /// <summary>
    /// 更新 Hook 下一次写入的目标帧率。
    /// </summary>
    /// <param name="targetFramerate">目标帧率</param>
    public void UpdateTargetFramerate(int targetFramerate)
    {
        _viewAccessor.Write(TargetFramerateOffset, targetFramerate);
    }

    /// <summary>
    /// 更新运行期功能位。
    /// </summary>
    /// <param name="featureFlags">运行期功能位</param>
    public void UpdateFeatureFlags(int featureFlags)
    {
        _viewAccessor.Write(FeatureFlagsOffset, featureFlags);
    }

    /// <summary>
    /// 更新 Hook 下一次替换使用的 FOV 目标。
    /// </summary>
    /// <param name="fovTarget">合法 FOV 目标</param>
    public void UpdateFovTarget(int fovTarget)
    {
        _viewAccessor.Write(FovTargetOffset, fovTarget);
    }

    /// <summary>
    /// 更新心跳，避免 BetterGI 异常退出后 Hook 永久写入。
    /// </summary>
    public void SignalHeartbeat()
    {
        _viewAccessor.Write(
            HeartbeatOffset,
            Interlocked.Increment(ref _heartbeat));
    }

    /// <summary>
    /// 请求 Hook DLL 恢复原帧率并停止写入。
    /// </summary>
    public void RequestStop()
    {
        _viewAccessor.Write(CommandOffset, StopCommand);
    }

    /// <summary>
    /// 读取 Hook 是否已经写入准备完成状态。
    /// </summary>
    /// <returns>准备完成时返回 true</returns>
    public bool IsReady()
    {
        return _viewAccessor.ReadInt32(StateOffset) == ReadyState;
    }

    /// <summary>
    /// 读取 Hook 是否已经停止。
    /// </summary>
    /// <returns>已经停止时返回 true</returns>
    public bool IsStopped()
    {
        return _viewAccessor.ReadInt32(StateOffset) == StoppedState;
    }

    /// <summary>
    /// 读取 Hook DLL 记录的动态帧率原值。
    /// </summary>
    /// <returns>动态帧率原值</returns>
    public int ReadOriginalFramerate()
    {
        return _viewAccessor.ReadInt32(OriginalFramerateOffset);
    }

    /// <summary>
    /// 读取动态帧率字段 RVA。
    /// </summary>
    /// <returns>动态帧率字段 RVA</returns>
    public int ReadFramerateRva()
    {
        return _viewAccessor.ReadInt32(FramerateRvaOffset);
    }

    /// <summary>
    /// 释放共享内存视图和对象。
    /// </summary>
    public void Dispose()
    {
        _viewAccessor.Dispose();
        _memoryMappedFile.Dispose();
    }
}
