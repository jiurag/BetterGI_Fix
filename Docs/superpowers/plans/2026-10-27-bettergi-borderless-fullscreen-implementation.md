# BetterGI 原神全屏窗口化实施计划

## 1. 实施目标

完成单开关“启动时全屏窗口化”、显示器矩形解析、popup 参数构建、启动后窗口定位、单元测试和真实窗口黑盒验证。

## 2. 约束

1. 不改游戏文件，不注入 DLL，不修改游戏进程。
2. 只影响由 BetterGI 启动的原神。
3. 不提供独占全屏功能。
4. 子会话继续使用 `1920x1080` 固定参数。
5. 不破坏现有帧率解锁功能和未提交工作区改动。
6. 不使用固定 `1920x1080` 兜底。

## 3. 实施步骤

### Step 1: 配置与首页单开关

- Output:
  - `GenshinStartConfig` 只保留 `BorderlessFullscreenEnabled`。
  - 首页显示单个“启动时全屏窗口化”开关。
  - 删除独占全屏子项、绑定、说明和国际化资源。
- Test:
  - 配置字段默认关闭。
  - 源码中不存在独占全屏配置或绑定。
  - 四个语言 JSON 可以正常解析。

### Step 2: 参数构建

- Output:
  - 开关关闭时不注入参数。
  - 开关开启时追加 `-popupwindow -screen-width W -screen-height H`。
  - 清理用户的冲突参数。
  - 结果绝不包含 `-screen-fullscreen` 或 `-window-mode`。
- Test:
  - 覆盖空参数、普通参数、冲突参数、大小写、等号、引号、无效尺寸和子会话。

### Step 3: 显示器解析和窗口定位

- Output:
  - 保存 BetterGI 所在显示器的完整矩形和负坐标。
  - 原神窗口出现后使用 `SetWindowPos` 定位。
  - 定位失败只记录日志，不中断启动。
- Test:
  - 日志包含显示器坐标、宽高、最终参数和定位结果。

### Step 4: 编译和静态检查

- Output:
  - 主项目、测试项目和 XAML 编译通过。
  - 参数测试全部通过。
  - 国际化 JSON 可解析。
  - `git diff --check` 无输出。
- Test:

```powershell
dotnet test Test\BetterGenshinImpact.UnitTest\BetterGenshinImpact.UnitTest.csproj -c Debug --filter "FullyQualifiedName~GenshinStartArgumentsBuilderTests"
```

### Step 5: 部署和黑盒验证

- Output:
  - 部署最新构建。
  - 由 BetterGI 启动原神。
  - 记录最终参数、窗口样式、窗口矩形、客户区和最小化状态。
- Test:
  - 最终参数包含 `-popupwindow`，不含全屏模式和窗口模式参数。
  - 窗口铺满 BetterGI 所在显示器。
  - Alt+Tab 后 `IsMinimized=False`。
  - BetterGI 可以绑定窗口并启动截图器。
