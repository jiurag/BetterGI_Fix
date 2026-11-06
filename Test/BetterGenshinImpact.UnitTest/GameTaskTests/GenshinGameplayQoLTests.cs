using System.Runtime.InteropServices;
using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.Runtime.Win32;

namespace BetterGenshinImpact.UnitTest.GameTaskTests;

/// <summary>
/// 验证七项游戏体验优化的共享布局和功能位映射。
/// </summary>
public sealed class GenshinGameplayQoLTests
{
    /// <summary>
    /// 共享内存必须与 C++ 侧保持一致，固定为 256 字节。
    /// </summary>
    [Fact]
    public void SharedState_UsesFixedNativeLayout()
    {
        Assert.Equal(
            256,
            Marshal.SizeOf<GenshinGameplayQoLSharedState>());
        Assert.Equal(
            24,
            Marshal.OffsetOf<GenshinGameplayQoLSharedState>(
                nameof(GenshinGameplayQoLSharedState.Status)).ToInt32());
        Assert.Equal(
            192,
            Marshal.OffsetOf<GenshinGameplayQoLSharedState>(
                nameof(GenshinGameplayQoLSharedState.Reserved)).ToInt32());
    }

    /// <summary>
    /// 七项开关只应组合成对应功能位，并且只对国服客户端生效。
    /// </summary>
    [Fact]
    public void CreateGameplayQoLFlags_MapsAllSevenFeatures()
    {
        var config = new GenshinStartConfig
        {
            HideQuestBannerEnabled = true,
            RemoveCameraMoveEnabled = true,
            RemoveTeamProgressEnabled = true,
            SpeedUpClockAnimationEnabled = true,
            RemoveDiveMosaicEnabled = true,
            RedirectSynthesisEnabled = true,
            RemoveCharacterFadeEnabled = true,
        };

        Assert.Equal(
            0x7F,
            GenshinFpsUnlockSession.CreateGameplayQoLFlags(
                config,
                isChineseClient: true));
        Assert.Equal(
            0,
            GenshinFpsUnlockSession.CreateGameplayQoLFlags(
                config,
                isChineseClient: false));
    }

    /// <summary>
    /// 共享状态含托管数组，必须能逐字段写入并原样读回；
    /// 整块写入会抛 ArgumentException 并中断整个帧率会话。
    /// </summary>
    [Fact]
    public void Channel_CreatesMappingAndRoundTripsSnapshot()
    {
        const int featureFlags = 0x7F;

        using var channel = new GenshinGameplayQoLChannel(
            Environment.ProcessId,
            featureFlags);

        var snapshot = channel.ReadSnapshot();

        Assert.Equal(0x31514742, snapshot.Magic);
        Assert.Equal(
            GenshinGameplayQoLChannel.CurrentLayoutVersion,
            snapshot.LayoutVersion);
        Assert.Equal(Environment.ProcessId, snapshot.GameProcessId);
        Assert.Equal(featureFlags, snapshot.FeatureFlags);
        // 1 表示允许游戏内工作线程继续运行。
        Assert.Equal(1, snapshot.Command);
        Assert.Equal(
            GenshinGameplayQoLSharedState.FeatureCount,
            snapshot.Status.Length);
        Assert.All(
            snapshot.Status,
            value => Assert.Equal(
                GenshinGameplayQoLChannel.NotRequestedFeatureStatus,
                value));
        Assert.All(
            snapshot.HookHitCount,
            value => Assert.Equal(0, value));
        Assert.All(
            snapshot.ReplaceCount,
            value => Assert.Equal(0, value));
        Assert.Equal(16, snapshot.Reserved.Length);
        Assert.All(
            snapshot.Reserved,
            value => Assert.Equal(0, value));
    }
}
