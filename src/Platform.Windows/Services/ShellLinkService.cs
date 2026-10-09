using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace DesktopIconsStorage.Platform.Services;

/// <summary>用 Windows Shell COM 创建和解析 .lnk；不移动或复制目标文件。</summary>
public static class ShellLinkService
{
    /// <summary>创建只指向目标路径的快捷方式，不设置命令行参数或远程图标。</summary>
    public static void Create(string targetPath, string shortcutPath)
    {
        if (!File.Exists(targetPath) && !Directory.Exists(targetPath))
            throw new IOException($"快捷方式目标不存在：{targetPath}");
        if (File.Exists(shortcutPath) || Directory.Exists(shortcutPath))
            throw new IOException($"快捷方式已存在：{shortcutPath}");
        ShellLinkCom? instance = null;
        try
        {
            instance = new ShellLinkCom();
            var link = (IShellLinkW)instance;
            link.SetPath(Path.GetFullPath(targetPath));
            var workingDirectory = Directory.Exists(targetPath)
                ? targetPath : Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrWhiteSpace(workingDirectory))
                link.SetWorkingDirectory(workingDirectory);
            ((IPersistFile)instance).Save(shortcutPath, true);
        }
        finally
        {
            if (instance != null) Marshal.FinalReleaseComObject(instance);
        }
    }

    /// <summary>读取目标路径；链接跟踪可能更新结果，读取失败则返回 null。</summary>
    public static string? ReadTarget(string shortcutPath)
    {
        if (!File.Exists(shortcutPath)) return null;
        ShellLinkCom? instance = null;
        try
        {
            instance = new ShellLinkCom();
            ((IPersistFile)instance).Load(shortcutPath, 0);
            var buffer = new StringBuilder(32768);
            ((IShellLinkW)instance).GetPath(buffer, buffer.Capacity, IntPtr.Zero, 0);
            return buffer.Length == 0 ? null : buffer.ToString();
        }
        catch { return null; }
        finally
        {
            if (instance != null) Marshal.FinalReleaseComObject(instance);
        }
    }

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLinkCom { }

    // IShellLinkW 必须按系统 vtable 顺序完整声明，否则 SetPath 会调用错误方法。
    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path,
            int capacity, IntPtr findData, uint flags);
        void GetIDList(out IntPtr pidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder description,
            int capacity);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string description);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder directory,
            int capacity);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments,
            int capacity);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int command);
        void SetShowCmd(int command);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath,
            int capacity, out int iconIndex);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath,
            int iconIndex);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        void Resolve(IntPtr hwnd, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    }

    [ComImport, Guid("0000010B-0000-0000-C000-000000000046"),
     InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPersistFile
    {
        void GetClassID(out Guid classId);
        void IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string fileName, uint mode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string fileName,
            [MarshalAs(UnmanagedType.Bool)] bool remember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string fileName);
        void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string fileName);
    }
}
