# 原神帧率解锁 Hook DLL

该 DLL 只负责在已经由 BetterGI 拉起的原神进程内部定位帧率字段并持续写入目标帧率。

1. BetterGI 创建 `Local\BetterGI.GenshinFpsUnlock.<PID>` 共享内存。
2. BetterGI 使用 `SetWindowsHookExW(WH_GETMESSAGE)` 把本 DLL 载入原神窗口线程。
3. DLL 在游戏进程内部搜索 `il2cpp` 节和帧率特征链。
4. DLL 每 `62ms` 读取一次共享内存中的目标帧率。
5. 开关关闭、BetterGI 正常退出或心跳停止超过 `5s` 时，DLL 尝试恢复原帧率。

## 构建

使用 Visual Studio 的 x64 Native Tools 环境执行：

```powershell
msbuild .\GenshinFpsUnlockHook.vcxproj /p:Configuration=Debug /p:Platform=x64
msbuild .\GenshinFpsUnlockHook.vcxproj /p:Configuration=Release /p:Platform=x64
```

把对应配置生成的 `GenshinFpsUnlockHook.dll` 放到 BetterGI 输出目录根目录，与 `BetterGI.exe` 同级。

该功能属于外部修改游戏进程，反作弊检测风险无法由本仓库保证。
