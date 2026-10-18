namespace CleanSpace.Application.DTOs;

// Provider boundary: raw optional metrics must be validated by DriveOverviewUseCase.
public sealed record DriveInfoSnapshot(string VolumeRoot, long? TotalBytes, long? TotalFreeBytes,
    long? AvailableToUserBytes, string? FileSystem, DateTimeOffset FetchedAt);
