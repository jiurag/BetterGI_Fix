using System;
using System.IO;
using System.Threading;

namespace BetterGenshinImpact.GameTask.Runtime.Win32;

/// <summary>
/// 一次注入使用的临时模块副本，使用随机名称并交给系统在句柄关闭后删除。
/// </summary>
internal sealed class GenshinFpsUnlockHookModuleLease : IDisposable
{
    /// <summary>
    /// 临时模块目录名；目录只保存运行期随机副本。
    /// </summary>
    private const string TemporaryDirectoryName = "RuntimeCache";

    /// <summary>
    /// 已过期临时模块的保留时间，避免缓存无限增长。
    /// </summary>
    private static readonly TimeSpan StaleModuleRetention = TimeSpan.FromDays(1);

    /// <summary>
    /// 保持临时文件打开并启用关闭即删除的流。
    /// </summary>
    private readonly FileStream _moduleStream;

    /// <summary>
    /// 本次注入实际使用的随机模块路径。
    /// </summary>
    private readonly string _modulePath;

    /// <summary>
    /// 是否已经释放临时文件句柄。
    /// </summary>
    private int _disposed;

    /// <summary>
    /// 创建临时模块副本。
    /// </summary>
    /// <param name="sourcePath">构建目录中的原始 Hook DLL</param>
    private GenshinFpsUnlockHookModuleLease(
        FileStream moduleStream,
        string modulePath)
    {
        _moduleStream = moduleStream;
        _modulePath = modulePath;
    }

    /// <summary>
    /// 本次注入实际使用的随机模块路径。
    /// </summary>
    public string ModulePath => _modulePath;

    /// <summary>
    /// 把 Hook DLL 复制到随机临时文件，并保持可读、可删除的句柄。
    /// </summary>
    /// <param name="sourcePath">构建目录中的原始 Hook DLL</param>
    /// <returns>已准备好供目标进程加载的临时模块</returns>
    public static GenshinFpsUnlockHookModuleLease Create(string sourcePath)
    {
        var temporaryDirectory = Path.Combine(
            Path.GetTempPath(),
            TemporaryDirectoryName);
        Directory.CreateDirectory(temporaryDirectory);
        DeleteStaleModules(temporaryDirectory);

        var modulePath = Path.Combine(
            temporaryDirectory,
            $"Runtime.{Guid.NewGuid():N}.dll");
        try
        {
            using var sourceStream = new FileStream(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
            using (var writer = new FileStream(
                       modulePath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.Read,
                       16 * 1024,
                       FileOptions.SequentialScan))
            {
                sourceStream.CopyTo(writer);
                writer.Flush(flushToDisk: true);
            }

            // 写入完成后只保留只读句柄，允许 LoadLibrary 和删除标记同时工作。
            var moduleStream = new FileStream(
                modulePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read | FileShare.Delete,
                16 * 1024,
                FileOptions.DeleteOnClose | FileOptions.RandomAccess);
            return new GenshinFpsUnlockHookModuleLease(
                moduleStream,
                modulePath);
        }
        catch
        {
            try
            {
                File.Delete(modulePath);
            }
            catch (IOException)
            {
                // 文件可能尚未创建或已被系统接管，保留异常调用栈。
            }
            throw;
        }
    }

    /// <summary>
    /// 关闭临时文件句柄；未再被游戏模块占用的副本会立即删除。
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _moduleStream.Dispose();
        }
    }

    /// <summary>
    /// 清理异常退出后遗留的过期随机模块，不触碰正在使用的文件。
    /// </summary>
    /// <param name="temporaryDirectory">受限的临时模块目录</param>
    private static void DeleteStaleModules(string temporaryDirectory)
    {
        var staleBefore = DateTime.UtcNow - StaleModuleRetention;
        foreach (var filePath in Directory.EnumerateFiles(
                     temporaryDirectory,
                     "Runtime.*.dll",
                     SearchOption.TopDirectoryOnly))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(filePath) < staleBefore)
                {
                    File.Delete(filePath);
                }
            }
            catch (IOException)
            {
                // 文件仍被游戏加载时留给下次启动继续清理。
            }
            catch (UnauthorizedAccessException)
            {
                // 权限变化不应阻止本次注入使用新的随机模块。
            }
        }
    }
}
