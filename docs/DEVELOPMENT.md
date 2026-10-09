# DesktopIconsStorage 开发文档

本文档面向需要阅读、构建、调试或维护 DesktopIconsStorage 的开发者。产品使用说明请先阅读仓库根目录的 [README](../README.md)。

## 1. 技术概览

- 语言：C#
- 运行时：.NET 8
- UI：WPF，同时使用 WinForms `NotifyIcon`
- 平台：Windows 10/11，目标运行时 `win-x64`
- 外部依赖：无 NuGet 运行时依赖
- 安装器：Inno Setup 6
- CI/CD：GitHub Actions

应用使用顶层无边框 `WS_POPUP` 窗口，并把桌面宿主设置为 owner。收纳盒位于壁纸之上、普通应用窗口之下，不通过 `SetParent` 创建 WPF 子窗口。

## 2. 解决方案结构

```text
DesktopIconsStorage.sln
src/
  Core/
    Models/                     设置、收纳盒和文件项目模型
    Services/                   文件移动、JSON 存储和文件夹监听
  Platform.Windows/
    Native/                     Win32、DWM、Shell COM 声明
    Services/                   桌面挂接、主题、图标、右键菜单、自启等
  App/
    Views/                      WPF 视图及交互
    Styles/                     全局控件和盒体菜单样式
    Helpers/                    应用中枢和 UI 辅助模型
    Tray/                       系统托盘及主题化菜单
    Assets/                     应用图标
build/
  installer/                    Inno Setup 安装脚本
scripts/
  build-release.ps1             自包含发布和安装器构建脚本
.github/workflows/
  release.yml                   GitHub Release 自动构建
docs/
  DEVELOPMENT.md                本文档
```

三个项目的职责：

| 项目 | 职责 |
| --- | --- |
| `DesktopIconsStorage.Core` | 不依赖 UI 的模型、配置、文件移动和冲突处理。 |
| `DesktopIconsStorage.Platform.Windows` | Windows API、Shell、DWM、注册表和桌面宿主能力。 |
| `DesktopIconsStorage.App` | WPF 界面、窗口生命周期、拖放、托盘和应用编排。 |

实体盒与链接筐分别使用 `BlockView`、`LinkBasketTileView`/`LinkBasketPopupView`。两种模式共享 `Block`、`BlockManager`、`BlockWindow` 和 `AppHost` 的入口与生命周期，因此分支合并仍可能在这些文件的相同改动处产生冲突；冲突位置由具体改动决定，不代表内容语义必须耦合在同一个视图中。

## 3. 启动流程

1. `App.OnStartup` 首先识别维护命令，例如卸载清理。
2. `JsonStore.MigrateLegacyFiles` 将旧版 `DesktopOrganizer` 设置复制到新配置目录。
3. `AppHost.Run` 获取单实例互斥锁并加载设置和布局。
4. `BlockManager` 创建或加载收纳盒模型。
5. 每个模型对应一个 `BlockWindow`；实体盒使用 `BlockView`，链接筐使用小盒预览和独立弹窗视图。
6. `DesktopEmbedService` 将窗口挂接到桌面层并维护 z 序。
7. `BackdropService` 应用实时 DWM Acrylic 和深浅主题。
8. 创建系统托盘菜单并启动 Explorer 重挂接看门狗。

命名对象：

- Mutex：`Local\DesktopIconsStorage.SingleInstance`
- 唤醒事件：`Local\DesktopIconsStorage.Wakeup`
- 自启值：`HKCU\Software\Microsoft\Windows\CurrentVersion\Run\DesktopIconsStorage`
- 安装元数据：`HKCU\Software\DesktopIconsStorage`

## 4. 数据与文件语义

实体盒的“收纳”是实际文件移动；链接筐只创建或复制 `.lnk`，原文件路径不变。模式创建后不可切换。

新用户默认位置：

```text
%APPDATA%\DesktopIconsStorage\settings.json
%APPDATA%\DesktopIconsStorage\layout.json
%APPDATA%\DesktopIconsStorage\startup.log
%USERPROFILE%\DesktopIconsStorage\<收纳盒名称>\...
```

旧版兼容目录：

```text
%APPDATA%\DesktopOrganizer
%USERPROFILE%\DesktopBlocks
```

升级时仅复制旧配置文件，不自动移动用户收纳文件。即使 `settings.json` 已使用新的收纳根目录，历史布局的盒目录也可能仍在 `%USERPROFILE%\DesktopBlocks`；现有链接筐允许加载这个已知旧根目录，重命名只在原目录内进行。

自动烟测使用 `scripts/run-test-sandbox.ps1`。脚本设置 `DESKTOPICONSSTORAGE_TEST_ROOT`，把桌面、收纳目录、配置、实例锁和构建产物隔离到 `artifacts/test-sandbox`；自动测试拒绝沙盒外输入，也不写开机自启注册表。手动体验直接运行当前分支的 Debug 程序，使用真实桌面与现有配置。`DESKTOPICONSSTORAGE_CONFIG_DIR` 可用于一般配置重定向，但不代替自动烟测的完整文件隔离。

### 4.1 settings.json

| 字段 | 说明 |
| --- | --- |
| `autoStart` | 是否写入当前用户开机自启。 |
| `themeMode` | `system`、`light` 或 `dark`。 |
| `storageRoot` | 收纳文件根目录。 |
| `backdropOpacity` | 毛玻璃 tint 不透明度。 |
| `blurEnabled` | 是否启用实时 Acrylic。 |
| `closeBehavior` | `ask`、`hide` 或 `exit`。 |
| `showIconNames` | 全局图标名称默认值。 |
| `iconSize` | 32、40 或 48。 |
| `autoFadeEnabled` | 是否自动淡出内容。 |

### 4.2 layout.json

每个收纳盒保存 ID、名称、固定模式（`move`/`link`）、文件夹路径、坐标、宽高、名称显示覆盖以及手动图标顺序。链接筐另保存 2～10 的预览行列规格；旧布局缺少模式时按实体盒读取。坐标和尺寸按物理像素保存。

### 4.3 文件冲突

移动或还原时永不覆盖已有项目。`BlockManager.UniqueDestination` 会生成 `名称 (2)`、`名称 (3)` 等不冲突名称。

## 5. 关键实现

### 5.1 桌面窗口

- `BlockWindow` 创建 `HwndSource` 顶层窗口。
- owner 指向包含 `SHELLDLL_DefView` 的 Progman/WorkerW。
- 几何更新使用 `SWP_NOZORDER`，避免移动或缩放时破坏桌面层级。
- 圆角同时使用 DWM corner preference、窗口区域和 WPF 几何裁剪。

### 5.2 毛玻璃

- 优先使用 `SetWindowCompositionAttribute` 的实时 Acrylic，可调 tint 和透明度。
- Windows 11 官方 backdrop 作为降级路径。
- 禁止通过桌面截图模拟透明，避免移动时背景滞后。
- 原生系统边框被关闭，边框由 WPF 统一绘制。

### 5.3 拖放和排序

- 外部拖放使用 `CF_HDROP`。
- 收纳盒内部拖放附加自定义数据格式，用于区分排序和真实文件移动。
- 文件夹单元格中央区域表示移入子文件夹，操作前必须二次确认。
- 图标顺序保存文件名而不是完整路径，避免收纳盒重命名后失效。
- 链接筐拖放和粘贴走同一入口，只创建或复制 `.lnk`，不能调用真实文件移动；展开窗口不提供移入图标中央的文件夹热区。
- 展开窗口按固定图标格计算分页容量；小盒规格只限制预览数量，列表不启用滚动条。存在链接筐时全局图标大小设置禁用，保证分页容量稳定。
- 分页点由当前窗口内容区的实际尺寸计算；页点和弹窗级左右方向键调用同一页切换入口，单页隐藏分页点。

### 5.4 Shell 通知

文件移动完成后使用正确的 `SHCNF_PATHW` 目录通知刷新 Explorer。禁止把字符串指针与 `SHCNF_IDLIST` 混用，否则可能触发不可捕获的 `AccessViolationException`。

### 5.5 主题与品牌图标

- `ThemeResourceManager` 把统一色板写入应用级动态资源；`App/Styles/Controls.xaml` 定义全局文字、输入框、下拉框、按钮、开关和 WPF 右键菜单样式。新界面应复用这些控件样式，只把页面布局样式留在窗口内部。
- 托盘 WinForms 菜单与 WPF 盒体菜单从 `ThemePalette` 读取相同菜单配色；系统 Shell 文件右键菜单仍由 Windows 提供。
- `Assets/app.ico` 是 EXE、安装器和开始菜单使用的通用主图标。
- `Assets/app-light.ico` 与 `Assets/app-dark.ico` 用于运行时主题切换。
- 托盘图标和 WPF 窗口图标会随应用主题立即更新；Windows Shell 缓存的 EXE 与快捷方式图标不会在运行时切换。
- README 使用 `<picture>` 根据 GitHub 深浅主题切换 `docs/product-icon-light.png` 和 `docs/product-icon-dark.png`。

## 6. 本地构建

### 6.1 Debug

```powershell
dotnet restore DesktopIconsStorage.sln
dotnet build DesktopIconsStorage.sln
.\src\App\bin\Debug\net8.0-windows\DesktopIconsStorage.exe
```

以上直接运行方式使用真实桌面和配置，适合用户亲自验证。编码烟测请运行 `scripts/run-test-sandbox.ps1`，只使用脚本生成的测试文件与应用图标副本。

### 6.2 Release

发布构建只能在功能合并到 `main` 后执行；功能分支的日常验证使用上面的隔离脚本。

```powershell
dotnet build DesktopIconsStorage.sln -c Release
```

项目启用了 `ApplicationHighDpiMode=PerMonitorV2`。构建时可能出现 Windows Forms 关于 manifest DPI 项的提示；实际 DPI 模式由项目属性提供。

## 7. 发布与打包

### 7.1 便携版

```powershell
.\scripts\build-release.ps1 -Version 0.4.0 -SkipInstaller
```

发布参数包括：

- `--self-contained true`
- `PublishSingleFile=true`
- `IncludeNativeLibrariesForSelfExtract=true`
- `EnableCompressionInSingleFile=false`：增大下载文件，但避免压缩程序集启动时解压到私有内存；本项目的 8 盒 A/B 测试中，私有提交减少约 72 MiB。
- 默认不启用 `PublishReadyToRun`：A/B 测试中没有降低常驻内存，发布文件约增大 16 MiB。
- 不生成 PDB

### 7.2 安装器

安装 Inno Setup 6 后执行：

```powershell
.\scripts\build-release.ps1 -Version 0.4.0
```

生成目录：

```text
artifacts/
  publish/win-x64/              dotnet publish 原始输出
  release/                      便携 EXE 和 SHA256SUMS.txt
  installer/                    Setup EXE
```

安装器使用固定 `AppId` 保持升级兼容，支持选择目录、开始菜单入口、可选桌面快捷方式和标准卸载。

### 7.3 GitHub Release

功能合并到 `main` 后，才从已合并的提交创建语义化版本标签。`.github/workflows/release.yml` 和发布脚本都会检查提交是否已进入主分支：

```powershell
git tag -a v0.4.0 -m "DesktopIconsStorage 0.4.0"
git push origin v0.4.0
```

云端会构建安装器、便携版和 SHA256 校验文件，并附加到对应 Release。功能分支可正常推送用于评审，但不创建发布包或 GitHub Release。

## 8. 卸载清理协议

卸载器默认询问是否还原数据：

- 选择 Yes：调用 `DesktopIconsStorage.exe --uninstall-cleanup`。实体盒中的真实文件移回桌面；链接筐中的快捷方式删除，原目标不动；随后删除空目录和配置。
- 选择 No：保留文件和配置，并显示 `storageRoot` 与配置目录。
- 自动清理不会递归删除未知文件或非空目录。
- 任何还原失败都会返回非零退出码，卸载器会保留数据并提示手动位置。

## 9. 测试

链接筐提供独立烟测；发布前仍需执行以下手工检查。

### 9.1 构建检查

```powershell
.\scripts\run-test-sandbox.ps1
$testBin = Join-Path (Get-Location) 'artifacts\test-sandbox\SmokeBin'
dotnet build tests\ShortcutBasketSmoke\ShortcutBasketSmoke.csproj "-p:OutputPath=$testBin"
& (Join-Path $testBin 'ShortcutBasketSmoke.exe')
```

测试脚本把 Debug 构建放到独立目录，即使正式应用正在运行，也不会覆盖它占用的 DLL。功能分支不执行发布脚本；合并到 `main` 后才做 Release 构建与安装器检查。

### 9.2 发布检查

- 便携版可以在关闭开发版后独立启动。
- 文件版本、产品名称和图标正确。
- SHA256 文件包含全部发布 EXE。

### 9.3 安装器端到端检查

1. 安装到隔离目录。
2. 验证主 EXE、卸载器和开始菜单快捷方式。
3. 从安装目录启动应用。
4. 测试保留数据卸载。
5. 测试还原并清理数据卸载。
6. 确认安装目录和开始菜单目录已移除。

### 9.4 手工功能检查

- 创建、移动、缩放和折叠收纳盒。
- 深浅主题、透明度和毛玻璃切换。
- 托盘、设置窗口和确认窗口图标随主题切换。
- 关闭设置窗口时不勾选“记住我的选择”，选择隐藏到托盘后只确认一次。
- 仅有一个收纳盒时，“移入其他收纳盒”选项保持禁用。
- 单项及多项拖入/拖出。
- 图标手动排序及重启持久化。
- 移入子文件夹确认与非法嵌套拦截。
- Explorer 重启后的窗口恢复。
- 多显示器和不同 DPI。
- 链接筐小盒只预览图标，默认 2×2；右键规格与居中弹窗双向动画正常。
- 展开窗口名称开关、深浅主题悬浮提示、快捷方式拖放/粘贴和原文件路径不变。
- 删除链接筐与卸载时只处理 `.lnk`；功能分支不会触发发布流程。

## 10. 临时文件与忽略规则

以下内容不进入仓库：

- `**/bin/`、`**/obj/`
- `artifacts/`、`publish/`、测试结果
- `.vs/`、`.idea/`、`.vscode/`
- 本地日志、设置、布局和收纳文件
- 历史截图及阶段性产品文档

查看完整规则请阅读 [`.gitignore`](../.gitignore)。不要将 `%APPDATA%` 配置或用户收纳目录复制进仓库。

## 11. 日志与故障排查

启动日志：`%APPDATA%\DesktopIconsStorage\startup.log`

应用捕获的错误日志：`%APPDATA%\DesktopIconsStorage\error.log`

原生访问冲突或 CoreCLR 崩溃还需要查看 Windows 事件查看器的“Windows 日志 → 应用程序”。

## 12. 版本与许可证

- 版本号位于 `src/App/DesktopIconsStorage.App.csproj`。
- 发布标签使用 `vMAJOR.MINOR.PATCH`。
- 本项目仅允许个人、学习、教学和非商业研究使用。
- 商业用途、企业生产使用和商业分发需要版权持有人书面授权。

详细条款见 [`LICENSE`](../LICENSE)。
