using System.Runtime.InteropServices;
using System.Text;

namespace SdqTerminal;

// Read-only fallback for native Windows shells. PowerShell locations are reported
// through OSC instead because Set-Location does not update the native process CWD.
internal static class ProcessDirectory
{
    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(IntPtr process, int informationClass,
        [Out] IntPtr[] information, int size, out int returned);
    [DllImport("kernel32.dll")]
    private static extern bool ReadProcessMemory(IntPtr process, IntPtr address,
        [Out] byte[] buffer, nuint size, out nuint read);

    public static string? Read(IntPtr process)
    {
        if (process == IntPtr.Zero) return null;
        var info = new IntPtr[6];
        if (NtQueryInformationProcess(process, 0, info, info.Length * IntPtr.Size, out _) != 0) return null;
        byte[]? Bytes(IntPtr address, int length)
        {
            var bytes = new byte[length];
            return ReadProcessMemory(process, address, bytes, (nuint)length, out var read) && read == (nuint)length ? bytes : null;
        }
        IntPtr Pointer(byte[] bytes, int offset = 0) => IntPtr.Size == 8
            ? new IntPtr(BitConverter.ToInt64(bytes, offset)) : new IntPtr(BitConverter.ToInt32(bytes, offset));
        var parameters = Bytes(info[1] + (IntPtr.Size == 8 ? 0x20 : 0x10), IntPtr.Size);
        if (parameters is null) return null;
        var directory = Bytes(Pointer(parameters) + (IntPtr.Size == 8 ? 0x38 : 0x24), IntPtr.Size * 2);
        if (directory is null) return null;
        var length = BitConverter.ToUInt16(directory);
        if (length == 0 || length > 32766 || length % 2 != 0) return null;
        var text = Bytes(Pointer(directory, IntPtr.Size), length);
        return text is null ? null : Encoding.Unicode.GetString(text);
    }
}
