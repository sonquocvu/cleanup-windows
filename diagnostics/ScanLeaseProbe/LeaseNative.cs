using System.Runtime.InteropServices;
using CleanSpace.Application.Abstractions;
using Microsoft.Win32.SafeHandles;

namespace ScanLeaseProbe;

internal static class LeaseNative
{
    // Identical metadata-only relative directory request to production; ShareAccess is READ | WRITE.
    internal static SafeFileHandle Open(SafeFileHandle parent, string name)
    {
        if (!new ScanEntry(name, 1, 0, 0, 0).HasSafeName) { throw new IOException("Unsafe segment."); }
        var text = Marshal.StringToHGlobalUni(name);
        var unicode = new UnicodeString { Length = checked((ushort)(name.Length * 2)),
            MaximumLength = checked((ushort)(name.Length * 2 + 2)), Buffer = text };
        var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<UnicodeString>());
        var referenced = false;
        try
        {
            parent.DangerousAddRef(ref referenced);
            Marshal.StructureToPtr(unicode, pointer, false);
            var attributes = new ObjectAttributes { Length = Marshal.SizeOf<ObjectAttributes>(),
                Root = parent.DangerousGetHandle(), Name = pointer, Attributes = 0x40 };
            var status = NtCreateFile(out var handle, 0x100081, in attributes,
                out _, IntPtr.Zero, 0, 3, 1, 0x00200021, IntPtr.Zero, 0);
            if (status == 0 && !handle.IsInvalid) { return handle; }
            handle?.Dispose();
            throw new IOException($"Lease open failed: 0x{status:X8}.");
        }
        finally
        {
            if (referenced) { parent.DangerousRelease(); }
            Marshal.FreeHGlobal(pointer);
            Marshal.FreeHGlobal(text);
        }
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct UnicodeString { public ushort Length, MaximumLength; public IntPtr Buffer; }
    [StructLayout(LayoutKind.Sequential)]
    private struct ObjectAttributes
    {
        public int Length; public IntPtr Root, Name; public uint Attributes;
        public IntPtr Security, Quality;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct IoStatus { public IntPtr Status, Information; }
    [DllImport("ntdll.dll", ExactSpelling = true)]
    private static extern int NtCreateFile(out SafeFileHandle handle, uint access, in ObjectAttributes attributes,
        out IoStatus status, IntPtr allocation, uint fileAttributes, uint share, uint disposition,
        uint options, IntPtr ea, uint eaLength);
}
