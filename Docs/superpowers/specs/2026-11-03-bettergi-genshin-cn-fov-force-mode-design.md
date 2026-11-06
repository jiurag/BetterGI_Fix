# BetterGI 国服原神强制 FOV 模式设计

## 1. 背景

当前 FOV 功能通过 `GenshinFpsUnlockHook.dll` 在国服原神的 FOV
写入函数上安装硬件断点。Hook 默认只替换以下两类请求：

1. 游戏普通视角使用的 `45.0f`。
2. 已经等于当前目标 FOV 的回写。

技能、瞄准、剧情和其他特殊镜头使用的其他 FOV 值会原样放行。因此角色释放
技能时，视角会短暂回到游戏原始值，技能结束后再恢复用户的 FOV。

用户已确认选择“强制 FOV 模式”：开启后，经过当前 FOV Hook 的所有写入都替换
成用户目标值，不再区分普通视角和特殊镜头。

## 2. 目标

1. 新增“强制 FOV 模式”开关，默认关闭。
2. 保持现有 FOV 调整开关、目标值、输入校验和快捷预设。
3. 强制模式仅在 FOV 调整开启且目标值合法时生效。
4. 强制模式开启后，`HookSetFieldOfView` 收到的每次调用都使用目标 FOV。
5. 强制模式关闭后，恢复当前“普通视角替换、特殊镜头放行”的行为。
6. 支持游戏运行中切换，无需重启 BetterGI 或原神。
7. 不改变当前 100 字节共享内存布局和布局版本。
8. 不修改游戏磁盘文件或 `.text` 代码页。
9. 增加自动化测试，并给出技能、瞄准、剧情和关闭恢复的黑盒验收步骤。

## 3. 非目标

1. 不保证覆盖没有经过当前 `HookSetFieldOfView` 函数的所有镜头路径。
2. 不保证强制模式下技能、瞄准、剧情和过场构图正常。
3. 不增加反作弊隐藏、检测绕过、进程隐藏或账号保护能力。
4. 不扩展国际服 `GenshinImpact.exe` 的 FOV 支持。
5. 不重构去雾、帧率解锁或现有硬件断点管理器。

## 4. 已核验现状

1. `GenshinStartConfig` 当前已有 `FovAdjustmentEnabled`、`FovTarget` 和
   `IsFovTargetInputValid`，没有强制模式配置。
2. `GenshinFpsUnlockHookChannel.CurrentLayoutVersion` 当前为 `6`。
3. 现有功能位为：
   - 去雾：`1 << 0`。
   - 帧率解锁：`1 << 1`。
   - FOV 调整：`1 << 2`。
4. `GenshinFpsUnlockSharedState` 当前为 25 个四字节字段，共 100 字节。
5. `GenshinFpsUnlockSession` 每 `62 ms` 更新功能位、FOV 目标和心跳。
6. `HookSetFieldOfView` 当前只在普通视角、当前目标值和目标变化后的首次命中时
   替换，其他请求调用原始函数放行。
7. 当前 FOV 功能仍使用硬件断点，不修改游戏代码页。

## 5. 方案选择

### 5.1 采用方案

在现有 `FeatureFlags` 中增加强制 FOV 功能位：

```text
FovForceModeFeatureFlag = 1 << 3
```

该方案不新增共享内存字段，不升级布局版本，C# 与 C++ 只同步新增功能位的语义。

### 5.2 未采用方案

1. 共享内存新增 `FovForceMode` 字段并升级布局到 `7`：
   语义更直观，但不必要地扩大布局变更和旧 DLL 错配范围。
2. 永久改成无条件替换：
   会移除现有特殊镜头兼容行为，用户无法切回普通替换模式。

## 6. 配置设计

在 `GenshinStartConfig` 增加：

```csharp
[ObservableProperty]
private bool _fovForceModeEnabled;
```

规则：

1. 默认值为 `false`，保证升级后行为不变。
2. 强制模式不单独启动 Hook 会话。
3. `FovAdjustmentEnabled=false` 时，即使强制模式保留为 `true`，也不会设置运行期
   功能位。
4. `IsFovTargetInputValid=false` 或目标超出 `15～165` 时，强制模式不生效。
5. 关闭 FOV 调整后保留强制模式配置，方便用户下次开启 FOV 时继续使用。

## 7. 界面设计

在首页现有 FOV 设置块中增加：

```text
强制所有镜头使用目标 FOV
```

交互规则：

1. 使用 `ToggleSwitch`，绑定
   `Config.GenshinStartConfig.FovForceModeEnabled`，显式使用 `Mode=TwoWay`。
2. FOV 调整关闭时，开关显示为禁用状态。
3. 开启时显示风险提示：可能影响技能、瞄准、剧情和特殊镜头。
4. 原说明“技能、剧情和其他特殊镜头保持游戏原值”需要标注为普通模式行为，
   避免与强制模式冲突。
5. 不新增第二个状态枚举；现有 FOV 状态继续表示 Hook 是否已启用、失败或停止。

## 8. 功能位与会话设计

### 8.1 功能位

`GenshinFpsUnlockHookChannel` 增加：

```csharp
public const int FovForceModeFeatureFlag = 1 << 3;
```

`GenshinFpsUnlockSession.CreateFeatureFlags` 只在以下条件全部满足时设置该位：

1. 目标进程是国服原神。
2. `FovAdjustmentEnabled=true`。
3. `FovForceModeEnabled=true`。
4. `IsFovTargetInputValid=true`。
5. `GenshinStartConfig.IsValidFovTarget(FovTarget)=true`。

### 8.2 会话

`GenshinFpsUnlockSession` 每轮继续调用 `CreateFeatureFlags`。用户切换强制模式后，
下一轮共享状态更新会反映新功能位，Hook 无需重新注入。

会话日志增加：

1. 启动日志记录强制模式开关。
2. 记录强制模式功能位从关闭到开启、从开启到关闭的变化。
3. 不因强制模式切换重复输出持续状态。

## 9. Native Hook 设计

### 9.1 功能位

在 `GenshinFpsUnlockHook.cpp` 增加：

```cpp
constexpr LONG FovForceModeFeatureFlag = 1 << 3;
```

增加进程内原子缓存：

```cpp
volatile LONG g_fovForceModeEnabled = 0;
```

工作线程每轮根据 `FeatureFlags` 更新该缓存。Hook 回调只读取进程内缓存，不直接
依赖共享内存指针生命周期。

### 9.2 替换流程

`HookSetFieldOfView` 保持以下前置条件：

1. 原始 trampoline 可用。
2. FOV 调整已启用。
3. 目标值在 `15～165` 内。

满足前置条件后：

1. 强制模式开启：无条件调用原始函数并传入目标 FOV。
2. 强制模式关闭：继续使用当前的 `45.0f`、当前目标值和一次性强制覆盖逻辑。
3. 每次实际替换递增 `FovReplaceCount`。
4. 每次入口调用继续递增 `FovHookHitCount`。
5. 不修改游戏代码页，不修改游戏磁盘文件。

### 9.3 停止与失败

1. FOV 功能位关闭时清除强制模式缓存并卸载 FOV 硬件断点。
2. 心跳超时或会话停止时先清除强制模式，再按现有顺序恢复 FOV。
3. FOV 安装或恢复失败只影响 FOV，不中止去雾和帧率。
4. 非法目标值不替换，继续调用原始函数。

## 10. 数据流

1. 用户开启 FOV 调整和强制模式。
2. 会话生成 `FovAdjustmentFeatureFlag | FovForceModeFeatureFlag`。
3. Hook 工作线程读取功能位并设置 `g_fovForceModeEnabled=1`。
4. 游戏调用 FOV 写入函数。
5. Hook 将本次请求替换为用户目标值并调用 trampoline。
6. 用户关闭强制模式。
7. 下一轮会话更新清除功能位。
8. Hook 工作线程设置 `g_fovForceModeEnabled=0`，恢复选择性替换。

## 11. 错误处理

1. 强制模式配置不能绕过 FOV 总开关和目标值校验。
2. 非国服进程不设置 FOV 或强制模式功能位。
3. 目标输入暂时非法时，强制模式不生效，但不停止当前会话。
4. 强制模式开关本身不产生新的 Hook 安装阶段或错误码。
5. FOV 未安装成功时，强制模式状态可以保留，但必须显示 FOV 功能失败。
6. 不新增静默地址猜测或失败后的硬编码 RVA 兜底。

## 12. 自动化测试

### 12.1 配置测试

1. `FovForceModeEnabled` 默认 `false`。
2. 开启 FOV 和强制模式后，属性值可正常保存。

### 12.2 功能位测试

1. 国服、FOV 开启、目标合法、强制模式开启：
   包含 `FovAdjustmentFeatureFlag | FovForceModeFeatureFlag`。
2. 强制模式开启但 FOV 关闭：
   不包含 FOV 或强制模式功能位。
3. 强制模式开启但目标输入非法：
   不包含 FOV 或强制模式功能位。
4. 非国服、FOV 和强制模式开启：
   不包含 FOV 或强制模式功能位。
5. 共享内存大小仍为 100 字节，布局版本仍为 6。

### 12.3 Native 源码契约测试

1. 源码包含 `FovForceModeFeatureFlag=1<<3`。
2. 源码包含进程内强制模式原子缓存。
3. 强制模式检查位于特殊镜头放行判断之前。
4. 现有 17 字节 FOV 序言、特征和布局版本 6 保持不变。

### 12.4 构建测试

运行：

```powershell
dotnet test Test\BetterGenshinImpact.UnitTest\BetterGenshinImpact.UnitTest.csproj -c Debug --filter "FullyQualifiedName~GenshinFpsUnlock"
dotnet msbuild BetterGenshinImpact.sln -restore -p:Configuration=Debug -nologo -tl:off -clp:ErrorsOnly
git diff --check
```

Native DLL 使用现有 `v143` 工程构建；若本机缺少工具链，必须标记为
“本地未验证”，不得声称成功。

## 13. 黑盒验收

测试只通过界面开关、游戏画面和日志判断，不读取或修改游戏内部状态。

1. 普通模式开启 FOV，释放同一技能，记录当前“短暂回到原视角”现象。
2. 开启强制 FOV，再次释放同一技能，确认视角不再跳回。
3. 面对固定场景连续释放多个角色技能，确认技能期间保持目标 FOV。
4. 切换瞄准模式，记录是否保持目标 FOV，以及画面是否可正常操作。
5. 进入剧情或过场，记录是否保持目标 FOV，以及是否出现构图或裁剪异常。
6. 运行中关闭强制模式，确认下一次正常视角写入后恢复普通替换行为。
7. 运行中再次开启强制模式，确认无需重启游戏即可重新接管。
8. 关闭 FOV 调整，确认 FOV Hook 停止并恢复游戏原始行为。
9. 同时开启去雾、帧率和强制 FOV，确认三个功能状态互不影响。
10. 长时间停留固定场景，只记录是否出现 `10010`、`10010-4001`、
    `EnetPingTimeout` 或 Hook 失败，不预设账号安全结论。

黑盒成功标准：

1. 技能期间不再跳回原 FOV。
2. 强制模式可运行中开关。
3. 关闭后恢复现有选择性替换。
4. FOV 失败不影响去雾和帧率。
5. 技能、瞄准、剧情的实际异常由用户确认并记录。

## 14. 风险

1. 技能和瞄准可能被固定在目标 FOV，导致镜头范围或操作手感不自然。
2. 剧情和过场可能依赖动态 FOV 构图，强制接管后可能出现裁剪或画面异常。
3. 当前 Hook 未覆盖的镜头路径无法通过本功能保证生效；该项只能黑盒确认。
4. 账号和反作弊风险未知，本设计不提供隐藏或绕过能力。
5. 强制模式会增加 FOV 替换次数，但计数本身不证明稳定性。
6. 游戏更新后仍依赖现有特征和 17 字节序言校验，失败时安全禁用 FOV。

## 15. 预计修改文件

1. `BetterGenshinImpact/Core/Config/GenshinStartConfig.cs`
2. `BetterGenshinImpact/View/Pages/HomePage.xaml`
3. `BetterGenshinImpact/ViewModel/Pages/HomePageViewModel.cs`
4. `BetterGenshinImpact/GameTask/Runtime/Win32/GenshinFpsUnlockHookChannel.cs`
5. `BetterGenshinImpact/GameTask/Runtime/Win32/GenshinFpsUnlockSession.cs`
6. `BetterGenshinImpact/GameTask/Runtime/Win32/GenshinFpsUnlockService.cs`
7. `BetterGenshinImpact/GameTask/Runtime/Win32/GenshinFpsUnlockSharedState.cs`
8. `BetterGenshinImpact/GameTask/Runtime/Win32/Hook/GenshinFpsUnlockHook.cpp`
9. `Test/BetterGenshinImpact.UnitTest/GameTaskTests/GenshinFpsUnlockTests/`
10. 相关国际化资源。
