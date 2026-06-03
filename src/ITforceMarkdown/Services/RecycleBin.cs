using System;
using System.IO;
using System.Runtime.InteropServices;

namespace ITforceMarkdown.Services;

/// <summary>
/// 把文件送 Windows 回收站 — 用 Shell32 的 SHFileOperation。
///
/// ⚠️ x64 对齐的坑:
/// 早期版本用了 [StructLayout(... Pack = 1)] 抄自老 32 位例子,
/// 在 64 位 Windows 上把 pFrom/pTo 这种 8 字节指针挤到非 8 对齐位置,
/// SHFileOperation 解引用拿到错误地址 → AccessViolation 直接崩溃 (用户报
/// "点删除程序崩了")。
///
/// 修复: 去掉 Pack 设置, 让 .NET 用平台默认对齐 (x64 是 8 字节)。
/// 同时一律用 Unicode 入口 SHFileOperationW, 不依赖 CharSet.Auto 路由。
/// </summary>
internal static class RecycleBin
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint   wFunc;
        [MarshalAs(UnmanagedType.LPWStr)] public string  pFrom;
        [MarshalAs(UnmanagedType.LPWStr)] public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszProgressTitle;
    }

    private const uint   FO_DELETE         = 0x0003;
    private const ushort FOF_ALLOWUNDO     = 0x0040;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_SILENT        = 0x0004;
    private const ushort FOF_NOERRORUI     = 0x0400;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHFileOperationW")]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT FileOp);

    /// <summary>
    /// 把指定路径的文件 / 目录送回收站。失败返回 false, 不抛异常。
    /// </summary>
    public static bool Send(string path)
    {
        if (string.IsNullOrEmpty(path)) return false;
        if (!File.Exists(path) && !Directory.Exists(path)) return false;

        try
        {
            // pFrom 是 double-null-terminated 字符串列表, 支持批量删除。
            // 单文件场景: "C:\foo.txt\0\0"
            var op = new SHFILEOPSTRUCT
            {
                hwnd  = IntPtr.Zero,
                wFunc = FO_DELETE,
                pFrom = path + '\0' + '\0',
                pTo   = null,
                fFlags = (ushort)(FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI),
                fAnyOperationsAborted = false,
                hNameMappings = IntPtr.Zero,
                lpszProgressTitle = null,
            };
            var rc = SHFileOperation(ref op);
            return rc == 0 && !op.fAnyOperationsAborted;
        }
        catch
        {
            // SEHException / AccessViolation 不一定能被 catch 住, 但 InvalidOp /
            // marshal 错误这里能兜住。崩溃保护层是 App.xaml.cs 的 AppDomain 处理。
            return false;
        }
    }
}
