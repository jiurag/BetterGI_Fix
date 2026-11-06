# BetterGI 国服原神 FOV 硬件断点实施计划

## 1. 实施目标

按照
`Docs/superpowers/specs/2026-11-03-bettergi-genshin-cn-fov-hwbp-design.md`
恢复国服原神 FOV，使用现有硬件断点管理器，不修改游戏代码页，并完成配置、
界面、共享状态、Native Hook、自动化测试、构建和黑盒验收交付。

## 2. 约束

1. 只支持 BetterGI 启动的国服 `YuanShen.exe`。
2. 不启动、关闭、重启或操作游戏。
3. 不修改磁盘上的游戏文件。
4. FOV、去雾和帧率独立启停。
5. 保留现有未跟踪项 `$null` 和 `artifacts/`。
6. 不恢复历史代码页内联 Hook。
7. 不猜测特征地址，不硬编码 RVA 兜底。
8. 不承诺反作弊安全。
9. 实际画面和错误码只能通过用户黑盒确认。
10. 未验证项必须明确标注，不得伪造测试结果。

## 3. 实施步骤

### Step 1: 配置、界面和 ViewModel

- Output:
  - `GenshinStartConfig` 增加 FOV 范围、默认值、开关、目标和输入合法性。
  - 首页增加 FOV 开关、输入框、错误文字和 `60/75/90` 预设。
  - ViewModel 增加实时整数校验、预设命令、独立状态文字。
  - 国际化文件增加 FOV 文案。
- Test:
  - `15`、`165`、`60` 合法。
  - `0`、`14`、`166`、负数、空值、小数和字母非法。
  - 非法输入不覆盖最后一次合法目标。
  - XAML 展示绑定显式使用 `Mode=OneWay` 或 `Mode=TwoWay`。

### Step 2: 共享状态、功能位和宿主状态

- Output:
  - 共享布局版本升为 `6`。
  - 增加 FOV 功能位 `1 << 2`。
  - 追加 FOV 状态、RVA、目标、命中、替换、安装阶段和错误码字段。
  - `GenshinFpsUnlockRuntimeStatus`、服务、会话和通道支持 FOV。
- Test:
  - C# 结构为 `25` 个四字节字段，共 `100` 字节。
  - 国服且输入合法时设置 FOV 功能位。
  - 非国服或输入非法时不设置 FOV 功能位。
  - 输入非法但开关开启时不停止整个会话。
  - 三个功能全部关闭时停止会话。

### Step 3: 会话动态更新和日志

- Output:
  - 会话创建时写入初始 FOV 目标。
  - 每 `62 ms` 更新功能位与合法目标值。
  - FOV 状态进入独立状态事件。
  - 记录 RVA、目标、首次命中、首次替换、失败阶段、错误码和零命中警告。
- Test:
  - FOV 单独开启即可创建会话。
  - 运行中修改合法目标后共享内存更新。
  - FOV 失败不改变去雾和帧率状态。
  - 状态无变化时不重复刷日志。

### Step 4: Native FOV 特征和硬件断点

- Output:
  - 增加 FOV 特征、掩码和 17 字节完整序言。
  - 增加 FOV trampoline 原型和 detour。
  - 使用 `InstallHardwareBreakpoint` 安装，不修改代码页。
  - 支持新线程刷新、动态目标、强制首次替换和命中/替换计数。
  - 关闭、心跳超时和会话停止时独立恢复 FOV 调试寄存器。
- Test:
  - 当前国服 EXE 特征唯一且入口序言完全匹配。
  - 零命中、多命中和序言不匹配时安全失败。
  - `45.0f ± 0.01f` 和当前目标值被替换，其他镜头值放行。
  - 去雾开启时 FOV 仍能独立安装，停止顺序为 FOV、去雾、帧率。

### Step 5: 自动化测试

- Output:
  - 行为测试覆盖配置边界、功能位、会话状态和布局契约。
  - 静态测试覆盖 FOV 特征、17 字节序言、版本和功能位一致性。
  - 测试文件放在现有 `GenshinFpsUnlockTests` 目录。
- Test:

```powershell
dotnet test Test\BetterGenshinImpact.UnitTest\BetterGenshinImpact.UnitTest.csproj -c Debug --filter "FullyQualifiedName~GenshinFpsUnlock"
```

### Step 6: 构建和静态自查

- Output:
  - C# 主项目和测试项目构建通过。
  - Native DLL 使用现有 Visual Studio `v143` 工程构建；本机缺少工具链时如实记录未验证。
  - 所有源码文件为 UTF-8 无 BOM。
  - 差异无空白错误、占位符和未提交用户文件。
- Test:

```powershell
dotnet msbuild BetterGenshinImpact.sln -restore -p:Configuration=Debug -nologo -tl:off -clp:ErrorsOnly
git diff --check
```

### Step 7: 黑盒验收交付

- Output:
  - 给出构建产物位置、启动方式和逐项黑盒测试表。
  - 说明实际画面、技能、剧情、动态修改、停止恢复和错误码仍待用户确认。
- Test:
  - 用户只通过界面、游戏画面和日志验收。
  - 未完成黑盒确认前，不宣称 FOV 实际效果成功。

## 4. 完成条件

1. 配置、界面、共享状态、宿主逻辑和 Native Hook 全部实现。
2. 自动化测试和可用构建命令通过，或明确列出无法执行的验证。
3. 当前国服 7.1.0 二进制静态核验通过。
4. 没有把用户未跟踪项混入提交。
5. 用户黑盒测试项和未验证风险已清楚交付。
