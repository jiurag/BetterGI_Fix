# BetterGI 国服原神 FOV vtable-only 实施计划

## 1. 实施目标

按照
`Docs/superpowers/specs/2026-11-04-bettergi-genshin-cn-fov-vtable-only-design.md`
让三个 FOV 入口优先尝试相机类堆内 vtable 接管，并在强制模式的 CameraPose
vtable 接管后停用失效辅助入口的硬件断点；同时删除正式热路径中的重量探针，
完成自动化测试、Native DLL 构建和本地黑盒验收准备。

## 2. 约束

1. 只在现有 `test/fov-vtable-performance` 分支实施；不推送 `main`，不创建正式 Release。
2. 不启动、关闭、重启或操作游戏。
3. 不修改游戏代码页、`.rdata` 或磁盘游戏文件。
4. 不提交未跟踪的 `$null` 和 `artifacts/`。
5. 不修改去雾和帧率解锁逻辑。
6. 不伪造 Native 构建、测试或黑盒结果。
7. vtable 未唯一匹配时必须安全降级，不写未知地址。
8. 黑盒验证前不打正式标签、不创建正式 Release。

## 3. 实施步骤

### Step 1: 抽象三个 FOV vtable Hook 状态

1. 输出：
   1. 为 `SetField`、`Synchronize`、`CameraPose` 使用同一套状态模型。
   2. 状态包含目标函数、detour、槽号、槽地址、原始函数、命中数和错误。
   3. 保留 `NotInstalled`、`AwaitingHit`、`Active`、`Failed` 四种状态。
2. 测试：
   1. 静态契约确认三个入口均有独立状态。
   2. 确认没有写死 `SetField` 或 `Synchronize` 的 vtable 槽号。

### Step 2: 通用化唯一 vtable 槽定位

1. 输出：
   1. 从相机实例读取 Il2CppClass 和 vtable 数量。
   2. 按精确函数地址查找唯一槽位。
   3. 零匹配和多重匹配都拒绝接管。
2. 测试：
   1. 验证目标函数地址必须与槽值完全一致。
   2. 验证只接受唯一匹配。
   3. 验证异常槽数量不会进入越界扫描。

### Step 3: 安装、校验和恢复三个 vtable Hook

1. 输出：
   1. 写入前只临时修改目标 vtable 项保护属性。
   2. 写入后立即恢复原保护属性。
   3. 每轮检查槽值是否仍为 detour。
   4. 槽值被恢复为原函数时允许重写。
   5. 槽值变成未知地址时拒绝覆盖并记录错误。
2. 测试：
   1. 验证安装失败会清理状态。
   2. 验证游戏恢复槽位后的重写。
   3. 验证停止时只恢复仍指向 detour 的槽位。

### Step 4: 调整 FOV detour 的原始函数调用

1. 输出：
   1. `SetField` 优先调用 vtable 原始函数，硬件断点降级时调用 trampoline。
   2. `Synchronize` 使用相同规则。
   3. `CameraPose` 优先使用已确认的 vtable 原始函数。
   4. 保持强制 FOV、普通 FOV、目标变化和替换计数语义。
2. 测试：
   1. 验证 vtable 原始函数与硬件 trampoline 不会形成递归。
   2. 验证 detour 命中后替换计数继续增长。

### Step 5: 切换安装流程和安全降级

1. 输出：
   1. 保留现有 CameraPose 临时硬件断点，用它取得相机实例。
   2. 获得实例后尝试安装三个 vtable Hook。
   3. 每个成功入口先写入 vtable，再撤除对应硬件断点。
   4. vtable 未成功时，仅在 CameraPose vtable 尚未接管前保留对应硬件断点。
   5. 强制模式确认 CameraPose vtable 已接管最终位姿后，停用
      `SetField`、`Synchronize` 的辅助硬件断点。
   6. vtable 已接管后禁止工作线程重复安装辅助硬件断点；CameraPose 接管丢失时
      才重新允许降级。
2. 测试：
   1. 验证成功入口不会重复安装硬件断点。
   2. 验证 CameraPose 接管前失败入口可走硬件断点降级。
   3. 验证 CameraPose 接管后辅助硬件断点会被撤除且不会重新安装。

### Step 6: 移除正式热路径重量探针

1. 输出：
   1. `HookUpdateCameraPose` 不再调用 `CaptureFovCameraProbe`。
   2. 三个 detour 只在接管完成前轻量记录相机实例。
   3. 探针映射和扫描函数保留为显式调试代码，默认关闭。
   4. 正常路径不再每帧执行 `0x4000` 字节扫描。
2. 测试：
   1. 契约测试禁止热路径调用重量探针。
   2. 默认构建不创建或写入逐帧探针。
   3. 静态检查确认正式路径没有候选扫描调用。

### Step 7: 扩充运行态诊断

1. 输出：
   1. FOV 诊断映射升级布局版本。
   2. 为三个入口记录 vtable 状态、槽号、错误、命中数和是否降级。
   3. 保留现有硬件断点诊断字段。
2. 测试：
   1. 校验诊断结构大小和版本。
   2. 校验三块状态顺序稳定。
   3. 读取脚本能区分 vtable 与硬件断点降级。

### Step 8: 更新文档和契约测试

1. 输出：
   1. 更新 Hook README 中的 FOV 接管流程。
   2. 更新 FOV 契约测试。
   3. 删除“正式热路径必须调用重量探针”的旧断言。
2. 测试：
   1. 静态测试覆盖三个 vtable 状态。
   2. 静态测试覆盖重量探针默认关闭和热路径移除。
   3. 全量运行 `GenshinFpsUnlock` 过滤测试。

### Step 9: 构建和本地验证

1. 输出：
   1. 构建 Native DLL 的 Debug 和 Release。
   2. 构建 BetterGI 测试版。
   3. 把测试版放到独立目录，不覆盖正在运行的工具。
2. 测试：

```powershell
dotnet test Test\BetterGenshinImpact.UnitTest\BetterGenshinImpact.UnitTest.csproj -c Debug --filter "FullyQualifiedName~GenshinFpsUnlock"
dotnet msbuild BetterGenshinImpact.sln -restore -p:Configuration=Debug -nologo -tl:off -clp:ErrorsOnly
git diff --check
```

3. 如果 Native 工具链或构建失败，如实记录失败阶段和原始错误，不宣称成功。

### Step 10: 黑盒验收准备

1. 输出：
   1. 给出独立测试版路径。
   2. 给出共享内存读取方法，显示三个 vtable 槽位、状态、命中数和降级标记。
   3. 给出 FOV 开/关 60 秒 A/B 测试步骤。
2. 测试：
   1. 由用户黑盒执行跑图、技能、碰撞、快速移动、剧情和固定场景观察。
   2. FOV 开启后中位帧率接近关闭 FOV 的基线。
   3. 无 FOV 闪回、无 `FovInstallError`，并观察是否新增 `10010`。
3. 未获得黑盒结果时，只交付测试版，不发布正式版。

## 4. 完成条件

1. CameraPose 强制模式实现 vtable-only；无法匹配的辅助入口完成安全尝试后
   撤除硬件断点。
2. 正式热路径不再执行重量探针。
3. 安全降级和恢复逻辑完整。
4. 自动化测试和可用构建命令通过，或明确列出无法执行的验证。
5. 本地提交不包含 `$null`、`artifacts/` 或无关改动。
6. 只推送现有测试分支，不推送 `main`，不创建正式 Release。
