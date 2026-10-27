using BetterGenshinImpact.GameTask;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.SystemControlTests;

public class GenshinStartArgumentsBuilderTests
{
    [Fact]
    public void Build_WindowedDisabledWithoutUserArguments_ReturnsEmptyArguments()
    {
        var result = GenshinStartArgumentsBuilder.Build(
            null,
            isChildSession: false,
            borderlessFullscreenEnabled: false,
            monitorWidth: 2560,
            monitorHeight: 1440);

        Assert.Equal(string.Empty, result.Arguments);
        Assert.Equal(GenshinStartWindowMode.None, result.AppliedMode);
    }

    [Fact]
    public void Build_WindowedDisabled_PreservesConfiguredArgumentsExceptOuterWhitespace()
    {
        var result = GenshinStartArgumentsBuilder.Build(
            "  -popupwindow -screen-width 1280 -custom value  ",
            isChildSession: false,
            borderlessFullscreenEnabled: false,
            monitorWidth: 2560,
            monitorHeight: 1440);

        Assert.Equal("-popupwindow -screen-width 1280 -custom value", result.Arguments);
        Assert.Equal(GenshinStartWindowMode.None, result.AppliedMode);
    }

    [Fact]
    public void Build_WindowedEnabledWithoutUserArguments_AppendsPopupWindowArguments()
    {
        var result = GenshinStartArgumentsBuilder.Build(
            null,
            isChildSession: false,
            borderlessFullscreenEnabled: true,
            monitorWidth: 2560,
            monitorHeight: 1440);

        Assert.Equal(
            "-popupwindow -screen-width 2560 -screen-height 1440",
            result.Arguments);
        Assert.Equal(GenshinStartWindowMode.FullscreenWindowed, result.AppliedMode);
    }

    [Fact]
    public void Build_WindowedEnabled_PreservesUnrelatedArgumentsInOriginalOrder()
    {
        var result = GenshinStartArgumentsBuilder.Build(
            "-custom first --flag second",
            isChildSession: false,
            borderlessFullscreenEnabled: true,
            monitorWidth: 3440,
            monitorHeight: 1440);

        Assert.Equal(
            "-custom first --flag second -popupwindow -screen-width 3440 -screen-height 1440",
            result.Arguments);
        Assert.Equal(GenshinStartWindowMode.FullscreenWindowed, result.AppliedMode);
    }

    [Fact]
    public void Build_WindowedEnabled_RemovesConflictingArgumentsRegardlessOfCase()
    {
        var result = GenshinStartArgumentsBuilder.Build(
            "-POPUPWINDOW -SCREEN-FULLSCREEN 0 -Window-Mode exclusive -custom value",
            isChildSession: false,
            borderlessFullscreenEnabled: true,
            monitorWidth: 1920,
            monitorHeight: 1080);

        Assert.Equal(
            "-custom value -popupwindow -screen-width 1920 -screen-height 1080",
            result.Arguments);
        Assert.Single(
            result.Arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries),
            token => string.Equals(token, "-popupwindow", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            result.Arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries),
            token => string.Equals(token, "-screen-fullscreen", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            result.Arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries),
            token => string.Equals(token, "-window-mode", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Build_WindowedEnabled_RemovesSpaceAndEqualsModeAndDimensionArguments()
    {
        var result = GenshinStartArgumentsBuilder.Build(
            "-custom value -Screen-Width 1280 -screen-height=720 -screen-fullscreen=0 -window-mode \"exclusive\" -SCREEN-WIDTH = 1024 -screen-HEIGHT \"600\"",
            isChildSession: false,
            borderlessFullscreenEnabled: true,
            monitorWidth: 2560,
            monitorHeight: 1440);

        Assert.Equal(
            "-custom value -popupwindow -screen-width 2560 -screen-height 1440",
            result.Arguments);
        Assert.Equal(GenshinStartWindowMode.FullscreenWindowed, result.AppliedMode);
    }

    [Theory]
    [InlineData(0, 1080)]
    [InlineData(1920, 0)]
    [InlineData(-1, 1080)]
    [InlineData(1920, -1)]
    public void Build_WindowedEnabledWithInvalidMonitorSize_DoesNotInject(int width, int height)
    {
        const string configuredArguments = "-popupwindow -screen-width 1280 -custom value";

        var result = GenshinStartArgumentsBuilder.Build(
            configuredArguments,
            isChildSession: false,
            borderlessFullscreenEnabled: true,
            monitorWidth: width,
            monitorHeight: height);

        Assert.Equal(configuredArguments, result.Arguments);
        Assert.Equal(GenshinStartWindowMode.None, result.AppliedMode);
    }

    [Fact]
    public void Build_ChildSession_AlwaysUsesFixedResolutionAndRemovesOverrides()
    {
        var result = GenshinStartArgumentsBuilder.Build(
            "-popupwindow -screen-width 3840 -screen-height=2160 -custom value",
            isChildSession: true,
            borderlessFullscreenEnabled: true,
            monitorWidth: 2560,
            monitorHeight: 1440);

        Assert.Equal(
            "-custom value -popupwindow -screen-width 1920 -screen-height 1080",
            result.Arguments);
        Assert.Equal(GenshinStartWindowMode.FullscreenWindowed, result.AppliedMode);
    }

    [Fact]
    public void Build_WindowedEnabled_PreservesWhitespaceInsideQuotedValues()
    {
        var result = GenshinStartArgumentsBuilder.Build(
            "-custom \"value  with  spaces\" -screen-width 1280",
            isChildSession: false,
            borderlessFullscreenEnabled: true,
            monitorWidth: 2560,
            monitorHeight: 1440);

        Assert.Equal(
            "-custom \"value  with  spaces\" -popupwindow -screen-width 2560 -screen-height 1440",
            result.Arguments);
        Assert.Equal(GenshinStartWindowMode.FullscreenWindowed, result.AppliedMode);
    }
}
