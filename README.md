# 桌面收纳 DesktopOrganizer

Windows 桌面图标收纳工具。通过桌面层上的毛玻璃收纳筐整理文件和快捷方式，支持拖放、缩放、折叠、手动排序和深浅主题。

## 主要功能

- 桌面收纳筐创建、移动、八向缩放和折叠
- 默认 5×3、最小 3×3 的自适应图标网格
- 桌面与收纳筐之间的多选拖放
- 收纳筐内手动排序及顺序持久化
- 移入子文件夹前二次确认
- Windows 11 风格深浅主题与实时 Acrylic 毛玻璃
- 文件系统右键菜单、复制、剪切、粘贴和回收站删除
- 系统托盘、单实例、开机自启动及 Explorer 重启恢复

> 收纳操作会移动真实文件，而不是仅隐藏桌面图标。建议先确认重要文件已有备份。

## 环境要求

- Windows 10/11
- .NET 8 SDK

项目使用 WPF 和原生 Windows API，无外部 NuGet 依赖。

## 构建与运行

```powershell
dotnet build DesktopOrganizer.sln
src\App\bin\Debug\net8.0-windows\DesktopOrganizer.exe
```

Release 构建：

```powershell
dotnet build DesktopOrganizer.sln -c Release
```

## 发布产物

推荐普通用户下载 GitHub Releases 中的安装包：

- `DesktopOrganizer-Setup-<版本>-win-x64.exe`：可选择安装目录，自动创建开始菜单入口，支持可选桌面快捷方式和标准卸载。
- `DesktopOrganizer-<版本>-win-x64.exe`：免安装便携版，不创建快捷方式或卸载信息。

两个版本都内置 .NET 8 运行时，不需要安装 SDK、配置环境变量或安装驱动。安装器默认采用当前用户安装，不要求管理员权限；卸载时保留 `%APPDATA%\DesktopOrganizer` 配置及 `%USERPROFILE%\DesktopBlocks` 中的用户文件。

维护者可在装有 Inno Setup 6 的 Windows 环境执行：

```powershell
.\scripts\build-release.ps1 -Version 0.1.0
```

推送 `v*` 标签后，GitHub Actions 会自动构建安装包和便携版，并将二者附加到对应的 GitHub Release。

## 数据位置

- 设置和布局：`%APPDATA%\DesktopOrganizer`
- 收纳文件：`%USERPROFILE%\DesktopBlocks\<收纳筐名称>`

运行数据不属于源代码仓库，请勿手动删除收纳目录中的重要文件。

## 项目结构

```text
src/Core/               数据模型、文件操作、配置与文件夹监听
src/Platform.Windows/   Win32、Shell、DWM、桌面挂接与系统服务
src/App/                WPF 界面、桌面收纳筐、设置窗口与系统托盘
```
