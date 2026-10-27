# BetterGI_Fix

> 这是 BetterGI 的第三方分支，不是官方版本。

基于 [上游项目](https://github.com/babalae/better-genshin-impact) 维护，新增：

1. 原神启动时解锁帧率，支持 `30–240` 帧及 `90`、`120` 预设。
2. 以全屏窗口化方式启动原神。
3. 在软件设置中管理桌面快捷方式和开始菜单入口。

## 使用

从 [Releases](https://github.com/jiurag/BetterGI_Fix/releases) 下载安装版或便携版。

1. 运行安装器，或解压便携版后运行 `BetterGI.exe`。
2. 在首页开启需要的启动功能，再通过 BetterGI 启动原神。
3. 快捷方式开关位于“软件设置”。

发布文件未签名，Windows 可能提示未知发布者。

## 风险

帧率解锁会修改游戏进程，是否触发反作弊或影响账号安全无法确认。游戏更新、显示器刷新率、垂直同步和显卡设置也可能影响效果。请自行评估风险。

## 构建与发布

更新项目版本并推送 `v*.*.*` 标签后，GitHub Actions 会自动构建和发布。

## 许可证

沿用上游 [GNU GPL v3.0](./LICENSE) 许可证。
