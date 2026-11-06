# BetterGI FOV 滑条输入设计

## 1. 背景

当前首页 FOV 设置提供一个文本输入框和 `60`、`75`、`90` 三个快捷按钮。
用户要求移除文本输入，只使用滑条调整，并在 BetterGI 重启后保留上次值。

现有运行期链路已经支持动态 FOV：

1. `GenshinFpsUnlockSession` 每 `62 ms` 把配置中的合法 FOV 写入共享内存。
2. Hook 工作线程读取目标值并更新实际替换值。
3. 修改合法 FOV 不需要重启游戏。

现有 `ConfigService` 会监听配置对象变化，并通过防抖任务自动保存配置。因此继续
写入 `GenshinStartConfig.FovTarget` 即可实现记忆，不需要增加额外存储文件。

## 2. 目标

1. 删除 FOV 文本输入框。
2. 删除 `60`、`75`、`90` 快捷按钮。
3. 使用唯一一个整数滑条调整 FOV。
4. 滑条范围为 `15～165`，步长为 `1`。
5. 滑条旁实时显示当前数值。
6. 拖动滑条时立即更新游戏内 FOV。
7. 当前值写入现有 `FovTarget` 配置并自动保存。
8. 重新打开 BetterGI 后，用保存值初始化滑条。
9. 保留 FOV 总开关和强制 FOV 模式。

## 3. 非目标

1. 不修改 FOV Hook 的实现和共享内存布局。
2. 不改变强制 FOV 模式的覆盖范围。
3. 不删除配置层的 FOV 范围校验函数。
4. 不新增配置文件、注册表项或第二次持久化机制。

## 4. 界面设计

FOV 设置块调整为：

```text
启动时调整视野角度（FOV）                          [开关]
仅对 BetterGI 启动的国服原神生效；普通模式下……
支持 15 到 165 的整数 FOV；账号风险未知……
15  [============ 滑条 ============]  当前：75
启用状态：已启用
强制所有镜头使用目标 FOV                            [开关]
开启后技能、瞄准、剧情等镜头也会保持目标 FOV……
```

滑条配置：

1. `Minimum=15`。
2. `Maximum=165`。
3. `TickFrequency=1`。
4. `IsSnapToTickEnabled=true`。
5. `SmallChange=1`。
6. `LargeChange=10`。
7. 绑定值使用 `Mode=TwoWay` 和 `UpdateSourceTrigger=PropertyChanged`。

## 5. ViewModel 设计

新增一个 `double` 类型的滑条绑定属性：

```csharp
[ObservableProperty]
private double _fovTargetSliderValue;
```

WPF `Slider.Value` 原生使用 `double`。ViewModel 属性变化时：

1. 四舍五入为整数。
2. 限制在 `15～165`。
3. 写入 `Config.GenshinStartConfig.FovTarget`。
4. 明确把 `IsFovTargetInputValid` 设为 `true`。

初始化时从 `Config.GenshinStartConfig.FovTarget` 读取保存值。

删除以下仅服务文本输入的成员：

1. `FovTargetText`。
2. `FovValidationMessage`。
3. `HasFovTargetInputError`。
4. `OnFovTargetTextChanged`。
5. `SetFov60Command`。
6. `SetFov75Command`。
7. `SetFov90Command`。

配置层的 `TryParseFovTarget` 和范围校验保留，用于兼容现有配置契约测试。

## 6. 持久化设计

1. 滑条每次有效变化都更新现有 `FovTarget`。
2. `ConfigService` 已通过 `Config.OnAnyChangedAction = RequestSave` 监听变化。
3. `RequestSave` 使用现有防抖写盘机制，不需要在滑条事件中同步写文件。
4. 启动时 ViewModel 从反序列化后的 `FovTarget` 初始化滑条。
5. 非法或越界值由 ViewModel 和 Hook 双重限制。

## 7. 错误处理

1. 保存配置损坏导致 `FovTarget` 越界时，滑条和 ViewModel 将其限制到边界值。
2. 滑条自身不会产生小数或文本解析错误。
3. FOV 总开关关闭时仍可预调滑条，但不会设置运行期 FOV 功能位。
4. 配置写入失败继续沿用 `ConfigService` 现有错误提示与重试机制。

## 8. 测试设计

### 8.1 自动化测试

1. 现有 FOV 范围、功能位、共享布局和 Native 契约测试继续通过。
2. 增加 XAML 契约检查：
   - FOV 滑条最小值为 `15`。
   - 最大值为 `165`。
   - 开启整数吸附。
   - 存在滑条绑定。
   - 不再绑定 `FovTargetText`。
3. 完整执行 FOV 测试和 Debug 解决方案构建。

### 8.2 黑盒验证

1. 打开测试版，确认输入框和三个快捷按钮消失。
2. 拖动滑条，确认显示值在 `15～165` 间按整数变化。
3. 游戏运行时拖动滑条，确认 FOV 实时变化。
4. 选择一个非默认值，关闭 BetterGI 后重新打开。
5. 确认滑条恢复上次值。
6. 开启强制 FOV，释放技能，确认不再跳回原视角。

## 9. 风险

1. 连续拖动会频繁更新运行期目标值；现有会话每 `62 ms` 读取一次，不会按鼠标
   事件数量直接写游戏。
2. 防抖保存可能延迟数百毫秒；正常关闭时 `ConfigService.Flush` 会处理待保存值。
3. 极端 FOV 仍可能导致画面异常，范围继续限制为 `15～165`。

## 10. 预计修改文件

1. `BetterGenshinImpact/View/Pages/HomePage.xaml`
2. `BetterGenshinImpact/ViewModel/Pages/HomePageViewModel.cs`
3. `Test/BetterGenshinImpact.UnitTest/GameTaskTests/GenshinFpsUnlockTests/GenshinFpsUnlockFovContractTests.cs`
