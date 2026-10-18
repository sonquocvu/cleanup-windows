using System.ComponentModel;
using System.Runtime.InteropServices;
using CleanSpace.Application.UseCases;
using CleanSpace.Infrastructure.Windows;
using Xunit;

namespace CleanSpace.IntegrationTests;

public sealed class WindowsDriveInfoProviderTests
{
    [Fact]
    public async Task ResolvesWindowsLocationInsteadOfAssumingDriveLetterAndOnlyCallsMetadataPort()
    {
        var api = new StrictMetadataApi();
        var result = await new WindowsDriveInfoProvider(api).GetSystemDriveAsync(default);
        Assert.Equal(@"Q:\", result.VolumeRoot);
        Assert.Equal(1000, result.TotalBytes);
        Assert.Equal(250, result.TotalFreeBytes);
        Assert.Equal(100, result.AvailableToUserBytes);
        Assert.Equal(new[] { "windows", "root", "id", "space", "filesystem", "root", "id" }, api.Calls);
    }

    [Fact]
    public async Task MountedFolderIdentityDoesNotDisclosePrivatePath()
    {
        var api = new StrictMetadataApi { Root = @"Q:\private-path\" };
        var result = await new WindowsDriveInfoProvider(api).GetSystemDriveAsync(default);
        Assert.Equal(StrictMetadataApi.VolumeId, result.VolumeRoot);
        Assert.DoesNotContain("private-path", result.VolumeRoot);
    }

    [Fact]
    public async Task ChangedVolumeFailsClosed()
    {
        var api = new StrictMetadataApi { ChangeIdentity = true };
        await Assert.ThrowsAsync<IOException>(() => new WindowsDriveInfoProvider(api).GetSystemDriveAsync(default));
    }

    [Fact]
    public async Task PreCancellationDoesNotCallNativeBoundary()
    {
        var api = new StrictMetadataApi();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new WindowsDriveInfoProvider(api).GetSystemDriveAsync(new CancellationToken(true)));
        Assert.Empty(api.Calls);
    }

    [Fact]
    public async Task UnavailableFilesystemIsOptionalButSpaceFailureIsAnError()
    {
        var api = new StrictMetadataApi { FileSystem = null };
        Assert.Null((await new WindowsDriveInfoProvider(api).GetSystemDriveAsync(default)).FileSystem);
        var failed = new StrictMetadataApi { FailSpace = true };
        var result = await new DriveOverviewUseCase(new WindowsDriveInfoProvider(failed)).ExecuteAsync();
        Assert.Null(result.Overview);
        Assert.NotNull(result.Error);
        Assert.Equal(new[] { "windows", "root", "id", "space" }, failed.Calls);
    }

    [Fact]
    public void NativeSpaceConversionUsesActualTotalRatherThanQuotaTotal()
    {
        var native = new WindowsDriveMetadataApi.DiskSpaceInformation
        {
            ActualTotalAllocationUnits = 1000, ActualAvailableAllocationUnits = 250,
            CallerTotalAllocationUnits = 100, CallerAvailableAllocationUnits = 50,
            SectorsPerAllocationUnit = 8, BytesPerSector = 512
        };
        var bytes = WindowsDriveMetadataApi.ConvertSpace(native);
        Assert.Equal(4096000, bytes.Total);
        Assert.Equal(1024000, bytes.Free);
        Assert.Equal(204800, bytes.AvailableToUser);
        Assert.Equal(96, Marshal.SizeOf<WindowsDriveMetadataApi.DiskSpaceInformation>());
    }

    [Fact]
    public void MissingAllocationGeometryReturnsUnknownAndOverflowIsRejected()
    {
        Assert.Equal(new DriveSpaceBytes(null, null, null), WindowsDriveMetadataApi.ConvertSpace(default));
        Assert.Throws<OverflowException>(() => WindowsDriveMetadataApi.ConvertSpace(new()
        {
            ActualTotalAllocationUnits = ulong.MaxValue, SectorsPerAllocationUnit = 8, BytesPerSector = 512
        }));
    }

    [Fact]
    public async Task InstalledWindowsVolumeCanBeReadAsStandardUser()
    {
        Assert.True(OperatingSystem.IsWindows(), "This integration suite requires the actual Windows toolchain.");
        var result = await new DriveOverviewUseCase(new WindowsDriveInfoProvider()).ExecuteAsync();
        Assert.Null(result.Error);
        var overview = Assert.IsType<CleanSpace.Core.Models.DriveOverview>(result.Overview);
        Assert.True(overview.TotalBytes > 0);
        Assert.True(overview.TotalFreeBytes >= 0);
        Assert.True(overview.AvailableToUserBytes >= 0);
        Assert.InRange(overview.PercentageUsed!.Value, 0, 100);
    }

    private sealed class StrictMetadataApi : IWindowsDriveMetadataApi
    {
        public const string VolumeId = @"\\?\Volume{12345678-1234-1234-1234-123456789abc}\";
        public List<string> Calls { get; } = [];
        public string Root { get; init; } = @"Q:\";
        public string? FileSystem { get; init; } = "NTFS";
        public bool ChangeIdentity { get; init; }
        public bool FailSpace { get; init; }
        public string GetWindowsDirectory() { Calls.Add("windows"); return @"Q:\InstalledWindows"; }
        public string GetVolumeRoot(string windowsDirectory)
        {
            Assert.Equal(@"Q:\InstalledWindows", windowsDirectory);
            Calls.Add("root"); return Root;
        }
        public string GetVolumeIdentifier(string root)
        {
            Assert.Equal(Root, root);
            Calls.Add("id");
            return ChangeIdentity && Calls.Count > 3 ? @"\\?\Volume{87654321-1234-1234-1234-123456789abc}\" : VolumeId;
        }
        public DriveSpaceBytes GetSpace(string identifier)
        {
            Assert.Equal(VolumeId, identifier);
            Calls.Add("space");
            if (FailSpace) { throw new Win32Exception(21); }
            return new(1000, 250, 100);
        }
        public string? GetFileSystem(string identifier)
        {
            Assert.Equal(VolumeId, identifier);
            Calls.Add("filesystem"); return FileSystem;
        }
    }
}
