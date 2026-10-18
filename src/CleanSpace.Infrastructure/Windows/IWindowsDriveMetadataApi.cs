namespace CleanSpace.Infrastructure.Windows;

// Deliberately narrow native seam: no enumeration, file handles, content reads or mutations.
internal interface IWindowsDriveMetadataApi
{
    string GetWindowsDirectory();
    string GetVolumeRoot(string windowsDirectory);
    string GetVolumeIdentifier(string volumeRoot);
    DriveSpaceBytes GetSpace(string volumeIdentifier);
    string? GetFileSystem(string volumeIdentifier);
}

internal readonly record struct DriveSpaceBytes(long? Total, long? Free, long? AvailableToUser);
