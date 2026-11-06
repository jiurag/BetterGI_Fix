using System;
using System.IO;
using System.Reflection.PortableExecutable;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.GenshinFpsUnlockTests;

/// <summary>
/// 验证当前国服客户端中的雾参数复制函数特征。
/// </summary>
public sealed class GenshinFpsUnlockFogPatternTests
{
    /// <summary>
    /// 当前客户端中的雾参数复制函数 RVA。
    /// </summary>
    private const int ExpectedFogCopyRva = 0x19C6230;

    /// <summary>
    /// 雾参数复制函数的唯一特征。
    /// </summary>
    private static readonly byte?[] FogCopyPattern =
    [
        0x0F, 0xB6, 0x02, 0x88, 0x01, 0x8B, 0x42, 0x04,
        0x89, 0x41, 0x04, 0xF3, 0x0F, 0x10, 0x52, null,
        0xF3, 0x0F, 0x10, 0x4A, null, 0xF3, 0x0F, 0x10,
        0x42, null, 0x8B, 0x42, 0x08,
    ];

    /// <summary>
    /// 雾参数复制函数前 16 字节完整序言。
    /// </summary>
    private static readonly byte[] FogCopyPrologue =
    [
        0x0F, 0xB6, 0x02, 0x88, 0x01, 0x8B, 0x42, 0x04,
        0x89, 0x41, 0x04, 0xF3, 0x0F, 0x10, 0x52, 0x14,
    ];

    /// <summary>
    /// 设置 BETTERGI_GENSHIN_EXE 后验证雾参数复制函数唯一且序言一致。
    /// </summary>
    [Fact]
    public void CurrentClient_FogCopy_IsUniqueAndMatchesPrologue()
    {
        var gamePath = Environment.GetEnvironmentVariable("BETTERGI_GENSHIN_EXE");
        if (string.IsNullOrWhiteSpace(gamePath))
        {
            return;
        }

        using var stream = File.OpenRead(gamePath);
        using var peReader = new PEReader(stream);
        var sectionIndex = -1;
        for (var index = 0; index < peReader.PEHeaders.SectionHeaders.Length; index++)
        {
            if (string.Equals(
                    peReader.PEHeaders.SectionHeaders[index].Name,
                    ".text",
                    StringComparison.Ordinal))
            {
                sectionIndex = index;
                break;
            }
        }

        Assert.True(sectionIndex >= 0, "未找到 .text 节");
        var section = peReader.PEHeaders.SectionHeaders[sectionIndex];
        var sectionData = peReader
            .GetSectionData(section.VirtualAddress)
            .GetContent();
        var matches = FindPatternMatches(
            sectionData.AsSpan(),
            FogCopyPattern);
        var match = Assert.Single(matches);
        Assert.Equal(
            ExpectedFogCopyRva,
            section.VirtualAddress + match);
        Assert.Equal(
            FogCopyPrologue,
            sectionData
                .AsSpan(match, FogCopyPrologue.Length)
                .ToArray());
    }

    /// <summary>
    /// 在二进制数据中查找带通配字节的唯一特征。
    /// </summary>
    /// <param name="data">待搜索的节数据</param>
    /// <param name="pattern">特征字节；空值表示通配</param>
    /// <returns>所有匹配偏移</returns>
    private static int[] FindPatternMatches(
        ReadOnlySpan<byte> data,
        ReadOnlySpan<byte?> pattern)
    {
        var matches = new System.Collections.Generic.List<int>();
        for (var offset = 0; offset <= data.Length - pattern.Length; offset++)
        {
            var matched = true;
            for (var index = 0; index < pattern.Length; index++)
            {
                var expected = pattern[index];
                if (expected.HasValue
                    && data[offset + index] != expected.GetValueOrDefault())
                {
                    matched = false;
                    break;
                }
            }

            if (matched)
            {
                matches.Add(offset);
            }
        }

        return [.. matches];
    }
}
