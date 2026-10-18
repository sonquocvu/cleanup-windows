using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using CleanSpace.Application.Abstractions;
using Microsoft.Win32.SafeHandles;

namespace CleanSpace.Infrastructure.Scanning;

// Only metadata access; FILE_OPEN (never CREATE), no data-read or write access for files.
internal static class WindowsScanNative
{
    internal static string KnownFolder(Guid id, bool defaultPath)
    {
        var result = SHGetKnownFolderPath(in id, defaultPath ? 0x400u : 0u, IntPtr.Zero, out var pointer);
        try
        {
            Marshal.ThrowExceptionForHR(result);
            return Marshal.PtrToStringUni(pointer) ?? throw new IOException("Known folder unavailable.");
        }
        finally { Marshal.FreeCoTaskMem(pointer); }
    }

    internal static SafeFileHandle OpenVolume(string volume)
    {
        var handle = CreateFileW(volume, 0x80, 1, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
        if (!handle.IsInvalid) { return handle; }
        handle.Dispose();
        throw new Win32Exception(Marshal.GetLastPInvokeError());
    }

    internal static SafeFileHandle OpenChild(SafeFileHandle parent, string name, bool directory)
    {
        if (!new ScanEntry(name, 1, 0, 0, 0).HasSafeName) { throw new IOException("Unsafe segment."); }
        var text = Marshal.StringToHGlobalUni(name);
        var unicode = new UnicodeString { Length = checked((ushort)(name.Length * 2)),
            MaximumLength = checked((ushort)(name.Length * 2 + 2)), Buffer = text };
        var unicodePointer = Marshal.AllocHGlobal(Marshal.SizeOf<UnicodeString>());
        var referenced = false;
        try
        {
            parent.DangerousAddRef(ref referenced);
            Marshal.StructureToPtr(unicode, unicodePointer, false);
            var attributes = new ObjectAttributes { Length = Marshal.SizeOf<ObjectAttributes>(),
                RootDirectory = parent.DangerousGetHandle(), ObjectName = unicodePointer, Attributes = 0x40 };
            // A single component relative to a verified handle + FILE_OPEN_REPARSE_POINT.
            // Directory handles deny write/delete sharing. Attribute-only file opens do not lock content.
            // FILE_DIRECTORY_FILE cannot be combined with FILE_OPEN_NO_RECALL on this Windows API.
            // Reparse directory handles are opened as the reparse object and rejected before enumeration.
            var status = NtCreateFile(out var handle, directory ? 0x100081u : 0x100080u, in attributes,
                out _, IntPtr.Zero, 0, 1, 1, 0x00200000u | 0x20u | (directory ? 1u : 0x00400040u), IntPtr.Zero, 0);
            if (status == 0 && !handle.IsInvalid) { return handle; }
            handle?.Dispose();
            throw new IOException($"Metadata handle unavailable (NTSTATUS 0x{status:X8}).");
        }
        finally
        {
            if (referenced) { parent.DangerousRelease(); }
            Marshal.FreeHGlobal(unicodePointer);
            Marshal.FreeHGlobal(text);
        }
    }

    internal static ScanEntry Read(SafeFileHandle handle, string name)
    {
        if (!GetFileInformationByHandle(handle, out var info))
        { throw new Win32Exception(Marshal.GetLastPInvokeError()); }
        return new(name, ((ulong)info.IndexHigh << 32) | info.IndexLow,
            checked((long)(((ulong)info.SizeHigh << 32) | info.SizeLow)),
            ((long)info.WriteHigh << 32) | info.WriteLow, info.Attributes);
    }

    internal static string FinalPath(SafeFileHandle handle)
    {
        var text = new StringBuilder(32768);
        var length = GetFinalPathNameByHandleW(handle, text, (uint)text.Capacity, 1);
        if (length == 0 || length >= text.Capacity) { throw new IOException("Identity unavailable."); }
        return text.ToString().TrimEnd('\\');
    }

    internal static bool ReadDirectory(SafeFileHandle handle, byte[] buffer, bool restart)
    {
        if (GetFileInformationByHandleEx(handle, restart ? 11 : 10, buffer, (uint)buffer.Length)) { return true; }
        var error = Marshal.GetLastPInvokeError();
        if (error == 18) { return false; } // ERROR_NO_MORE_FILES, not access denied or provider failure.
        throw new Win32Exception(error);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct UnicodeString { public ushort Length, MaximumLength; public IntPtr Buffer; }
    [StructLayout(LayoutKind.Sequential)]
    private struct ObjectAttributes
    {
        public int Length; public IntPtr RootDirectory, ObjectName; public uint Attributes;
        public IntPtr SecurityDescriptor, SecurityQualityOfService;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct IoStatus { public IntPtr Status, Information; }
    [StructLayout(LayoutKind.Sequential)]
    private struct HandleInfo
    {
        public uint Attributes, CreationLow, CreationHigh, AccessLow, AccessHigh, WriteLow, WriteHigh;
        public uint VolumeSerial, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }

    [DllImport("shell32.dll", ExactSpelling = true)]
    private static extern int SHGetKnownFolderPath(in Guid id, uint flags, IntPtr token, out IntPtr path);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string name, uint access, uint share,
        IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("ntdll.dll", ExactSpelling = true)]
    private static extern int NtCreateFile(out SafeFileHandle handle, uint access, in ObjectAttributes attributes,
        out IoStatus status, IntPtr allocation, uint fileAttributes, uint share, uint disposition,
        uint options, IntPtr ea, uint eaLength);
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out HandleInfo info);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandleW(SafeFileHandle handle, StringBuilder path, uint capacity, uint flags);
    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle handle, int infoClass,
        [Out] byte[] buffer, uint length);
}
