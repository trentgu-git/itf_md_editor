using System;
using System.IO;
using System.Runtime.InteropServices;

namespace ITforceMarkdown.Services;

/// <summary>
/// 把文件送 Windows 回收站 — 用 Shell32 的 SHFileOperation。
///
/// 不用 Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile 是因为它依赖
/// Microsoft.VisualBasic.dll, 在某些 .NET 8 SDK 配置下要额外引用。
/// SHFileOperation 是 Win32 内置 API, 零依赖。
/// </summary>
internal static class RecycleBin
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto, Pack = 1)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        [MarshalAs(UnmanagedType.LPWStr)] public string pFrom;
        [MarshalAs(UnmanagedType.LPWStr)] public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszProgressTitle;
    }

    private const uint FO_DELETE = 0x0003;
    private const ushort FOF_ALLOWUNDO     = 0x0040;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_SILENT        = 0x0004;
    private const ushort FOF_NOERRORUI     = 0x0400;

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT FileOp);

    public static bool Send(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) return false;

        // SHFileOperation 用 double-null-terminated 字符串列表表示批量
        var op = new SHFILEOPSTRUCT
        {
            wFunc = FO_DELETE,
            pFrom = path + '\0' + '\0',
            fFlags = (ushort)(FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI),
        };
        var rc = SHFileOperation(ref op);
        return rc == 0 && !op.fAnyOperationsAborted;
    }
}
