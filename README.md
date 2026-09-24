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
> 创建时可选择实体收纳盒或快捷方式收纳筐。实体盒会移动真实文件；链接筐只创建或复制快捷方式，原项目保留在原位置。重要文件仍建议提前备份。

## 功能特性

- 创建多个独立的桌面收纳盒，每个收纳盒对应一个真实文件夹
- 创建时固定选择实体盒或快捷方式收纳筐；链接筐以紧凑预览小盒和居中内容窗口呈现
- 链接筐小盒默认 2×2，只显示图标；展开窗口可单独切换名称显示，关闭后悬停查看名称
- 链接筐展开窗口按可用容量分页，可点击页点或使用键盘左右键切页
- 链接筐展开窗口空间不足时自动分页，可点页点或用键盘左右键切页；单页不显示页点
- 实体盒移动、八向缩放、双击折叠与展开；链接筐通过右键弹窗设置 2～10 行、2～10 列预览
- 默认 5×3、最窄三列且最矮一行的 DPI 自适应图标网格
- 文件和文件夹多选、框选、拖入、拖出、复制、剪切和粘贴
- 收纳盒内手动排序，并持久化每个收纳盒的独立顺序
- 将项目移入子文件夹前进行二次确认
- Windows 11 风格深浅主题和实时 DWM Acrylic 毛玻璃
- Windows Shell 右键菜单、回收站删除及系统图标提取
- 系统托盘、单实例、开机自启动和 Explorer 重启恢复
- 卸载时可选择还原实体文件并清理链接筐快捷方式，或保留现有收纳数据

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

1. 将实体盒的真实文件移回桌面，删除链接筐及其中的快捷方式，并清理空目录和配置；
2. 保留收纳文件及设置。卸载器会显示保留数据的实际位置，便于以后找回。

自动还原遇到同名文件时会创建不冲突的新名称，不会覆盖桌面上的现有文件。未知文件或非空目录不会被递归删除。

## 基本使用

1. 启动后在系统托盘菜单中创建收纳盒。
2. 创建时选择模式；拖入实体盒会移动文件，拖入链接筐只添加快捷方式。
3. 实体盒可拖动标题栏并从四边或四角调整尺寸；链接筐右键“设置预览规格…”可分别选择 2～10 行和列。
4. 点击链接筐小盒，在当前显示器中心打开内容窗口；小盒图标只用于预览。
5. 展开窗口右上角可切换名称显示；关闭名称后悬停图标可查看完整名称。

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

编码验证请运行隔离脚本。它只准备测试文件和仓库自带应用图标副本，不读取真实桌面图标文件：

```powershell
.\scripts\run-test-sandbox.ps1
```

需要亲自用桌面文件体验时，直接运行当前分支的 Debug 程序：

```powershell
dotnet build src\App\DesktopIconsStorage.App.csproj -c Debug
.\src\App\bin\Debug\net8.0-windows\DesktopIconsStorage.exe
```

这种运行方式使用当前用户的桌面、配置和收纳目录；请先用自己新建的测试文件验证实体盒的真实移动。

生成自包含便携版和安装器：

```powershell
.\scripts\build-release.ps1 -Version 0.4.0
```

安装器构建需要 Inno Setup 6；使用 `-SkipInstaller` 可以只生成便携版。仅在功能合并到 `main` 后，才允许从 `main` 或已合并提交的 `v*` 标签构建发布包；功能分支的提交和推送不会发布新版本。

## 开发文档

架构、目录结构、运行流程、配置格式、构建、打包、测试、临时文件和发布流程请参阅：

- [开发文档](docs/DEVELOPMENT.md)

## 反馈问题

提交问题前请先确认使用的是最新版本，并说明 Windows 版本、复现步骤及相关日志。问题反馈入口：[GitHub Issues](https://github.com/Mua-xiu/desktop-icons-storage/issues)。

## 许可证

本项目源码可供个人使用和学习参考，**禁止商业用途**。它不是 OSI 定义的开源许可证项目。详细条款请阅读 [LICENSE](LICENSE)。
