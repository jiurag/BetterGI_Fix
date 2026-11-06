using BetterGenshinImpact.Core.Config;
using BetterGenshinImpact.GameTask.Runtime.Win32;
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace BetterGenshinImpact.UnitTest.GameTaskTests.GenshinFpsUnlockTests;

/// <summary>
/// 帧率解锁画面增强功能的黑盒行为测试。
/// </summary>
public sealed class GenshinFpsUnlockBehaviorTests
{
    /// <summary>
    /// 仅映射模块而不执行 DllMain，用于检查打包 DLL 的公开入口。
    /// </summary>
    private const uint DontResolveDllReferences = 0x00000001;

    /// <summary>
    /// 验证高风险的去雾功能默认保持关闭。
    /// </summary>
    [Fact]
    public void Config_FogRemovalDefaultsToDisabled()
    {
        var config = new GenshinStartConfig();

        Assert.False(config.FogRemovalEnabled);
    }

    /// <summary>
    /// 验证共享状态创建后能原样提供目标帧率和功能位。
    /// </summary>
    [Fact]
    public void HookChannel_PublishesInitialFramerateAndFeatureFlags()
    {
        const int targetFramerate = 120;
        var featureFlags =
            GenshinFpsUnlockHookChannel.FramerateFeatureFlag
            | GenshinFpsUnlockHookChannel.FogRemovalFeatureFlag
            | GenshinFpsUnlockHookChannel.FovAdjustmentFeatureFlag
            | GenshinFpsUnlockHookChannel.FovForceModeFeatureFlag;
        const int fovTarget = 75;

        using var channel = new GenshinFpsUnlockHookChannel(
            Environment.ProcessId,
            targetFramerate,
            featureFlags,
            fovTarget);

        var snapshot = channel.ReadSnapshot();
        Assert.Equal(Environment.ProcessId, snapshot.GameProcessId);
        Assert.Equal(targetFramerate, snapshot.TargetFramerate);
        Assert.Equal(featureFlags, snapshot.FeatureFlags);
        Assert.Equal(fovTarget, snapshot.FovTarget);
        Assert.Equal(
            GenshinFpsUnlockHookChannel.CurrentLayoutVersion,
            snapshot.LayoutVersion);
    }

    /// <summary>
    /// 验证关闭帧率解锁时仍能单独请求国服去雾。
    /// </summary>
    [Fact]
    public void FeatureFlags_FogRemovalDoesNotDependOnFramerateSwitch()
    {
        var config = new GenshinStartConfig
        {
            FpsUnlockEnabled = false,
            FogRemovalEnabled = true,
        };

        var flags = GenshinFpsUnlockSession.CreateFeatureFlags(
            config,
            isChineseClient: true);

        Assert.Equal(
            GenshinFpsUnlockHookChannel.FogRemovalFeatureFlag,
            flags);
    }

    /// <summary>
    /// 验证非国服仍可独立请求帧率解锁，但不会带出画面增强位。
    /// </summary>
    [Fact]
    public void FeatureFlags_InternationalClientOnlyRequestsFramerate()
    {
        var config = new GenshinStartConfig
        {
            FpsUnlockEnabled = true,
            FogRemovalEnabled = true,
        };

        var flags = GenshinFpsUnlockSession.CreateFeatureFlags(
            config,
            isChineseClient: false);

        Assert.Equal(
            GenshinFpsUnlockHookChannel.FramerateFeatureFlag,
            flags);
    }

    /// <summary>
    /// 验证注入模块使用随机副本，关闭租约后临时文件被删除。
    /// </summary>
    [Fact]
    public void HookModuleLease_UsesRandomCopyAndDeletesItOnDispose()
    {
        var sourcePath = Path.Combine(
            Path.GetTempPath(),
            $"BetterGI.RuntimeSource.{Guid.NewGuid():N}.dll");
        var moduleBytes = new byte[] { 0x4D, 0x5A, 0x90, 0x00 };
        File.WriteAllBytes(sourcePath, moduleBytes);

        try
        {
            string modulePath;
            using (var lease = GenshinFpsUnlockHookModuleLease.Create(
                       sourcePath))
            {
                modulePath = lease.ModulePath;
                Assert.NotEqual(sourcePath, modulePath);
                Assert.True(File.Exists(modulePath));
                using var reader = new FileStream(
                    modulePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                using var memory = new MemoryStream();
                reader.CopyTo(memory);
                Assert.Equal(moduleBytes, memory.ToArray());
            }

            Assert.False(File.Exists(modulePath));
        }
        finally
        {
            File.Delete(sourcePath);
        }
    }

    /// <summary>
    /// 验证打包 DLL 只公开新的通用运行期入口。
    /// </summary>
    [Fact]
    public void HookDll_ExportsRuntimeBridgeAndOldEntryIsAbsent()
    {
        var hookPath = Path.Combine(
            AppContext.BaseDirectory,
            "GenshinFpsUnlockHook.dll");
        var module = LoadLibraryEx(
            hookPath,
            0,
            DontResolveDllReferences);
        Assert.NotEqual(nint.Zero, module);

        try
        {
            Assert.NotEqual(nint.Zero, GetProcAddress(module, "RuntimeBridgeProc"));
            Assert.Equal(nint.Zero, GetProcAddress(module, "GenshinFpsUnlockHookProc"));
        }
        finally
        {
            Assert.True(FreeLibrary(module));
        }
    }

    /// <summary>
    /// 映射 DLL 但不调用 DllMain 和初始化例程。
    /// </summary>
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint LoadLibraryEx(
        string fileName,
        nint file,
        uint flags);

    /// <summary>
    /// 从已映射模块中查询导出函数。
    /// </summary>
    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern nint GetProcAddress(
        nint module,
        string procedureName);

    /// <summary>
    /// 释放测试中的只映射模块句柄。
    /// </summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeLibrary(nint module);
}
