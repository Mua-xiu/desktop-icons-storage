<div align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="docs/product-icon-dark.png">
    <img src="docs/product-icon-light.png" width="180" alt="DesktopIconsStorage product icon">
  </picture>
  <h1>DesktopIconsStorage</h1>
  <p><strong>让桌面图标收纳清晰、自然，并保持 Windows 原生体验。</strong></p>
  <p>
    <a href="https://www.microsoft.com/windows/"><img src="https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4?logo=windows" alt="Windows 10 and 11"></a>
    <a href="https://www.microsoft.com/windows/"><img src="https://img.shields.io/badge/Windows-10%20%7C%2011-0078D4?logo=windows" alt="Windows 10 and 11"></a>
    <a href="https://dotnet.microsoft.com/"><img src="https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet" alt=".NET 8"></a>
    <a href="https://github.com/Mua-xiu/desktop-icons-storage/releases"><img src="https://img.shields.io/github/v/release/Mua-xiu/desktop-icons-storage" alt="Latest release"></a>
    <a href="https://github.com/Mua-xiu/desktop-icons-storage/actions/workflows/release.yml"><img src="https://github.com/Mua-xiu/desktop-icons-storage/actions/workflows/release.yml/badge.svg" alt="Build status"></a>
    <a href="LICENSE"><img src="https://img.shields.io/badge/license-non--commercial-orange" alt="Non-commercial license"></a>
  </p>
</div>

DesktopIconsStorage 是一款原生 Windows 桌面图标收纳工具。它通过桌面层上的实时毛玻璃收纳盒整理文件、文件夹和快捷方式，同时保留 Windows 桌面的使用习惯。

> [!IMPORTANT]
> 收纳操作会移动真实文件，而不是创建一份仅用于显示的副本。使用前请确保重要文件已有备份。

## 功能特性

- 创建多个独立的桌面收纳盒，每个收纳盒对应一个真实文件夹
- 收纳盒移动、八向缩放、双击折叠与展开
- 默认 5×3、最小 3×3 的 DPI 自适应图标网格
- 文件和文件夹多选、框选、拖入、拖出、复制、剪切和粘贴
- 收纳盒内手动排序，并持久化每个收纳盒的独立顺序
- 将项目移入子文件夹前进行二次确认
- Windows 11 风格深浅主题和实时 DWM Acrylic 毛玻璃
- Windows Shell 右键菜单、回收站删除及系统图标提取
- 系统托盘、单实例、开机自启动和 Explorer 重启恢复
- 卸载时可选择将所有项目安全移回桌面，或保留现有收纳数据

## 下载

请从 [GitHub Releases](https://github.com/Mua-xiu/desktop-icons-storage/releases) 下载正式版本。

| 产物                                              | 适用场景                                                               |
| ------------------------------------------------- | ---------------------------------------------------------------------- |
| `DesktopIconsStorage-Setup-<version>-win-x64.exe` | 推荐。支持选择安装目录、开始菜单快捷方式、可选桌面快捷方式和标准卸载。 |
| `DesktopIconsStorage-<version>-win-x64.exe`       | 便携版。直接运行，不创建快捷方式和卸载信息。                           |
| `SHA256SUMS.txt`                                  | 用于校验下载文件完整性。                                               |

两种程序都内置 .NET 8 运行时，不需要安装 SDK、配置环境变量或安装驱动。发布版不压缩单文件程序集，以换取更低的运行时私有内存占用；下载文件会相应更大。

## 安装与卸载

安装器默认安装到当前用户目录，因此通常不需要管理员权限。安装过程中可以修改目标目录，并可选择是否创建桌面快捷方式；开始菜单入口会自动创建。

卸载时可以选择：

1. 将所有收纳项目移回桌面，并清理空的收纳目录和配置；
2. 保留收纳文件及设置。卸载器会显示保留数据的实际位置，便于以后找回。

自动还原遇到同名文件时会创建不冲突的新名称，不会覆盖桌面上的现有文件。未知文件或非空目录不会被递归删除。

## 基本使用

1. 启动后在系统托盘菜单中创建收纳盒。
2. 将桌面文件、文件夹或快捷方式拖入收纳盒。
3. 拖动标题栏移动收纳盒，拖动四边或四角调整尺寸。
4. 在收纳盒内部拖动图标调整顺序；拖到文件夹中央时会请求确认后移入该文件夹。
5. 双击标题栏折叠或展开；右键收纳盒可重命名、打开目录或删除收纳盒。

## 数据位置

新安装默认使用：

- 配置和布局：`%APPDATA%\DesktopIconsStorage`
- 收纳文件：`%USERPROFILE%\DesktopIconsStorage\<收纳盒名称>`

从 0.1.x 升级时会自动读取旧的 `%APPDATA%\DesktopOrganizer` 配置；旧设置中已经指定的收纳目录保持不变，不会擅自移动用户文件。

## 从源码构建

要求 Windows 10/11 和 .NET 8 SDK。

```powershell
git clone https://github.com/Mua-xiu/desktop-icons-storage.git
cd desktop-icons-storage
dotnet build DesktopIconsStorage.sln
```

运行 Debug 构建：

```powershell
.\src\App\bin\Debug\net8.0-windows\DesktopIconsStorage.exe
```

生成自包含便携版和安装器：

```powershell
.\scripts\build-release.ps1 -Version 0.4.0
```

安装器构建需要 Inno Setup 6；使用 `-SkipInstaller` 可以只生成便携版。推送 `v*` 标签时，GitHub Actions 会自动生成 Release 产物。

## 开发文档

架构、目录结构、运行流程、配置格式、构建、打包、测试、临时文件和发布流程请参阅：

- [开发文档](docs/DEVELOPMENT.md)

## 反馈问题

提交问题前请先确认使用的是最新版本，并说明 Windows 版本、复现步骤及相关日志。问题反馈入口：[GitHub Issues](https://github.com/Mua-xiu/desktop-icons-storage/issues)。

## 许可证

本项目源码可供个人使用和学习参考，**禁止商业用途**。它不是 OSI 定义的开源许可证项目。详细条款请阅读 [LICENSE](LICENSE)。
