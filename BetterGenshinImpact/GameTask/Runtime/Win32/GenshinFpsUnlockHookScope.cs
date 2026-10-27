using System;

namespace BetterGenshinImpact.GameTask.Runtime.Win32;

/// <summary>
/// 一次窗口消息 Hook 的模块和钩子生命周期。
/// </summary>
internal sealed class GenshinFpsUnlockHookScope : IDisposable
{
    /// <summary>
    /// Hook DLL 的导出函数名
    /// </summary>
    private const string HookProcedureName = "GenshinFpsUnlockHookProc";

    /// <summary>
    /// 载入的 Hook DLL 模块句柄
    /// </summary>
    private readonly nint _moduleHandle;

    /// <summary>
    /// Windows 消息钩子句柄
    /// </summary>
    private readonly nint _hookHandle;

    /// <summary>
    /// 目标原神窗口线程 ID
    /// </summary>
    private readonly uint _threadId;

    /// <summary>
    /// 创建已经安装完成的 Hook 作用域。
    /// </summary>
    /// <param name="moduleHandle">Hook DLL 模块句柄</param>
    /// <param name="hookHandle">窗口消息钩子句柄</param>
    /// <param name="threadId">目标窗口线程 ID</param>
    private GenshinFpsUnlockHookScope(
        nint moduleHandle,
        nint hookHandle,
        uint threadId)
    {
        _moduleHandle = moduleHandle;
        _hookHandle = hookHandle;
        _threadId = threadId;
    }

    /// <summary>
    /// 加载 Hook DLL 并把 WH_GETMESSAGE 钩子安装到原神窗口线程。
    /// </summary>
    /// <param name="hookDllPath">Hook DLL 完整路径</param>
    /// <param name="gameWindowHandle">原神窗口句柄</param>
    /// <param name="expectedProcessId">期望的原神进程 ID</param>
    /// <param name="scope">成功后的 Hook 作用域</param>
    /// <param name="win32Error">失败时的 Win32 错误码</param>
    /// <returns>成功安装时返回 true</returns>
    public static bool TryCreate(
        string hookDllPath,
        nint gameWindowHandle,
        int expectedProcessId,
        out GenshinFpsUnlockHookScope? scope,
        out int win32Error)
    {
        scope = null;
        nint moduleHandle = 0;
        nint hookHandle = 0;

        try
        {
            moduleHandle = GenshinFpsUnlockNativeMethods.LoadHookModule(
                hookDllPath,
                out win32Error);
            if (moduleHandle == 0)
            {
                return false;
            }

            var hookProcedure = GenshinFpsUnlockNativeMethods.GetHookProcedure(
                moduleHandle,
                HookProcedureName,
                out win32Error);
            if (hookProcedure == 0)
            {
                return false;
            }

            var threadId = GenshinFpsUnlockNativeMethods.GetWindowThreadProcessId(
                gameWindowHandle,
                out var windowProcessId,
                out win32Error);
            if (threadId == 0)
            {
                return false;
            }

            if (windowProcessId != (uint)expectedProcessId)
            {
                win32Error = 87;
                return false;
            }

            hookHandle = GenshinFpsUnlockNativeMethods.SetMessageHook(
                hookProcedure,
                moduleHandle,
                threadId,
                out win32Error);
            if (hookHandle == 0)
            {
                return false;
            }

            scope = new GenshinFpsUnlockHookScope(
                moduleHandle,
                hookHandle,
                threadId);
            win32Error = 0;
            return true;
        }
        finally
        {
            if (scope is null)
            {
                if (hookHandle != 0)
                {
                    GenshinFpsUnlockNativeMethods.RemoveMessageHook(hookHandle);
                }

                if (moduleHandle != 0)
                {
                    GenshinFpsUnlockNativeMethods.FreeHookModule(moduleHandle);
                }
            }
        }
    }

    /// <summary>
    /// 向原神窗口线程投递空消息，触发 Windows 把 Hook DLL 映射进游戏进程。
    /// </summary>
    /// <param name="win32Error">失败时的 Win32 错误码</param>
    /// <returns>消息投递成功时返回 true</returns>
    public bool TryActivate(out int win32Error)
    {
        return GenshinFpsUnlockNativeMethods.PostThreadMessage(
            _threadId,
            out win32Error);
    }

    /// <summary>
    /// 移除窗口消息钩子并释放 BetterGI 侧的 DLL 模块引用。
    /// </summary>
    public void Dispose()
    {
        if (_hookHandle != 0)
        {
            GenshinFpsUnlockNativeMethods.RemoveMessageHook(_hookHandle);
        }

        if (_moduleHandle != 0)
        {
            GenshinFpsUnlockNativeMethods.FreeHookModule(_moduleHandle);
        }
    }
}
