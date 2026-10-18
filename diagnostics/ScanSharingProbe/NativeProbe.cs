using System.ComponentModel;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using CleanSpace.Application.Abstractions;
using CleanSpace.Infrastructure.Scanning;
using Microsoft.Win32.SafeHandles;

namespace ScanSharingProbe;

// Diagnostic executable only. Original opens call the compiled production implementation.
// Candidate opens differ in ShareAccess only and cannot be selected in the production app.
internal static class NativeProbe
{
    private static readonly Type Native = typeof(WindowsTempScopeProvider).Assembly.GetType(
        "CleanSpace.Infrastructure.Scanning.WindowsScanNative", true)!;
    private static readonly Type DirectoryType = typeof(WindowsTempScopeProvider).Assembly.GetType(
        "CleanSpace.Infrastructure.Scanning.WindowsScanDirectory", true)!;

    internal static T Production<T>(string name, params object[] arguments)
    {
        try { return (T)Native.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, arguments)!; }
        catch (TargetInvocationException error) when (error.InnerException is not null)
        { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
    }

    internal static IScanDirectory Bind(SafeFileHandle handle, string name, IScanDirectory? parent)
    {
        try
        {
            return (IScanDirectory)Activator.CreateInstance(DirectoryType, BindingFlags.NonPublic | BindingFlags.Instance,
                null, new object?[] { handle, Production<string>("FinalPath", handle), name, parent }, null)!;
        }
        catch (TargetInvocationException error) when (error.InnerException is not null)
        { ExceptionDispatchInfo.Capture(error.InnerException).Throw(); throw; }
    }

    internal static SafeFileHandle CandidateRoot(string path) => Open(path, 0x80, 7);

    internal static SafeFileHandle CandidateChild(SafeFileHandle parent, string name, bool directory)
    {
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
            var status = NtCreateFile(out var handle, directory ? 0x100081u : 0x100080u, in attributes,
                out _, IntPtr.Zero, 0, 7, 1, 0x00200000u | 0x20u | (directory ? 1u : 0x00400040u), IntPtr.Zero, 0);
            if (status == 0 && !handle.IsInvalid) { return handle; }
            handle?.Dispose();
            throw new IOException($"Candidate open failed: 0x{status:X8}.");
        }
        finally
        {
            if (referenced) { parent.DangerousRelease(); }
            Marshal.FreeHGlobal(pointer);
            Marshal.FreeHGlobal(text);
        }
    }

    internal static SafeFileHandle Open(string path, uint access, uint share)
    {
        var handle = CreateFileW(path, access, share, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
        var error = Marshal.GetLastPInvokeError();
        if (!handle.IsInvalid) { return handle; }
        handle.Dispose();
        throw new Win32Exception(error);
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
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security,
        uint disposition, uint flags, IntPtr template);
}
