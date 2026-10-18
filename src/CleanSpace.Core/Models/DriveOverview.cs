namespace CleanSpace.Core.Models;

// Immutable, OS/UI-independent byte values. Null means unavailable, never zero.
public sealed record DriveOverview
{
    public string VolumeRoot { get; }
    public long? TotalBytes { get; }
    public long? TotalFreeBytes { get; }
    public long? AvailableToUserBytes { get; }
    public string? FileSystem { get; }
    public DateTimeOffset FetchedAt { get; }
    public long? UsedBytes => TotalBytes - TotalFreeBytes;
    public double? PercentageUsed => TotalBytes is > 0 && UsedBytes is { } used
        ? (double)used / TotalBytes.Value * 100 : null;

    public DriveOverview(string volumeRoot, long? totalBytes, long? totalFreeBytes,
        long? availableToUserBytes, string? fileSystem, DateTimeOffset fetchedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(volumeRoot);
        if (totalBytes < 0 || totalFreeBytes < 0 || availableToUserBytes < 0
            || totalFreeBytes > totalBytes || availableToUserBytes > totalFreeBytes
            || availableToUserBytes > totalBytes)
        {
            throw new ArgumentException("Drive capacity metrics are inconsistent.");
        }

        VolumeRoot = volumeRoot;
        TotalBytes = totalBytes;
        TotalFreeBytes = totalFreeBytes;
        AvailableToUserBytes = availableToUserBytes;
        FileSystem = fileSystem;
        FetchedAt = fetchedAt;
    }
}
