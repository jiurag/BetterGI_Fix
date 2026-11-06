using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace BetterGenshinImpact.Helpers;

/// <summary>
/// 创建和删除 BetterGI 的 Windows 快捷方式。
/// </summary>
public sealed class ShellShortcutService
{
    /// <summary>
    /// 快捷方式显示名称。
    /// </summary>
    private const string ShortcutName = "BetterGI";

    /// <summary>
    /// 快捷方式描述。
    /// </summary>
    private const string ShortcutDescription = "BetterGI - 更好的原神";

    /// <summary>
    /// BetterGI 可执行文件路径。
    /// </summary>
    private readonly string _executablePath;

    /// <summary>
    /// 通过快捷方式启动时使用的工作目录。
    /// </summary>
    private readonly string _workingDirectory;

    /// <summary>
    /// 创建桌面和开始菜单快捷方式服务。
    /// </summary>
    /// <param name="executablePath">快捷方式指向的可执行文件。</param>
    /// <param name="workingDirectory">快捷方式启动时的工作目录。</param>
    /// <param name="desktopShortcutPath">可选的桌面快捷方式完整路径，主要用于黑盒验证。</param>
    /// <param name="startMenuShortcutPath">可选的开始菜单快捷方式完整路径，主要用于黑盒验证。</param>
    public ShellShortcutService(
        string executablePath,
        string workingDirectory,
        string? desktopShortcutPath = null,
        string? startMenuShortcutPath = null)
    {
        _executablePath = Path.GetFullPath(executablePath);
        _workingDirectory = Path.GetFullPath(workingDirectory);
        DesktopShortcutPath = Path.GetFullPath(
            desktopShortcutPath
            ?? Path.Combine(
                ResolveFolder(Environment.SpecialFolder.CommonDesktopDirectory, Environment.SpecialFolder.DesktopDirectory),
                $"{ShortcutName}.lnk"));
        StartMenuShortcutPath = Path.GetFullPath(
            startMenuShortcutPath
            ?? Path.Combine(
                ResolveFolder(Environment.SpecialFolder.CommonPrograms, Environment.SpecialFolder.Programs),
                ShortcutName,
                $"{ShortcutName}.lnk"));
    }

    /// <summary>
    /// 公共桌面快捷方式路径。
    /// </summary>
    public string DesktopShortcutPath { get; }

    /// <summary>
    /// 公共开始菜单快捷方式路径。
    /// </summary>
    public string StartMenuShortcutPath { get; }

    /// <summary>
    /// 桌面快捷方式当前是否存在。
    /// </summary>
    public bool DesktopShortcutExists => File.Exists(DesktopShortcutPath);

    /// <summary>
    /// 开始菜单快捷方式当前是否存在。
    /// </summary>
    public bool StartMenuShortcutExists => File.Exists(StartMenuShortcutPath);

    /// <summary>
    /// 创建或覆盖桌面快捷方式。
    /// </summary>
    public void CreateDesktopShortcut()
    {
        CreateShortcut(DesktopShortcutPath);
    }

    /// <summary>
    /// 删除桌面快捷方式；文件不存在时不执行任何操作。
    /// </summary>
    public void RemoveDesktopShortcut()
    {
        RemoveShortcut(DesktopShortcutPath, removeEmptyParentDirectory: false);
    }

    /// <summary>
    /// 创建或覆盖开始菜单快捷方式。
    /// </summary>
    public void CreateStartMenuShortcut()
    {
        CreateShortcut(StartMenuShortcutPath);
    }

    /// <summary>
    /// 删除开始菜单快捷方式；目录为空时一并删除 BetterGI 开始菜单文件夹。
    /// </summary>
    public void RemoveStartMenuShortcut()
    {
        RemoveShortcut(StartMenuShortcutPath, removeEmptyParentDirectory: true);
    }

    /// <summary>
    /// 使用 Windows Shell 创建快捷方式并写入目标、工作目录与图标。
    /// </summary>
    /// <param name="shortcutPath">快捷方式完整路径。</param>
    private void CreateShortcut(string shortcutPath)
    {
        if (!File.Exists(_executablePath))
        {
            throw new FileNotFoundException("找不到 BetterGI 主程序，无法创建快捷方式。", _executablePath);
        }

        var parentDirectory = Path.GetDirectoryName(shortcutPath)
                              ?? throw new InvalidOperationException("快捷方式路径缺少父目录。");
        Directory.CreateDirectory(parentDirectory);

        IShellLinkW? shellLink = null;
        IPersistFile? persistFile = null;
        try
        {
            shellLink = (IShellLinkW)new ShellLink();
            shellLink.SetPath(_executablePath);
            shellLink.SetWorkingDirectory(_workingDirectory);
            shellLink.SetDescription(ShortcutDescription);
            shellLink.SetIconLocation(_executablePath, 0);

            persistFile = (IPersistFile)shellLink;
            persistFile.Save(shortcutPath, true);
        }
        finally
        {
            if (persistFile is not null)
            {
                Marshal.FinalReleaseComObject(persistFile);
            }
            else if (shellLink is not null)
            {
                Marshal.FinalReleaseComObject(shellLink);
            }
        }
    }

    /// <summary>
    /// 删除指定快捷方式，并按需清理空的 BetterGI 开始菜单目录。
    /// </summary>
    /// <param name="shortcutPath">快捷方式完整路径。</param>
    /// <param name="removeEmptyParentDirectory">是否清理空的开始菜单父目录。</param>
    private static void RemoveShortcut(string shortcutPath, bool removeEmptyParentDirectory)
    {
        if (File.Exists(shortcutPath))
        {
            File.SetAttributes(shortcutPath, FileAttributes.Normal);
            File.Delete(shortcutPath);
        }

        if (!removeEmptyParentDirectory)
        {
            return;
        }

        var parentDirectory = Path.GetDirectoryName(shortcutPath);
        if (parentDirectory is not null
            && Directory.Exists(parentDirectory)
            && !Directory.EnumerateFileSystemEntries(parentDirectory).Any())
        {
            Directory.Delete(parentDirectory);
        }
    }

    /// <summary>
    /// 优先使用公共目录；系统未提供公共目录时退回当前用户目录。
    /// </summary>
    /// <param name="preferredFolder">首选公共目录。</param>
    /// <param name="fallbackFolder">回退用户目录。</param>
    private static string ResolveFolder(
        Environment.SpecialFolder preferredFolder,
        Environment.SpecialFolder fallbackFolder)
    {
        var preferredPath = Environment.GetFolderPath(preferredFolder);
        if (!string.IsNullOrWhiteSpace(preferredPath))
        {
            return preferredPath;
        }

        var fallbackPath = Environment.GetFolderPath(fallbackFolder);
        return !string.IsNullOrWhiteSpace(fallbackPath)
            ? fallbackPath
            : throw new DirectoryNotFoundException("系统未提供桌面或开始菜单目录。");
    }

    /// <summary>
    /// Windows Shell 的 IShellLinkW 接口，用于设置快捷方式属性。
    /// </summary>
    [ComImport]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        /// <summary>
        /// 读取快捷方式目标；创建流程不使用该方法，但必须保留以维持接口布局。
        /// </summary>
        void GetPath(StringBuilder filePath, int filePathLength, IntPtr findData, uint flags);

        /// <summary>
        /// 读取目标 PIDL；创建流程不使用该方法，但必须保留以维持接口布局。
        /// </summary>
        void GetIDList(out IntPtr itemIdentifierList);

        /// <summary>
        /// 设置目标 PIDL；创建流程不使用该方法，但必须保留以维持接口布局。
        /// </summary>
        void SetIDList(IntPtr itemIdentifierList);

        /// <summary>
        /// 读取快捷方式描述；创建流程不使用该方法，但必须保留以维持接口布局。
        /// </summary>
        void GetDescription(StringBuilder description, int descriptionLength);

        /// <summary>
        /// 设置快捷方式描述。
        /// </summary>
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string description);

        /// <summary>
        /// 读取工作目录；创建流程不使用该方法，但必须保留以维持接口布局。
        /// </summary>
        void GetWorkingDirectory(StringBuilder workingDirectory, int workingDirectoryLength);

        /// <summary>
        /// 设置快捷方式工作目录。
        /// </summary>
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string workingDirectory);

        /// <summary>
        /// 读取启动参数；创建流程不使用该方法，但必须保留以维持接口布局。
        /// </summary>
        void GetArguments(StringBuilder arguments, int argumentsLength);

        /// <summary>
        /// 设置启动参数；创建流程不使用该方法，但必须保留以维持接口布局。
        /// </summary>
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);

        /// <summary>
        /// 读取热键；创建流程不使用该方法，但必须保留以维持接口布局。
        /// </summary>
        void GetHotkey(out short hotkey);

        /// <summary>
        /// 设置热键；创建流程不使用该方法，但必须保留以维持接口布局。
        /// </summary>
        void SetHotkey(short hotkey);

        /// <summary>
        /// 读取窗口显示方式；创建流程不使用该方法，但必须保留以维持接口布局。
        /// </summary>
        void GetShowCmd(out int showCommand);

        /// <summary>
        /// 设置窗口显示方式；创建流程不使用该方法，但必须保留以维持接口布局。
        /// </summary>
        void SetShowCmd(int showCommand);

        /// <summary>
        /// 读取图标位置；创建流程不使用该方法，但必须保留以维持接口布局。
        /// </summary>
        void GetIconLocation(StringBuilder iconPath, int iconPathLength, out int iconIndex);

        /// <summary>
        /// 设置快捷方式图标。
        /// </summary>
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex);

        /// <summary>
        /// 设置相对路径；创建流程不使用该方法，但必须保留以维持接口布局。
        /// </summary>
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string relativePath, uint reserved);

        /// <summary>
        /// 解析快捷方式目标；创建流程不使用该方法，但必须保留以维持接口布局。
        /// </summary>
        void Resolve(IntPtr windowHandle, uint flags);

        /// <summary>
        /// 设置快捷方式目标。
        /// </summary>
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string filePath);
    }

    /// <summary>
    /// Windows Shell 的 IPersistFile 接口，用于保存快捷方式文件。
    /// </summary>
    [ComImport]
    [Guid("0000010B-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPersistFile
    {
        /// <summary>
        /// 读取对象类标识；保存流程不使用该方法，但必须保留以维持接口布局。
        /// </summary>
        void GetClassID(out Guid classId);

        /// <summary>
        /// 检查对象是否已修改；保存流程不使用该方法，但必须保留以维持接口布局。
        /// </summary>
        [PreserveSig]
        int IsDirty();

        /// <summary>
        /// 从文件加载对象；保存流程不使用该方法，但必须保留以维持接口布局。
        /// </summary>
        void Load([MarshalAs(UnmanagedType.LPWStr)] string fileName, uint mode);

        /// <summary>
        /// 将快捷方式保存到指定文件。
        /// </summary>
        void Save([MarshalAs(UnmanagedType.LPWStr)] string fileName, [MarshalAs(UnmanagedType.Bool)] bool remember);

        /// <summary>
        /// 完成分阶段保存；保存流程不使用该方法，但必须保留以维持接口布局。
        /// </summary>
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string fileName);

        /// <summary>
        /// 读取当前文件路径；保存流程不使用该方法，但必须保留以维持接口布局。
        /// </summary>
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string fileName);
    }

    /// <summary>
    /// Windows Shell Link COM 组件。
    /// </summary>
    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLink
    {
    }
}
