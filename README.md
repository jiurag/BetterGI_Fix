# BetterGI_Fix

> [!WARNING]
> **这是 BetterGI 的第三方分支（Fork），不是 babalae 的官方版本。**
>
> - 本仓库：[`jiurag/BetterGI_Fix`](https://github.com/jiurag/BetterGI_Fix)
> - 当前发布分支：`main`
> - 上游项目：[`babalae/better-genshin-impact`](https://github.com/babalae/better-genshin-impact)
> - 当前预发布版本：[`v0.66.3-alpha.3`](https://github.com/jiurag/BetterGI_Fix/releases/tag/v0.66.3-alpha.3)

BetterGI_Fix 基于 BetterGI 源码维护，保留上游的视觉识别、自动任务和模拟操作能力，并在本分支中增加原神帧率解锁、启动时全屏窗口化以及快捷方式管理。

本仓库中的问题、构建和发布均针对这个分支。上游官方问题请不要提交到这里；只属于官方版本的问题，请前往[上游仓库](https://github.com/babalae/better-genshin-impact)反馈。

## 分支新增功能

### 1. 启动时解锁帧率

- 支持 `30–240` 的整数帧率。
- 默认目标帧率为 `120`。
- 提供 `90` 和 `120` 两个快捷预设。
- 仅在原神由 BetterGI 启动时尝试生效。
- 开关关闭、BetterGI 正常退出或心跳超时后，Hook 会尝试恢复进入前的帧率值。

该功能通过窗口消息 Hook 把 `GenshinFpsUnlockHook.dll` 载入原神进程，再通过共享内存持续传递目标帧率。

### 2. 启动时全屏窗口化

- 使用 `-popupwindow` 启动原神。
- 根据 BetterGI 主窗口所在显示器追加 `-screen-width` 和 `-screen-height`。
- 游戏窗口出现后使用 `SetWindowPos` 铺满该显示器的完整物理区域。
- 目标是切换窗口时不自动最小化；实际行为仍受游戏版本、Windows 设置和显卡驱动影响。
- 不提供独占全屏模式。

全屏窗口化只处理启动参数和窗口位置，不修改游戏文件或游戏进程内存。

### 3. 桌面与开始菜单快捷方式

- 在“软件设置”中的“桌面与开始菜单”展开项里提供两个独立开关。
- 打开开关会创建快捷方式，关闭开关会删除对应快捷方式。
- 目标是当前正在运行的 `BetterGI.exe`，工作目录和程序图标会自动写入快捷方式。
- 安装版通常已经由安装器自动创建公共桌面和开始菜单入口；便携版可使用这两个开关手动创建。
- 关闭开始菜单开关时会删除 `BetterGI.lnk`；BetterGI 开始菜单文件夹为空时也会一并清理。

## 下载与使用

前往[本分支的 Releases](https://github.com/jiurag/BetterGI_Fix/releases)下载最新预发布资产。

1. 下载 `BetterGI.Install.*.exe` 安装版，或 `BetterGI_v*.zip` 便携版。
2. 安装版直接运行安装器并选择安装目录；便携版解压后运行 `BetterGI` 目录中的 `BetterGI.exe`。
3. 安装或解压时请选择非系统盘目录，避免占用 C 盘空间。
4. 打开首页的“原神，启动！”区域。
5. 按需开启“启动时解锁帧率”和“启动时全屏窗口化”。
6. 便携版可以在“软件设置”中按需开启桌面或开始菜单快捷方式。
7. 通过 BetterGI 启动原神。

发布页同时提供安装器和便携 ZIP。`BetterGI.exe` 与安装器均未进行数字签名，Windows SmartScreen 可能提示“未知发布者”。

## 环境要求

- Windows 10 或更高版本的 64 位系统。
- 原神国服或国际服客户端。
- 帧率解锁和全屏窗口化只影响由 BetterGI 拉起的原神进程。
- 发布包已包含所需的 .NET 运行时，不需要额外安装 .NET。

上游对图像识别性能和 `16:9` 分辨率有要求。游戏亮度、画面滤镜、HDR、显示器和分辨率变化仍可能影响视频识别任务，详细说明请参考[上游文档](https://www.bettergi.com/doc.html)。

## 风险说明

> [!CAUTION]
> 帧率解锁会修改正在运行的游戏进程。是否会被反作弊检测、是否影响账号安全，本分支无法确认，也不作保证。

1. 使用帧率解锁前，请自行评估账号风险。
2. 游戏更新可能改变内存特征，导致帧率解锁失败或暂时失效。
3. 不同显示器、刷新率、垂直同步和显卡设置下，实际帧率可能低于目标值。
4. 全屏窗口化的切窗行为不能保证在所有游戏版本和系统环境中完全一致。
5. 不要将本分支构建描述为 BetterGI 官方版本。

## 构建

构建环境：

- Visual Studio 2026，安装 x64 C++ 桌面开发组件。
- .NET 10 SDK。

在 Visual Studio 的 x64 Native Tools 环境中构建原生 Hook：

```powershell
msbuild .\BetterGenshinImpact\GameTask\Runtime\Win32\Hook\GenshinFpsUnlockHook.vcxproj /p:Configuration=Release /p:Platform=x64
```

构建主程序：

```powershell
dotnet publish .\BetterGenshinImpact\BetterGenshinImpact.csproj `
  -c Release `
  -p:PublishProfile=FolderProfile
```

更完整的编译说明见 [`BetterGenshinImpact/README.md`](./BetterGenshinImpact/README.md)。

## 自动发布

`.github/workflows/publish.yml` 支持两种发布方式：

- 手动运行工作流：完成构建后，按 `create-release` 输入决定是否创建草稿 Release。
- 推送 `v*.*.*` 标签：自动构建主程序、便携 ZIP、安装器和 SHA-256 文件，并直接发布正式 Release。

发布新版本时，建议先让项目版本与标签保持一致：

1. 修改 `BetterGenshinImpact/BetterGenshinImpact.csproj` 中的 `<Version>`。
2. 提交并推送 `main`。
3. 创建并推送版本标签，例如 `v0.66.3-alpha.4`。

本仓库的 `origin` 可能仍指向上游仓库，推送时应明确指定 BetterGI_Fix：

```powershell
git push https://github.com/jiurag/BetterGI_Fix.git main
git tag v0.66.3-alpha.4
git push https://github.com/jiurag/BetterGI_Fix.git v0.66.3-alpha.4
```

标签构建不会自动把版本号提交回 `main`，因此应在推送标签前完成版本号提交。标签名不符合语义版本格式时，工作流会在校验阶段失败，不会发布 Release。

## 验证范围

本分支的当前发布构建已完成：

- 原生 Hook DLL 和主程序的 x64 Release 构建。
- 启动参数测试 `12/12` 通过。
- 桌面和开始菜单快捷方式已完成创建、Windows Shell 反向读取、删除及空目录清理黑盒测试。
- `en`、`it`、`ja`、`ru` 四个语言 JSON 解析检查。
- 发布 ZIP 全条目可读取、路径安全检查和 SHA-256 校验。

游戏内帧率是否实际写入成功，以及 Alt+Tab 后窗口是否保持不最小化，仍需要在目标电脑和当前游戏版本上做人工黑盒验收。

## 上游与许可证

- 上游项目：[`babalae/better-genshin-impact`](https://github.com/babalae/better-genshin-impact)
- 上游功能文档：[`bettergi.com`](https://www.bettergi.com/)
- 本仓库许可证：[GNU General Public License v3.0](./LICENSE)

本分支保留上游版权与许可证声明。上游作者和贡献者仍是 BetterGI 主体功能的主要作者；本分支只维护上述差异及相关构建发布。

## 反馈

- 本分支问题：[`jiurag/BetterGI_Fix Issues`](https://github.com/jiurag/BetterGI_Fix/issues)
- 本分支发布：[`jiurag/BetterGI_Fix Releases`](https://github.com/jiurag/BetterGI_Fix/releases)
- 上游问题：[`babalae/better-genshin-impact Issues`](https://github.com/babalae/better-genshin-impact/issues)
