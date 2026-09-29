using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Makosh.Core;

static class NativeWinPath
{
    const uint FileShareRead = 0x00000001;
    const uint FileShareWrite = 0x00000002;
    const uint FileShareDelete = 0x00000004;
    const uint OpenExisting = 3;
    const uint FileFlagBackupSemantics = 0x02000000;
    const uint FileNameNormalized = 0;

    public static string? TryFinalPath(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        using var handle = CreateFile(
            path,
            0,
            FileShareRead | FileShareWrite | FileShareDelete,
            IntPtr.Zero,
            OpenExisting,
            FileFlagBackupSemantics,
            IntPtr.Zero);
        if (handle.IsInvalid)
        {
            return null;
        }

        var buffer = new StringBuilder(1024);
        var length = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Capacity, FileNameNormalized);
        if (length == 0)
        {
            return null;
        }

        var result = buffer.ToString();
        if (result.StartsWith(@"\\?\", StringComparison.Ordinal))
        {
            result = result[4..];
        }

        if (result.StartsWith(@"UNC\", StringComparison.Ordinal))
        {
            result = @"\\" + result[4..];
        }

        return Path.GetFullPath(result);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern SafeFileHandle CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern uint GetFinalPathNameByHandle(
        SafeFileHandle hFile,
        StringBuilder lpszFilePath,
        uint cchFilePath,
        uint dwFlags);
}
