using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace CleanSpace.Infrastructure.Windows;

internal sealed class WindowsDriveMetadataApi : IWindowsDriveMetadataApi
{
    private const int PathCapacity = 32768;

    public string GetWindowsDirectory()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Windows drive metadata requires Windows.");
        }

        var buffer = new StringBuilder(PathCapacity);
        var length = GetSystemWindowsDirectoryW(buffer, (uint)buffer.Capacity);
        if (length == 0) { throw new Win32Exception(Marshal.GetLastPInvokeError()); }
        if (length >= buffer.Capacity || !Path.IsPathFullyQualified(buffer.ToString()))
        {
            throw new IOException("Windows location is unavailable.");
        }

        return buffer.ToString();
    }

    public string GetVolumeRoot(string windowsDirectory)
    {
        var buffer = new StringBuilder(PathCapacity);
        if (!GetVolumePathNameW(windowsDirectory, buffer, (uint)buffer.Capacity))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        return buffer.ToString();
    }

    public string GetVolumeIdentifier(string volumeRoot)
    {
        var buffer = new StringBuilder(50);
        if (!GetVolumeNameForVolumeMountPointW(volumeRoot, buffer, (uint)buffer.Capacity))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        return buffer.ToString();
    }

    public DriveSpaceBytes GetSpace(string volumeIdentifier)
    {
        var result = GetDiskSpaceInformationW(volumeIdentifier, out var space);
        if (result != 0) { throw new Win32Exception(result & 0xFFFF); }
        return ConvertSpace(space);
    }

    internal static DriveSpaceBytes ConvertSpace(DiskSpaceInformation space)
    {
        if (space.SectorsPerAllocationUnit == 0 || space.BytesPerSector == 0)
        {
            return new(null, null, null);
        }

        var unitBytes = checked((ulong)space.SectorsPerAllocationUnit * space.BytesPerSector);
        return new(ToBytes(space.ActualTotalAllocationUnits, unitBytes),
            ToBytes(space.ActualAvailableAllocationUnits, unitBytes),
            ToBytes(space.CallerAvailableAllocationUnits, unitBytes));
    }

    private static long ToBytes(ulong units, ulong unitBytes) => checked((long)checked(units * unitBytes));

    public string? GetFileSystem(string volumeIdentifier)
    {
        var buffer = new StringBuilder(256);
        return GetVolumeInformationW(volumeIdentifier, IntPtr.Zero, 0, out _, out _, out _,
            buffer, (uint)buffer.Capacity) ? buffer.ToString() : null;
    }

    // https://learn.microsoft.com/windows/win32/api/fileapi/ns-fileapi-disk_space_information
    [StructLayout(LayoutKind.Sequential)]
    internal struct DiskSpaceInformation
    {
        public ulong ActualTotalAllocationUnits;
        public ulong ActualAvailableAllocationUnits;
        public ulong ActualPoolUnavailableAllocationUnits;
        public ulong CallerTotalAllocationUnits;
        public ulong CallerAvailableAllocationUnits;
        public ulong CallerPoolUnavailableAllocationUnits;
        public ulong UsedAllocationUnits;
        public ulong TotalReservedAllocationUnits;
        public ulong VolumeStorageReserveAllocationUnits;
        public ulong AvailableCommittedAllocationUnits;
        public ulong PoolAvailableAllocationUnits;
        public uint SectorsPerAllocationUnit;
        public uint BytesPerSector;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern uint GetSystemWindowsDirectoryW(StringBuilder buffer, uint size);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumePathNameW(string fileName, StringBuilder volumePath, uint length);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeNameForVolumeMountPointW(string mountPoint, StringBuilder volumeName, uint length);

    // Supported since Windows 10 17763, below this app's 19041 minimum. HRESULT, not BOOL.
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int GetDiskSpaceInformationW(string rootPath, out DiskSpaceInformation information);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeInformationW(string rootPath, IntPtr volumeName, uint volumeNameSize,
        out uint serialNumber, out uint maximumComponentLength, out uint flags,
        StringBuilder fileSystemName, uint fileSystemNameSize);
}
