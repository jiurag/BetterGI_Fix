using System;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using System.Threading;

namespace BetterGenshinImpact.GameTask.Runtime.Win32;

/// <summary>
/// 通过独立共享内存控制游戏内七项体验优化功能。
/// </summary>
internal sealed class GenshinGameplayQoLChannel : IDisposable
{
    /// <summary>
    /// 共享状态签名。
    /// </summary>
    private const int SharedStateMagic = 0x31514742;

    /// <summary>
    /// 当前共享状态布局版本。
    /// </summary>
    public const int CurrentLayoutVersion = 2;

    /// <summary>
    /// 隐藏地图任务横幅。
    /// </summary>
    public const int HideQuestBannerFeatureFlag = 1 << 0;

    /// <summary>
    /// 移除事件镜头。
    /// </summary>
    public const int RemoveCameraMoveFeatureFlag = 1 << 1;

    /// <summary>
    /// 移除队伍配置进度条。
    /// </summary>
    public const int RemoveTeamProgressFeatureFlag = 1 << 2;

    /// <summary>
    /// 加速调整时间动画。
    /// </summary>
    public const int SpeedUpClockAnimationFeatureFlag = 1 << 3;

    /// <summary>
    /// 移除水下马赛克。
    /// </summary>
    public const int RemoveDiveMosaicFeatureFlag = 1 << 4;

    /// <summary>
    /// 合成入口重定向。
    /// </summary>
    public const int RedirectSynthesisFeatureFlag = 1 << 5;

    /// <summary>
    /// 角色去虚化。
    /// </summary>
    public const int RemoveCharacterFadeFeatureFlag = 1 << 6;

    /// <summary>
    /// 功能尚未请求。
    /// </summary>
    public const int NotRequestedFeatureStatus = 0;

    /// <summary>
    /// 功能已经启用。
    /// </summary>
    public const int ReadyFeatureStatus = 1;

    /// <summary>
    /// 功能安装失败。
    /// </summary>
    public const int FailedFeatureStatus = 2;

    /// <summary>
    /// 功能已经恢复并停止。
    /// </summary>
    public const int StoppedFeatureStatus = 3;

    /// <summary>
    /// 允许游戏内工作线程继续运行。
    /// </summary>
    private const int RunCommand = 1;

    /// <summary>
    /// 请求游戏内工作线程恢复功能并停止。
    /// </summary>
    private const int StopCommand = 0;

    /// <summary>
    /// 共享内存名称前缀。
    /// </summary>
    private const string MappingNamePrefix = "Local\\BetterGI.GameplayQoL.";

    /// <summary>
    /// 预留数组元素数量。
    /// </summary>
    private const int ReservedCount = 16;

    /// <summary>
    /// 共享状态签名偏移。
    /// </summary>
    private static readonly int MagicOffset =
        Marshal.OffsetOf<GenshinGameplayQoLSharedState>(
            nameof(GenshinGameplayQoLSharedState.Magic)).ToInt32();

    /// <summary>
    /// 布局版本偏移。
    /// </summary>
    private static readonly int LayoutVersionOffset =
        Marshal.OffsetOf<GenshinGameplayQoLSharedState>(
            nameof(GenshinGameplayQoLSharedState.LayoutVersion)).ToInt32();

    /// <summary>
    /// 游戏进程 ID 偏移。
    /// </summary>
    private static readonly int GameProcessIdOffset =
        Marshal.OffsetOf<GenshinGameplayQoLSharedState>(
            nameof(GenshinGameplayQoLSharedState.GameProcessId)).ToInt32();

    /// <summary>
    /// 心跳字段偏移。
    /// </summary>
    private static readonly int HeartbeatOffset =
        Marshal.OffsetOf<GenshinGameplayQoLSharedState>(
            nameof(GenshinGameplayQoLSharedState.Heartbeat)).ToInt32();

    /// <summary>
    /// 停止命令字段偏移。
    /// </summary>
    private static readonly int CommandOffset =
        Marshal.OffsetOf<GenshinGameplayQoLSharedState>(
            nameof(GenshinGameplayQoLSharedState.Command)).ToInt32();

    /// <summary>
    /// 功能位字段偏移。
    /// </summary>
    private static readonly int FeatureFlagsOffset =
        Marshal.OffsetOf<GenshinGameplayQoLSharedState>(
            nameof(GenshinGameplayQoLSharedState.FeatureFlags)).ToInt32();

    /// <summary>
    /// 状态数组字段偏移。
    /// </summary>
    private static readonly int StatusOffset =
        Marshal.OffsetOf<GenshinGameplayQoLSharedState>(
            nameof(GenshinGameplayQoLSharedState.Status)).ToInt32();

    /// <summary>
    /// 错误码数组字段偏移。
    /// </summary>
    private static readonly int ErrorCodeOffset =
        Marshal.OffsetOf<GenshinGameplayQoLSharedState>(
            nameof(GenshinGameplayQoLSharedState.ErrorCode)).ToInt32();

    /// <summary>
    /// 命中计数数组字段偏移。
    /// </summary>
    private static readonly int HookHitCountOffset =
        Marshal.OffsetOf<GenshinGameplayQoLSharedState>(
            nameof(GenshinGameplayQoLSharedState.HookHitCount)).ToInt32();

    /// <summary>
    /// 替换计数数组字段偏移。
    /// </summary>
    private static readonly int ReplaceCountOffset =
        Marshal.OffsetOf<GenshinGameplayQoLSharedState>(
            nameof(GenshinGameplayQoLSharedState.ReplaceCount)).ToInt32();

    /// <summary>
    /// 恢复计数数组字段偏移。
    /// </summary>
    private static readonly int RestoreCountOffset =
        Marshal.OffsetOf<GenshinGameplayQoLSharedState>(
            nameof(GenshinGameplayQoLSharedState.RestoreCount)).ToInt32();

    /// <summary>
    /// 功能 RVA 数组字段偏移。
    /// </summary>
    private static readonly int FeatureRvaOffset =
        Marshal.OffsetOf<GenshinGameplayQoLSharedState>(
            nameof(GenshinGameplayQoLSharedState.FeatureRva)).ToInt32();

    /// <summary>
    /// 预留数组字段偏移。
    /// </summary>
    private static readonly int ReservedOffset =
        Marshal.OffsetOf<GenshinGameplayQoLSharedState>(
            nameof(GenshinGameplayQoLSharedState.Reserved)).ToInt32();

    /// <summary>
    /// 共享内存对象。
    /// </summary>
    private readonly MemoryMappedFile _memoryMappedFile;

    /// <summary>
    /// 共享内存读写视图。
    /// </summary>
    private readonly MemoryMappedViewAccessor _viewAccessor;

    /// <summary>
    /// 当前进程内递增的心跳计数。
    /// </summary>
    private int _heartbeat = 1;

    /// <summary>
    /// 创建指定原神进程的独立体验优化共享内存。
    /// </summary>
    /// <param name="processId">原神进程 ID</param>
    /// <param name="featureFlags">初始功能位</param>
    public GenshinGameplayQoLChannel(
        int processId,
        int featureFlags)
    {
        MappingName = $"{MappingNamePrefix}{processId}";
        _memoryMappedFile = MemoryMappedFile.CreateNew(
            MappingName,
            Marshal.SizeOf<GenshinGameplayQoLSharedState>(),
            MemoryMappedFileAccess.ReadWrite);
        _viewAccessor = _memoryMappedFile.CreateViewAccessor(
            0,
            Marshal.SizeOf<GenshinGameplayQoLSharedState>(),
            MemoryMappedFileAccess.ReadWrite);

        // 共享状态包含托管数组，不能整块写入，只能逐字段写入。
        _viewAccessor.Write(MagicOffset, SharedStateMagic);
        _viewAccessor.Write(LayoutVersionOffset, CurrentLayoutVersion);
        _viewAccessor.Write(GameProcessIdOffset, processId);
        _viewAccessor.Write(HeartbeatOffset, 1);
        _viewAccessor.Write(CommandOffset, RunCommand);
        _viewAccessor.Write(FeatureFlagsOffset, featureFlags);
        WriteArray(StatusOffset, CreateArray(NotRequestedFeatureStatus));
        WriteArray(ErrorCodeOffset, CreateArray(0));
        WriteArray(HookHitCountOffset, CreateArray(0));
        WriteArray(ReplaceCountOffset, CreateArray(0));
        WriteArray(RestoreCountOffset, CreateArray(0));
        WriteArray(FeatureRvaOffset, CreateArray(0));
        WriteArray(ReservedOffset, new int[ReservedCount]);
    }

    /// <summary>
    /// 当前共享内存名称。
    /// </summary>
    public string MappingName { get; }

    /// <summary>
    /// 读取一份完整共享状态快照。
    /// </summary>
    /// <returns>当前体验优化状态</returns>
    public GenshinGameplayQoLSharedState ReadSnapshot()
    {
        return new GenshinGameplayQoLSharedState
        {
            Magic = _viewAccessor.ReadInt32(MagicOffset),
            LayoutVersion = _viewAccessor.ReadInt32(LayoutVersionOffset),
            GameProcessId = _viewAccessor.ReadInt32(GameProcessIdOffset),
            Heartbeat = _viewAccessor.ReadInt32(HeartbeatOffset),
            Command = _viewAccessor.ReadInt32(CommandOffset),
            FeatureFlags = _viewAccessor.ReadInt32(FeatureFlagsOffset),
            Status = ReadArray(StatusOffset),
            ErrorCode = ReadArray(ErrorCodeOffset),
            HookHitCount = ReadArray(HookHitCountOffset),
            ReplaceCount = ReadArray(ReplaceCountOffset),
            RestoreCount = ReadArray(RestoreCountOffset),
            FeatureRva = ReadArray(FeatureRvaOffset),
            Reserved = ReadArray(ReservedOffset, ReservedCount),
        };
    }

    /// <summary>
    /// 更新运行期功能位。
    /// </summary>
    /// <param name="featureFlags">最新功能位</param>
    public void UpdateFeatureFlags(int featureFlags)
    {
        _viewAccessor.Write(FeatureFlagsOffset, featureFlags);
    }

    /// <summary>
    /// 更新心跳，避免 BetterGI 异常退出后功能永久保持开启。
    /// </summary>
    public void SignalHeartbeat()
    {
        _viewAccessor.Write(
            HeartbeatOffset,
            Interlocked.Increment(ref _heartbeat));
    }

    /// <summary>
    /// 请求游戏内工作线程恢复全部功能并停止。
    /// </summary>
    public void RequestStop()
    {
        _viewAccessor.Write(CommandOffset, StopCommand);
    }

    /// <summary>
    /// 检查七项功能是否都已恢复并进入停止状态。
    /// </summary>
    /// <returns>全部停止时返回 true</returns>
    public bool IsStopped()
    {
        for (var index = 0;
             index < GenshinGameplayQoLSharedState.FeatureCount;
             index++)
        {
            if (_viewAccessor.ReadInt32(
                    StatusOffset + (index * sizeof(int)))
                != StoppedFeatureStatus)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 释放共享内存视图和内核对象。
    /// </summary>
    public void Dispose()
    {
        _viewAccessor.Dispose();
        _memoryMappedFile.Dispose();
    }

    /// <summary>
    /// 创建固定长度的整数数组。
    /// </summary>
    /// <param name="value">初始值</param>
    /// <returns>功能数量长度的数组</returns>
    private static int[] CreateArray(int value)
    {
        var values = new int[GenshinGameplayQoLSharedState.FeatureCount];
        if (value != 0)
        {
            Array.Fill(values, value);
        }

        return values;
    }

    /// <summary>
    /// 把一组整数写入共享内存。
    /// </summary>
    /// <param name="offset">字段起始偏移</param>
    /// <param name="values">要写入的值</param>
    private void WriteArray(int offset, int[] values)
    {
        _viewAccessor.WriteArray(offset, values, 0, values.Length);
    }

    /// <summary>
    /// 从共享内存读取一组功能字段。
    /// </summary>
    /// <param name="offset">字段起始偏移</param>
    /// <returns>读取到的字段副本</returns>
    private int[] ReadArray(int offset)
    {
        return ReadArray(
            offset,
            GenshinGameplayQoLSharedState.FeatureCount);
    }

    /// <summary>
    /// 从共享内存读取指定数量的整数。
    /// </summary>
    /// <param name="offset">字段起始偏移</param>
    /// <param name="count">元素数量</param>
    /// <returns>读取到的数组副本</returns>
    private int[] ReadArray(int offset, int count)
    {
        var values = new int[count];
        _viewAccessor.ReadArray(offset, values, 0, count);
        return values;
    }
}
