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

## 3. 启动流程

1. `App.OnStartup` 首先识别维护命令，例如卸载清理。
2. `JsonStore.MigrateLegacyFiles` 将旧版 `DesktopOrganizer` 设置复制到新配置目录。
3. `AppHost.Run` 获取单实例互斥锁并加载设置和布局。
4. `BlockManager` 创建或加载收纳盒模型。
5. 每个模型对应一个 `BlockWindow` 和 `BlockView`。
6. `DesktopEmbedService` 将窗口挂接到桌面层并维护 z 序。
7. `BackdropService` 应用实时 DWM Acrylic 和深浅主题。
8. 创建系统托盘菜单并启动 Explorer 重挂接看门狗。

命名对象：

- Mutex：`Local\DesktopIconsStorage.SingleInstance`
- 唤醒事件：`Local\DesktopIconsStorage.Wakeup`
- 自启值：`HKCU\Software\Microsoft\Windows\CurrentVersion\Run\DesktopIconsStorage`
- 安装元数据：`HKCU\Software\DesktopIconsStorage`

## 4. 数据与文件语义

DesktopIconsStorage 的“收纳”是实际文件移动，不是数据库索引或视觉隐藏。

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

升级时仅复制旧配置文件。已有 `settings.json` 中的 `storageRoot` 保持原值，因此不会自动移动用户收纳文件。

开发和自动化测试可以为单个进程设置可选环境变量 `DESKTOPICONSSTORAGE_CONFIG_DIR`，把配置重定向到隔离目录。正式运行不需要也不应设置该变量。

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

每个收纳盒保存 ID、名称、文件夹路径、坐标、宽高、折叠状态、名称显示覆盖以及手动图标顺序。坐标和尺寸按物理像素保存。

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

### 5.4 Shell 通知

文件移动完成后使用正确的 `SHCNF_PATHW` 目录通知刷新 Explorer。禁止把字符串指针与 `SHCNF_IDLIST` 混用，否则可能触发不可捕获的 `AccessViolationException`。

### 5.5 主题与品牌图标

- `ThemeResourceManager` 把统一色板写入应用级动态资源，设置窗口、删除窗口和关闭确认窗口共同使用。
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

### 6.2 Release

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

推送语义化版本标签会触发 `.github/workflows/release.yml`：

```powershell
git tag -a v0.4.0 -m "DesktopIconsStorage 0.4.0"
git push origin v0.4.0
```

云端会构建安装器、便携版和 SHA256 校验文件，并附加到对应 Release。

## 8. 卸载清理协议

卸载器默认询问是否还原数据：

- 选择 Yes：调用 `DesktopIconsStorage.exe --uninstall-cleanup`，由应用读取真实配置，将所有可见项目安全移回桌面，然后删除空目录和配置。
- 选择 No：保留文件和配置，并显示 `storageRoot` 与配置目录。
- 自动清理不会递归删除未知文件或非空目录。
- 任何还原失败都会返回非零退出码，卸载器会保留数据并提示手动位置。

## 9. 测试

当前没有独立自动化测试项目，发布前至少执行以下检查。

### 9.1 构建检查

```powershell
dotnet build DesktopIconsStorage.sln
dotnet build DesktopIconsStorage.sln -c Release
```

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
