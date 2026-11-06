using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.Runtime.Win32;
using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.GenshinFpsUnlockTests;

/// <summary>
/// 验证国服原神 FOV 配置、功能位和 Native 静态契约。
/// </summary>
public sealed class GenshinFpsUnlockFovContractTests
{
    /// <summary>
    /// FOV 函数的唯一特征；空值表示通配字节。
    /// </summary>
    private static readonly byte?[] FovPattern =
    [
        0x40, 0x53, 0x48, 0x83, 0xEC, 0x60, 0x0F, 0x29,
        0x74, 0x24, null, 0x48, 0x8B, 0xD9, 0x0F, 0x28,
        0xF1, 0xE8, null, null, null, null, 0x48, 0x85,
        0xC0, 0x0F, 0x84, null, null, null, null, 0xE8,
        null, null, null, null, 0x48, 0x8B, 0xC8,
    ];

    /// <summary>
    /// FOV 函数前 17 字节完整序言。
    /// </summary>
    private static readonly byte[] FovPrologue =
    [
        0x40, 0x53, 0x48, 0x83, 0xEC, 0x60, 0x0F, 0x29,
        0x74, 0x24, 0x50, 0x48, 0x8B, 0xD9, 0x0F, 0x28,
        0xF1,
    ];

    /// <summary>
    /// 直接同步相机 FOV 字段函数的唯一特征；空值表示通配字节。
    /// </summary>
    private static readonly byte?[] FovSyncPattern =
    [
        0x40, 0x53, 0x48, 0x83, 0xEC, 0x20, 0x48, 0x8B,
        0xD9, 0xE8, null, null, null, null, 0x48, 0x85,
        0xC0, 0x74, 0x78, 0xE8, null, null, null, null,
        0x48, 0x8B, 0xC8, 0x48, 0x8B, 0x10, 0xFF, 0x92,
        0xF8, 0x00, 0x00, 0x00, 0x84, 0xC0, 0x74, 0x63,
        0x48,
    ];

    /// <summary>
    /// 直接同步相机 FOV 字段函数前 9 字节完整序言。
    /// </summary>
    private static readonly byte[] FovSyncPrologue =
    [
        0x40, 0x53, 0x48, 0x83, 0xEC, 0x20, 0x48, 0x8B,
        0xD9,
    ];

    /// <summary>
    /// 第三人称相机最终位姿函数的唯一特征；空值表示通配字节。
    /// </summary>
    private static readonly byte?[] FovCameraPosePattern =
    [
        0x41, 0x57, 0x41, 0x56, 0x56, 0x57, 0x53, 0x48,
        0x81, 0xEC, 0x20, 0x03, 0x00, 0x00, 0x66, 0x44,
        0x0F, 0x29, 0xBC, 0x24, 0x10, 0x03, 0x00, 0x00,
        0x66, 0x44, 0x0F, 0x29, 0xB4, 0x24, 0x00, 0x03,
        0x00, 0x00,
    ];

    /// <summary>
    /// 第三人称相机最终位姿函数前 14 字节完整序言。
    /// </summary>
    private static readonly byte[] FovCameraPosePrologue =
    [
        0x41, 0x57, 0x41, 0x56, 0x56, 0x57, 0x53, 0x48,
        0x81, 0xEC, 0x20, 0x03, 0x00, 0x00,
    ];

    /// <summary>
    /// 验证 FOV 默认关闭并使用设计规定的初始值。
    /// </summary>
    [Fact]
    public void Config_FovDefaultsToDisabledWithDefaultTarget()
    {
        var config = new GenshinStartConfig();

        Assert.False(config.FovAdjustmentEnabled);
        Assert.False(config.FovForceModeEnabled);
        Assert.Equal(GenshinStartConfig.DefaultFovTarget, config.FovTarget);
        Assert.True(config.IsFovTargetInputValid);
    }

    /// <summary>
    /// 验证 FOV 范围边界和范围外输入。
    /// </summary>
    /// <param name="target">待校验目标</param>
    /// <param name="expected">预期是否合法</param>
    [Theory]
    [InlineData(14, false)]
    [InlineData(15, true)]
    [InlineData(60, true)]
    [InlineData(165, true)]
    [InlineData(166, false)]
    public void Config_FovValidationUsesInclusiveBounds(
        int target,
        bool expected)
    {
        Assert.Equal(
            expected,
            GenshinStartConfig.IsValidFovTarget(target));
    }

    /// <summary>
    /// 验证输入框只接受范围内的纯整数文本。
    /// </summary>
    /// <param name="text">待解析文本</param>
    /// <param name="expected">预期是否合法</param>
    [Theory]
    [InlineData("", false)]
    [InlineData(" ", false)]
    [InlineData("abc", false)]
    [InlineData("60.0", false)]
    [InlineData("-15", false)]
    [InlineData("+60", false)]
    [InlineData("14", false)]
    [InlineData("15", true)]
    [InlineData("165", true)]
    [InlineData("166", false)]
    public void Config_FovTextParsingRejectsInvalidInput(
        string text,
        bool expected)
    {
        Assert.Equal(
            expected,
            GenshinStartConfig.TryParseFovTarget(text, out _));
    }

    /// <summary>
    /// 验证共享状态布局版本、大小和追加后的初始 FOV 字段。
    /// </summary>
    [Fact]
    public void HookChannel_FovFieldsMatchLayoutVersionSix()
    {
        const int fovTarget = 90;
        using var channel = new GenshinFpsUnlockHookChannel(
            Environment.ProcessId,
            120,
            GenshinFpsUnlockHookChannel.FovAdjustmentFeatureFlag,
            fovTarget);

        var snapshot = channel.ReadSnapshot();
        Assert.Equal(6, GenshinFpsUnlockHookChannel.CurrentLayoutVersion);
        Assert.Equal(100, Marshal.SizeOf<GenshinFpsUnlockSharedState>());
        Assert.Equal(fovTarget, snapshot.FovTarget);
        Assert.Equal(
            GenshinFpsUnlockHookChannel.NotRequestedFeatureStatus,
            snapshot.FovStatus);
    }

    /// <summary>
    /// 验证 FOV 可以独立于帧率和去雾开启。
    /// </summary>
    [Fact]
    public void FeatureFlags_FovDoesNotDependOnOtherFeatures()
    {
        var config = new GenshinStartConfig
        {
            FpsUnlockEnabled = false,
            FogRemovalEnabled = false,
            FovAdjustmentEnabled = true,
            FovTarget = 75,
            IsFovTargetInputValid = true,
        };

        var flags = GenshinFpsUnlockSession.CreateFeatureFlags(
            config,
            isChineseClient: true);

        Assert.Equal(
            GenshinFpsUnlockHookChannel.FovAdjustmentFeatureFlag,
            flags);
    }

    /// <summary>
    /// 验证非法输入或非国服进程不会设置 FOV 功能位。
    /// </summary>
    [Fact]
    public void FeatureFlags_InvalidInputAndInternationalClientExcludeFov()
    {
        var invalidConfig = new GenshinStartConfig
        {
            FovAdjustmentEnabled = true,
            FovTarget = 60,
            IsFovTargetInputValid = false,
        };
        Assert.Equal(
            0,
            GenshinFpsUnlockSession.CreateFeatureFlags(
                invalidConfig,
                isChineseClient: true));

        var internationalConfig = new GenshinStartConfig
        {
            FovAdjustmentEnabled = true,
            FovTarget = 60,
            IsFovTargetInputValid = true,
        };
        Assert.Equal(
            0,
            GenshinFpsUnlockSession.CreateFeatureFlags(
                internationalConfig,
                isChineseClient: false));
    }

    /// <summary>
    /// 验证强制模式必须依附于已开启且目标合法的国服 FOV。
    /// </summary>
    [Fact]
    public void FeatureFlags_ForceModeRequiresEnabledValidFov()
    {
        var config = new GenshinStartConfig
        {
            FovAdjustmentEnabled = true,
            FovForceModeEnabled = true,
            FovTarget = 75,
            IsFovTargetInputValid = true,
        };
        var fovFlags =
            GenshinFpsUnlockHookChannel.FovAdjustmentFeatureFlag
            | GenshinFpsUnlockHookChannel.FovForceModeFeatureFlag;

        Assert.Equal(
            fovFlags,
            GenshinFpsUnlockSession.CreateFeatureFlags(
                config,
                isChineseClient: true));

        config.FovAdjustmentEnabled = false;
        Assert.Equal(
            0,
            GenshinFpsUnlockSession.CreateFeatureFlags(
                config,
                isChineseClient: true));

        config.FovAdjustmentEnabled = true;
        config.IsFovTargetInputValid = false;
        Assert.Equal(
            0,
            GenshinFpsUnlockSession.CreateFeatureFlags(
                config,
                isChineseClient: true));

        config.IsFovTargetInputValid = true;
        config.FovTarget = 166;
        Assert.Equal(
            0,
            GenshinFpsUnlockSession.CreateFeatureFlags(
                config,
                isChineseClient: true));

        config.FovTarget = 75;
        Assert.Equal(
            fovFlags,
            GenshinFpsUnlockSession.CreateFeatureFlags(
                config,
                isChineseClient: true));
        Assert.Equal(
            0,
            GenshinFpsUnlockSession.CreateFeatureFlags(
                config,
                isChineseClient: false));
    }

    /// <summary>
    /// 设置 BETTERGI_GENSHIN_EXE 后验证 FOV 特征唯一且 17 字节序言一致。
    /// </summary>
    [Fact]
    public void CurrentClient_FovPattern_IsUniqueAndMatchesPrologue()
    {
        var gamePath = Environment.GetEnvironmentVariable(
            "BETTERGI_GENSHIN_EXE");
        if (string.IsNullOrWhiteSpace(gamePath))
        {
            return;
        }

        using var stream = File.OpenRead(gamePath);
        using var peReader =
            new System.Reflection.PortableExecutable.PEReader(stream);
        var section = Assert.Single(
            peReader.PEHeaders.SectionHeaders.Where(
                header => string.Equals(
                    header.Name,
                    ".text",
                    StringComparison.Ordinal)));
        var sectionData = peReader
            .GetSectionData(section.VirtualAddress)
            .GetContent();
        var match = Assert.Single(
            FindPatternMatches(sectionData.AsSpan(), FovPattern));

        Assert.Equal(
            FovPrologue,
            sectionData.AsSpan(match, FovPrologue.Length).ToArray());
    }

    /// <summary>
    /// 设置 BETTERGI_GENSHIN_EXE 后验证相机 FOV 同步特征唯一且 9 字节序言一致。
    /// </summary>
    [Fact]
    public void CurrentClient_FovSyncPattern_IsUniqueAndMatchesPrologue()
    {
        var gamePath = Environment.GetEnvironmentVariable(
            "BETTERGI_GENSHIN_EXE");
        if (string.IsNullOrWhiteSpace(gamePath))
        {
            return;
        }

        using var stream = File.OpenRead(gamePath);
        using var peReader =
            new System.Reflection.PortableExecutable.PEReader(stream);
        var section = Assert.Single(
            peReader.PEHeaders.SectionHeaders.Where(
                header => string.Equals(
                    header.Name,
                    ".text",
                    StringComparison.Ordinal)));
        var sectionData = peReader
            .GetSectionData(section.VirtualAddress)
            .GetContent();
        var match = Assert.Single(
            FindPatternMatches(sectionData.AsSpan(), FovSyncPattern));

        Assert.Equal(
            FovSyncPrologue,
            sectionData.AsSpan(match, FovSyncPrologue.Length).ToArray());
    }

    /// <summary>
    /// 设置 BETTERGI_GENSHIN_EXE 后验证相机最终位姿特征唯一且 14 字节序言一致。
    /// </summary>
    [Fact]
    public void CurrentClient_FovCameraPosePattern_IsUniqueAndMatchesPrologue()
    {
        var gamePath = Environment.GetEnvironmentVariable(
            "BETTERGI_GENSHIN_EXE");
        if (string.IsNullOrWhiteSpace(gamePath))
        {
            return;
        }

        using var stream = File.OpenRead(gamePath);
        using var peReader =
            new System.Reflection.PortableExecutable.PEReader(stream);
        var section = Assert.Single(
            peReader.PEHeaders.SectionHeaders.Where(
                header => string.Equals(
                    header.Name,
                    "il2cpp",
                    StringComparison.Ordinal)));
        var sectionData = peReader
            .GetSectionData(section.VirtualAddress)
            .GetContent();
        var match = Assert.Single(
            FindPatternMatches(sectionData.AsSpan(), FovCameraPosePattern));

        Assert.Equal(
            FovCameraPosePrologue,
            sectionData
                .AsSpan(match, FovCameraPosePrologue.Length)
                .ToArray());
    }

    /// <summary>
    /// 设置 BETTERGI_GENSHIN_EXE 后验证 CameraPose 在只读分派表中只有一个指针槽位。
    /// </summary>
    [Fact]
    public void CurrentClient_CameraPoseDispatchPointer_IsUniqueInRdata()
    {
        var gamePath = Environment.GetEnvironmentVariable(
            "BETTERGI_GENSHIN_EXE");
        if (string.IsNullOrWhiteSpace(gamePath))
        {
            return;
        }

        using var stream = File.OpenRead(gamePath);
        using var peReader =
            new System.Reflection.PortableExecutable.PEReader(stream);
        var imageBase = peReader.PEHeaders.PEHeader?.ImageBase
            ?? throw new InvalidOperationException("PE 镜像基址缺失");
        var il2cppSection = Assert.Single(
            peReader.PEHeaders.SectionHeaders.Where(
                header => string.Equals(
                    header.Name,
                    "il2cpp",
                    StringComparison.Ordinal)));
        var il2cppData = peReader
            .GetSectionData(il2cppSection.VirtualAddress)
            .GetContent();
        var cameraPoseOffset = Assert.Single(
            FindPatternMatches(il2cppData.AsSpan(), FovCameraPosePattern));
        var cameraPoseAddress = imageBase
            + (ulong)il2cppSection.VirtualAddress
            + (ulong)cameraPoseOffset;

        var rdataSection = Assert.Single(
            peReader.PEHeaders.SectionHeaders.Where(
                header => string.Equals(
                    header.Name,
                    ".rdata",
                    StringComparison.Ordinal)));
        var rdataData = peReader
            .GetSectionData(rdataSection.VirtualAddress)
            .GetContent();
        var rdataBytes = rdataData.ToArray();
        var pointerMatches = Enumerable
            .Range(0, (rdataBytes.Length - sizeof(ulong)) / sizeof(ulong) + 1)
            .Where(index => BitConverter.ToUInt64(
                rdataBytes,
                index * sizeof(ulong)) == cameraPoseAddress)
            .ToArray();

        Assert.Single(pointerMatches);
    }

    /// <summary>
    /// 验证 Native 源码仍使用布局 6、功能位 2、三套特征和对应序言。
    /// </summary>
    [Fact]
    public void NativeSource_KeepsFovContract()
    {
        var sourcePath = Path.Combine(
            FindRepositoryRoot(),
            "BetterGenshinImpact",
            "GameTask",
            "Runtime",
            "Win32",
            "Hook",
            "GenshinFpsUnlockHook.cpp");
        var compactSource = Regex.Replace(
            File.ReadAllText(sourcePath),
            @"\s+",
            string.Empty);

        Assert.Contains("CurrentLayoutVersion=6", compactSource);
        Assert.Contains("FovAdjustmentFeatureFlag=1<<2", compactSource);
        Assert.Contains("FovForceModeFeatureFlag=1<<3", compactSource);
        Assert.Contains(
            "volatileLONGg_fovForceModeEnabled=0",
            compactSource);
        var forceModeCheck = compactSource.IndexOf(
            "constboolforceAllFov=",
            StringComparison.Ordinal);
        var defaultViewCheck = compactSource.IndexOf(
            "constboolisDefaultView=",
            StringComparison.Ordinal);
        Assert.True(forceModeCheck >= 0);
        Assert.True(defaultViewCheck > forceModeCheck);
        Assert.Contains(
            "sizeof(SharedState)==100",
            compactSource);
        Assert.Contains(
            "sizeof(FovPrologue)==17",
            compactSource);
        Assert.Contains(
            ToCompactHexLiteral(FovPattern),
            compactSource);
        Assert.Contains(
            ToCompactHexLiteral(FovPrologue),
            compactSource);
        Assert.Contains(
            "sizeof(FovSyncPrologue)==9",
            compactSource);
        Assert.Contains(
            ToCompactHexLiteral(FovSyncPattern),
            compactSource);
        Assert.Contains(
            ToCompactHexLiteral(FovSyncPrologue),
            compactSource);
        Assert.Contains(
            "HookSynchronizeFieldOfView",
            compactSource);
        Assert.Contains(
            "if(fovForceModeRequested&&!fovSyncHookFailed)",
            compactSource);
        Assert.Contains(
            "if(!fovForceModeRequested&&(g_fovSyncFunctionHook.IsInstalled()||",
            compactSource);
        Assert.Contains(
            "sizeof(FovCameraPosePrologue)==14",
            compactSource);
        Assert.Contains(
            ToCompactHexLiteral(FovCameraPosePattern),
            compactSource);
        Assert.Contains(
            ToCompactHexLiteral(FovCameraPosePrologue),
            compactSource);
        Assert.Contains(
            "HookUpdateCameraPose",
            compactSource);
        Assert.Contains(
            "if(!fovForceModeRequested&&IsCameraPoseHookInstalled())",
            compactSource);
        Assert.Contains(
            "InstallCameraPoseDispatchHook",
            compactSource);
        Assert.Contains(
            "FindUniquePointerSlot",
            compactSource);
        Assert.Contains(
            "TryFindRdataSection",
            compactSource);
        Assert.Contains(
            "EnsureCameraPoseDispatchHook",
            compactSource);
        Assert.Contains(
            "HookVtableUpdateCameraPose",
            compactSource);
        Assert.Contains(
            "HookVtableSetFieldOfView",
            compactSource);
        Assert.Contains(
            "HookVtableSynchronizeFieldOfView",
            compactSource);
        Assert.Contains(
            "float__fastcallHookVtableSynchronizeFieldOfView(void*instance)",
            compactSource);
        Assert.Contains(
            "TryFindFovVtableSlot",
            compactSource);
        Assert.Contains(
            "原子替换已经完成；即使页保护恢复失败，也不能丢失槽位跟踪",
            compactSource);
        Assert.Contains(
            "InstallFovVtableHook",
            compactSource);
        Assert.Contains(
            "EnsureFovVtableHook",
            compactSource);
        Assert.Contains(
            "UninstallFovVtableHook",
            compactSource);
        Assert.Contains(
            "AbandonFovVtableHook",
            compactSource);
        Assert.Contains(
            "ActivateFovVtableHook",
            compactSource);
        Assert.Contains(
            "RetireFovAuxiliaryHardwareFallback",
            compactSource);
        Assert.Contains(
            "g_fovSetVtableHook",
            compactSource);
        Assert.Contains(
            "g_fovSyncVtableHook",
            compactSource);
        Assert.Contains(
            "g_fovCameraPoseVtableHook",
            compactSource);
        Assert.Contains(
            "FovVtableHookStatusActive",
            compactSource);
        var activateVtableStart = compactSource.IndexOf(
            "boolActivateFovVtableHook(",
            StringComparison.Ordinal);
        var activateVtableEnd = compactSource.IndexOf(
            "boolIsCameraPoseHookInstalled(",
            activateVtableStart,
            StringComparison.Ordinal);
        Assert.True(activateVtableStart >= 0);
        Assert.True(activateVtableEnd > activateVtableStart);
        var activateVtableSource = compactSource[
            activateVtableStart..activateVtableEnd];
        Assert.Contains(
            "if(current!=targetFunction)",
            activateVtableSource);
        var currentValidation = activateVtableSource.IndexOf(
            "if(current!=targetFunction)",
            StringComparison.Ordinal);
        var hardwareUninstall = activateVtableSource.IndexOf(
            "hardwareHook.Uninstall(uninstallError)",
            StringComparison.Ordinal);
        Assert.True(currentValidation >= 0);
        Assert.True(hardwareUninstall > currentValidation);
        Assert.Contains(
            "current!=detour&&current!=original",
            compactSource);
        var retireFallbackStart = compactSource.IndexOf(
            "boolRetireFovAuxiliaryHardwareFallback(",
            StringComparison.Ordinal);
        var retireFallbackEnd = compactSource.IndexOf(
            "boolIsCameraPoseHookInstalled(",
            retireFallbackStart,
            StringComparison.Ordinal);
        Assert.True(retireFallbackStart >= 0);
        Assert.True(retireFallbackEnd > retireFallbackStart);
        var retireFallbackSource = compactSource[
            retireFallbackStart..retireFallbackEnd];
        Assert.Contains(
            "if(poseStatus!=FovVtableHookStatusActive)",
            retireFallbackSource);
        Assert.Contains(
            "g_fovFunctionHook.Uninstall(error)",
            retireFallbackSource);
        Assert.Contains(
            "g_fovSyncFunctionHook.Uninstall(error)",
            retireFallbackSource);
        Assert.Contains(
            "&&!fovSetHardwareFallbackRetired",
            compactSource);
        Assert.Contains(
            "&&!fovSyncHardwareFallbackRetired",
            compactSource);
        Assert.Contains(
            "elseif(auxiliaryRetireError!=ERROR_SUCCESS&&auxiliaryRetireError!=ERROR_RETRY)",
            compactSource);
        Assert.Contains(
            "InterlockedCompareExchange(&state->FovInstallError,ERROR_SUCCESS,static_cast<LONG>(fovAuxiliaryRetireReportedError))",
            compactSource);
        Assert.Contains(
            "constboolfovFeatureFailed=fovHookFailed||(fovForceModeRequested&&(fovSyncHookFailed||fovCameraPoseHookFailed));",
            compactSource);
        Assert.Contains(
            "FovHardwareDiagnosticsMappingNamePrefix[]=L\"Local\\\\BetterGI.FovHardwareDiagnostics.\"",
            compactSource);
        Assert.Contains(
            "FovHardwareDiagnosticsVersion=2",
            compactSource);
        Assert.Contains(
            "sizeof(FovHookHardwareDiagnosticsState)==80",
            compactSource);
        Assert.Contains(
            "sizeof(FovVtableHookDiagnosticsState)==20",
            compactSource);
        Assert.Contains(
            "sizeof(FovHardwareDiagnosticsState)==340",
            compactSource);
        Assert.Contains(
            "FovCameraProbeMappingNamePrefix[]=L\"Local\\\\BetterGI.FovCameraProbe.\"",
            compactSource);
        Assert.Contains(
            "FovCameraProbeVersion=5",
            compactSource);
        Assert.Contains(
            "FovCameraProbeInstanceSpan=0x4000",
            compactSource);
        Assert.Contains(
            "FovCameraProbeInstanceSlotCount=8",
            compactSource);
        Assert.Contains(
            "FovCameraProbeThreadSlotCount=16",
            compactSource);
        Assert.Contains(
            "sizeof(FovCameraProbeState)==51356",
            compactSource);
        Assert.Contains(
            "FovCameraProbeInstanceHeadByteCount=0x200",
            compactSource);
        Assert.Contains(
            "FovCameraProbeTypeNameByteCount=128",
            compactSource);
        Assert.Contains(
            "FovCameraProbeClassHeadByteCount=0x200",
            compactSource);
        Assert.Contains(
            "CaptureFovCameraProbeClass(instance);",
            compactSource);
        Assert.Contains(
            "std::memcpy(&methods,classBytes+0x20,sizeof(methods));",
            compactSource);
        Assert.Contains(
            "std::memcpy(&methodCount,classBytes+0xC2,sizeof(methodCount));",
            compactSource);
        Assert.Contains(
            "std::memcpy(&vtableCount,classBytes+0xBE,sizeof(vtableCount));",
            compactSource);
        Assert.Contains(
            "classBytes+0xD0+(slot*sizeof(uintptr_t))",
            compactSource);
        Assert.Contains(
            "MethodPointerMatchCount",
            compactSource);
        Assert.Contains(
            "VtablePointerMatchCount",
            compactSource);
        Assert.Contains(
            "EnableCameraPoseDispatchHook=false",
            compactSource);
        Assert.Contains(
            "EnableFovCameraProbe=false",
            compactSource);
        Assert.Contains(
            "ifconstexpr(EnableFovCameraProbe)",
            compactSource);
        Assert.Contains(
            "FovVtableFallbackGraceMilliseconds=3000",
            compactSource);
        Assert.Contains(
            "HardwareFallback",
            compactSource);
        Assert.Contains(
            "CaptureFovCameraProbeCandidates(instance,poseFov,target);",
            compactSource);
        Assert.Contains(
            "CloseFovCameraProbe();",
            compactSource);
        Assert.Contains(
            "HardwareBreakpointHotVerificationMilliseconds=62",
            File.ReadAllText(Path.Combine(
                FindRepositoryRoot(),
                "BetterGenshinImpact",
                "GameTask",
                "Runtime",
                "Win32",
                "Hook",
                "InlineHook.cpp")).Replace(" ", string.Empty));

        var syncOriginalCall = compactSource.IndexOf(
            "constfloatoriginalResult=original(instance);",
            StringComparison.Ordinal);
        var syncFieldWrite = compactSource.IndexOf(
            "CameraActiveFieldOfViewOffset)=replacement",
            StringComparison.Ordinal);
        Assert.True(syncOriginalCall >= 0);
        Assert.True(syncFieldWrite > syncOriginalCall);

        var poseHookStart = compactSource.IndexOf(
            "void__fastcallHookUpdateCameraPose(",
            StringComparison.Ordinal);
        var poseHookEnd = compactSource.IndexOf(
            "//雾参数复制Hook",
            poseHookStart,
            StringComparison.Ordinal);
        Assert.True(poseHookStart >= 0);
        Assert.True(poseHookEnd > poseHookStart);
        var poseHookSource = compactSource[
            poseHookStart..poseHookEnd];
        Assert.Contains(
            "ifconstexpr(EnableFovCameraProbe){CaptureFovCameraProbe(instance,deltaTime,input,outputPose,extra,context,method);}",
            poseHookSource);
        var poseOriginalCall = poseHookSource.IndexOf(
            "original(instance,deltaTime,input,outputPose,extra,context,method);",
            StringComparison.Ordinal);
        var poseFieldWrite = poseHookSource.IndexOf(
            "*poseFov=static_cast<double>(replacement);",
            StringComparison.Ordinal);
        Assert.True(poseOriginalCall >= 0);
        Assert.True(poseFieldWrite > poseOriginalCall);
        Assert.Contains(
            "CameraPoseFieldOfViewOffset=0",
            compactSource);
        Assert.DoesNotContain(
            "CameraActiveFieldOfViewOffset)=replacement",
            poseHookSource);

        var inlineHookSource = Regex.Replace(
            File.ReadAllText(Path.Combine(
                FindRepositoryRoot(),
                "BetterGenshinImpact",
                "GameTask",
                "Runtime",
                "Win32",
                "Hook",
                "InlineHook.cpp")),
            @"\s+",
            string.Empty);
        Assert.Contains(
            "thread.ThreadId==lastMatchedThreadId",
            inlineHookSource);
        Assert.Contains(
            "HardwareBreakpointHotVerificationMilliseconds",
            inlineHookSource);
        Assert.Contains(
            "HotWatchdogProc",
            inlineHookSource);
        Assert.Contains(
            "hotVerificationIntervalMilliseconds",
            inlineHookSource);
        Assert.Contains(
            "installError,2))",
            compactSource);
    }

    /// <summary>
    /// 验证首页使用可持久化的整数 FOV 滑条，不再提供文本输入。
    /// </summary>
    [Fact]
    public void HomePage_UsesPersistentFovSliderWithoutTextInput()
    {
        var repositoryRoot = FindRepositoryRoot();
        var xamlPath = Path.Combine(
            repositoryRoot,
            "BetterGenshinImpact",
            "View",
            "Pages",
            "HomePage.xaml");
        var viewModelPath = Path.Combine(
            repositoryRoot,
            "BetterGenshinImpact",
            "ViewModel",
            "Pages",
            "HomePageViewModel.cs");
        var xaml = File.ReadAllText(xamlPath);
        var compactXaml = Regex.Replace(xaml, @"\s+", string.Empty);
        var compactViewModel = Regex.Replace(
            File.ReadAllText(viewModelPath),
            @"\s+",
            string.Empty);

        Assert.Contains("Minimum=\"15\"", xaml);
        Assert.Contains("Maximum=\"165\"", xaml);
        Assert.Contains("TickFrequency=\"1\"", xaml);
        Assert.Contains("IsSnapToTickEnabled=\"True\"", xaml);
        Assert.Contains(
            "Value=\"{BindingFovTargetSliderValue,Mode=TwoWay,UpdateSourceTrigger=PropertyChanged}\"",
            compactXaml);
        Assert.DoesNotContain("FovTargetText", xaml);
        Assert.DoesNotContain("SetFov60Command", xaml);
        Assert.Contains(
            "Config.GenshinStartConfig.FovTarget=target",
            compactViewModel);
    }

    /// <summary>
    /// 在二进制数据中查找带通配字节的特征。
    /// </summary>
    /// <param name="data">待搜索数据</param>
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

    /// <summary>
    /// 把测试特征转换为源码中的紧凑十六进制字面量。
    /// </summary>
    /// <param name="pattern">测试特征</param>
    /// <returns>去除空白后的源码片段</returns>
    private static string ToCompactHexLiteral(ReadOnlySpan<byte?> pattern)
    {
        var values = new string[pattern.Length];
        for (var index = 0; index < pattern.Length; index++)
        {
            var value = pattern[index];
            values[index] = value.HasValue
                ? $"0x{value.GetValueOrDefault():X2}"
                : "0x00";
        }

        return string.Join(',', values);
    }

    /// <summary>
    /// 把完整序言转换为源码中的紧凑十六进制字面量。
    /// </summary>
    /// <param name="pattern">完整序言</param>
    /// <returns>去除空白后的源码片段</returns>
    private static string ToCompactHexLiteral(ReadOnlySpan<byte> pattern)
    {
        var values = new string[pattern.Length];
        for (var index = 0; index < pattern.Length; index++)
        {
            values[index] = $"0x{pattern[index]:X2}";
        }

        return string.Join(',', values);
    }

    /// <summary>
    /// 从测试输出目录向上定位仓库根目录。
    /// </summary>
    /// <returns>包含解决方案文件的仓库根目录</returns>
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(
                    Path.Combine(directory.FullName, "BetterGenshinImpact.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("无法定位 BetterGenshinImpact.sln");
    }
}
