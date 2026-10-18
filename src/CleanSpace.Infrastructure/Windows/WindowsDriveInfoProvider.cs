using CleanSpace.Application.Abstractions;
using CleanSpace.Application.DTOs;

namespace CleanSpace.Infrastructure.Windows;

public sealed class WindowsDriveInfoProvider : IDriveInfoProvider
{
    private readonly IWindowsDriveMetadataApi api;

    public WindowsDriveInfoProvider() : this(new WindowsDriveMetadataApi()) { }

    internal WindowsDriveInfoProvider(IWindowsDriveMetadataApi api) => this.api = api;

    // Native metadata calls are synchronous; none run on the UI thread.
    public Task<DriveInfoSnapshot> GetSystemDriveAsync(CancellationToken cancellationToken) =>
        Task.Run(() => Read(cancellationToken), cancellationToken);

    private DriveInfoSnapshot Read(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var windowsDirectory = api.GetWindowsDirectory();
        var root = api.GetVolumeRoot(windowsDirectory);
        var identifier = api.GetVolumeIdentifier(root);
        cancellationToken.ThrowIfCancellationRequested();
        var space = api.GetSpace(identifier);
        cancellationToken.ThrowIfCancellationRequested();
        var fileSystem = api.GetFileSystem(identifier);
        // Reject a changed mount/identity; all metrics above were queried by the volume GUID.
        var currentRoot = api.GetVolumeRoot(windowsDirectory);
        if (!string.Equals(root, currentRoot, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(identifier, api.GetVolumeIdentifier(currentRoot), StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException("Windows volume identity changed.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        // A mounted-folder path can contain private names. Expose its opaque volume ID instead.
        var displayRoot = root.Length == 3 && char.IsAsciiLetter(root[0]) && root[1] == ':' && root[2] == '\\'
            ? root : identifier;
        return new(displayRoot, space.Total, space.Free, space.AvailableToUser, fileSystem, DateTimeOffset.UtcNow);
    }
}
