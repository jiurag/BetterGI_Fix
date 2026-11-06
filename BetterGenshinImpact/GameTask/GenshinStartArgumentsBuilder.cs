using System;
using System.Text;
using System.Text.RegularExpressions;

namespace BetterGenshinImpact.GameTask;

/// <summary>
/// 构建原神启动参数；不读取配置或 Win32 状态，便于独立测试。
/// </summary>
internal static partial class GenshinStartArgumentsBuilder
{
    private const string ChildSessionGenshinStartArgs =
        "-popupwindow -screen-width 1920 -screen-height 1080";

    private static readonly Regex OverriddenArgumentRegex = new(
        @"(?<!\S)(?:-popupwindow|-screen-(?:width|height|fullscreen)(?:\s*=\s*(?:""[^""]*""|\S+)|\s+(?:""[^""]*""|(?!-)\S+))?|-window-mode(?:\s*=\s*(?:""[^""]*""|\S+)|\s+(?:""[^""]*""|(?!-)\S+))?)(?=\s|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    /// <summary>
    /// 合并用户参数、子会话固定参数和当前实例的全屏窗口化参数。
    /// </summary>
    /// <param name="configuredArguments">用户在首页填写的启动参数</param>
    /// <param name="isChildSession">是否为桌面分身子会话</param>
    /// <param name="borderlessFullscreenEnabled">首页全屏无边框父开关</param>
    /// <param name="monitorWidth">目标显示器的完整宽度</param>
    /// <param name="monitorHeight">目标显示器的完整高度</param>
    /// <returns>最终参数以及是否应用了全屏窗口化</returns>
    internal static GenshinStartArgumentsResult Build(
        string? configuredArguments,
        bool isChildSession,
        bool borderlessFullscreenEnabled,
        int monitorWidth,
        int monitorHeight)
    {
        var originalArguments = configuredArguments?.Trim() ?? string.Empty;

        if (isChildSession)
        {
            var childArguments = RemoveOverriddenArguments(originalArguments);
            return new GenshinStartArgumentsResult(
                AppendArguments(childArguments, ChildSessionGenshinStartArgs),
                GenshinStartWindowMode.FullscreenWindowed);
        }

        if (!borderlessFullscreenEnabled || monitorWidth <= 0 || monitorHeight <= 0)
        {
            return new GenshinStartArgumentsResult(
                originalArguments,
                GenshinStartWindowMode.None);
        }

        var arguments = RemoveOverriddenArguments(originalArguments);
        var fullscreenArguments = $"-popupwindow -screen-width {monitorWidth} -screen-height {monitorHeight}";
        return new GenshinStartArgumentsResult(
            AppendArguments(arguments, fullscreenArguments),
            GenshinStartWindowMode.FullscreenWindowed);
    }

    private static string RemoveOverriddenArguments(string arguments)
    {
        if (string.IsNullOrEmpty(arguments))
        {
            return string.Empty;
        }

        return NormalizeWhitespaceOutsideQuotes(
            OverriddenArgumentRegex.Replace(arguments, " "));
    }

    private static string AppendArguments(string arguments, string appendedArguments)
    {
        return string.IsNullOrEmpty(arguments)
            ? appendedArguments
            : $"{arguments} {appendedArguments}";
    }

    /// <summary>
    /// 合并参数时压缩参数之间的连续空白，但保留引号内用户填写的原始空白。
    /// </summary>
    private static string NormalizeWhitespaceOutsideQuotes(string arguments)
    {
        var builder = new StringBuilder(arguments.Length);
        var inQuotes = false;
        var pendingWhitespace = false;

        foreach (var character in arguments)
        {
            if (character == '"')
            {
                AppendPendingWhitespace(builder, ref pendingWhitespace);
                builder.Append(character);
                inQuotes = !inQuotes;
                continue;
            }

            if (!inQuotes && char.IsWhiteSpace(character))
            {
                pendingWhitespace = builder.Length > 0;
                continue;
            }

            AppendPendingWhitespace(builder, ref pendingWhitespace);
            builder.Append(character);
        }

        return builder.ToString().TrimEnd();
    }

    private static void AppendPendingWhitespace(StringBuilder builder, ref bool pendingWhitespace)
    {
        if (!pendingWhitespace)
        {
            return;
        }

        if (builder.Length > 0)
        {
            builder.Append(' ');
        }

        pendingWhitespace = false;
    }
}

/// <summary>
/// 原神启动参数构建结果。
/// </summary>
/// <param name="Arguments">最终启动参数</param>
/// <param name="AppliedMode">实际应用的窗口模式</param>
internal readonly record struct GenshinStartArgumentsResult(
    string Arguments,
    GenshinStartWindowMode AppliedMode);

/// <summary>
/// 实际应用的窗口模式。
/// </summary>
internal enum GenshinStartWindowMode
{
    None,
    FullscreenWindowed
}

/// <summary>
/// BetterGI 主窗口所在显示器的完整物理矩形。
/// </summary>
/// <param name="Left">显示器左边界</param>
/// <param name="Top">显示器上边界</param>
/// <param name="Width">显示器宽度</param>
/// <param name="Height">显示器高度</param>
internal readonly record struct GenshinMonitorBounds(
    int Left,
    int Top,
    int Width,
    int Height);
