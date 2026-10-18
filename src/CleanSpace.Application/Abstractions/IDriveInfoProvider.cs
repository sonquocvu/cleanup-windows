using CleanSpace.Application.DTOs;

namespace CleanSpace.Application.Abstractions;

public interface IDriveInfoProvider
{
    // Resolve only the current Windows volume and read metadata; never enumerate or mutate files.
    Task<DriveInfoSnapshot> GetSystemDriveAsync(CancellationToken cancellationToken);
}
