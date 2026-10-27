using System;
using System.Runtime.InteropServices;

namespace BetterGenshinImpact.GameTask.Runtime.Win32;

/// <summary>
/// 帧率解锁使用的窗口消息 Hook Win32 操作。
/// </summary>
internal static class GenshinFpsUnlockNativeMethods
{
    /// <summary>
    /// WH_GETMESSAGE：把 Hook DLL 载入目标窗口线程
    /// </summary>
    private const int WhGetMessage = 3;

    /// <summary>
    /// WM_NULL：只用于触发目标线程处理已安装的 Hook
    /// </summary>
    private const uint WmNull = 0;

    /// <summary>
    /// 从 DLL 所在目录解析依赖
    /// </summary>
    private const uint LoadWithAlteredSearchPath = 0x00000008;

    /// <summary>
    /// 加载 Hook DLL 并返回 BetterGI 侧模块句柄。
    /// </summary>
    /// <param name="path">Hook DLL 完整路径</param>
    /// <param name="win32Error">失败时的 Win32 错误码</param>
    /// <returns>模块句柄；失败时返回 0</returns>
    public static nint LoadHookModule(string path, out int win32Error)
    {
        var handle = LoadLibraryExW(path, 0, LoadWithAlteredSearchPath);
        win32Error = handle == 0 ? Marshal.GetLastWin32Error() : 0;
        return handle;
    }

    /// <summary>
    /// 获取窗口消息 Hook 导出函数地址。
    /// </summary>
    /// <param name="moduleHandle">Hook DLL 模块句柄</param>
    /// <param name="procedureName">导出函数名</param>
    /// <param name="win32Error">失败时的 Win32 错误码</param>
    /// <returns>导出函数地址；失败时返回 0</returns>
    public static nint GetHookProcedure(
        nint moduleHandle,
        string procedureName,
        out int win32Error)
    {
        var procedure = GetProcAddress(moduleHandle, procedureName);
        win32Error = procedure == 0 ? Marshal.GetLastWin32Error() : 0;
        return procedure;
    }

    /// <summary>
    /// 获取窗口所属线程和进程 ID。
    /// </summary>
    /// <param name="windowHandle">窗口句柄</param>
    /// <param name="processId">窗口所属进程 ID</param>
    /// <param name="win32Error">失败时的 Win32 错误码</param>
    /// <returns>窗口线程 ID；失败时返回 0</returns>
    public static uint GetWindowThreadProcessId(
        nint windowHandle,
        out uint processId,
        out int win32Error)
    {
        var threadId = GetWindowThreadProcessId(windowHandle, out processId);
        win32Error = threadId == 0 ? Marshal.GetLastWin32Error() : 0;
        return threadId;
    }

    /// <summary>
    /// 在目标窗口线程安装 WH_GETMESSAGE 钩子。
    /// </summary>
    /// <param name="hookProcedure">Hook DLL 导出函数地址</param>
    /// <param name="moduleHandle">Hook DLL 模块句柄</param>
    /// <param name="threadId">目标窗口线程 ID</param>
    /// <param name="win32Error">失败时的 Win32 错误码</param>
    /// <returns>Hook 句柄；失败时返回 0</returns>
    public static nint SetMessageHook(
        nint hookProcedure,
        nint moduleHandle,
        uint threadId,
        out int win32Error)
    {
        var hook = SetWindowsHookExW(
            WhGetMessage,
            hookProcedure,
            moduleHandle,
            threadId);
        win32Error = hook == 0 ? Marshal.GetLastWin32Error() : 0;
        return hook;
    }

    /// <summary>
    /// 向原神窗口线程投递 WM_NULL，触发 Hook DLL 载入。
    /// </summary>
    /// <param name="threadId">目标窗口线程 ID</param>
    /// <param name="win32Error">失败时的 Win32 错误码</param>
    /// <returns>消息投递成功时返回 true</returns>
    public static bool PostThreadMessage(uint threadId, out int win32Error)
    {
        var success = PostThreadMessageW(threadId, WmNull, 0, 0);
        win32Error = success ? 0 : Marshal.GetLastWin32Error();
        return success;
    }

    /// <summary>
    /// 移除窗口消息钩子。
    /// </summary>
    /// <param name="hookHandle">Hook 句柄</param>
    /// <returns>成功移除时返回 true</returns>
    public static bool RemoveMessageHook(nint hookHandle)
    {
        return UnhookWindowsHookEx(hookHandle);
    }

    /// <summary>
    /// 释放 BetterGI 对 Hook DLL 的模块引用。
    /// </summary>
    /// <param name="moduleHandle">Hook DLL 模块句柄</param>
    /// <returns>成功释放时返回 true</returns>
    public static bool FreeHookModule(nint moduleHandle)
    {
        return FreeLibrary(moduleHandle);
    }

    /// <summary>
    /// 加载动态链接库。
    /// </summary>
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint LoadLibraryExW(
        string fileName,
        nint fileHandle,
        uint flags);

    /// <summary>
    /// 获取动态链接库导出函数地址。
    /// </summary>
    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern nint GetProcAddress(
        nint moduleHandle,
        string procedureName);

    /// <summary>
    /// 释放动态链接库模块引用。
    /// </summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeLibrary(nint moduleHandle);

    /// <summary>
    /// 获取窗口所属线程和进程 ID。
    /// </summary>
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(
        nint windowHandle,
        out uint processId);

    /// <summary>
    /// 安装 Windows 窗口消息钩子。
    /// </summary>
    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetWindowsHookExW(
        int hookType,
        nint hookProcedure,
        nint moduleHandle,
        uint threadId);

    /// <summary>
    /// 向指定线程消息队列投递消息。
    /// </summary>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessageW(
        uint threadId,
        uint message,
        nuint wParam,
        nint lParam);

    /// <summary>
    /// 移除 Windows 窗口消息钩子。
    /// </summary>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(nint hookHandle);
}
