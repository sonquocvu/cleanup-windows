using System.ComponentModel;
using System.Security;
using CleanSpace.Application.Abstractions;
using CleanSpace.Application.DTOs;
using CleanSpace.Core.Models;

namespace CleanSpace.Application.UseCases;

public sealed class DriveOverviewUseCase(IDriveInfoProvider provider)
{
    public async Task<DriveOverviewResult> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var snapshot = await provider.GetSystemDriveAsync(cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            // Only a drive root or opaque volume identifier may reach the UI. No private mount paths.
            if (snapshot is null || !IsVolumeRoot(snapshot.VolumeRoot))
            {
                return new(null, "Windows volume identity is unavailable. Try Refresh.");
            }

            var fileSystem = snapshot.FileSystem is { Length: > 0 and <= 32 } name
                && name.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '.')
                ? name : null;
            return new(new DriveOverview(snapshot.VolumeRoot, snapshot.TotalBytes, snapshot.TotalFreeBytes,
                snapshot.AvailableToUserBytes, fileSystem, snapshot.FetchedAt));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException
            or SecurityException or Win32Exception or NotSupportedException or InvalidOperationException
            or ArgumentException or OverflowException)
        {
            // Never surface provider exception messages, native paths, or raw OS errors.
            return new(null, "Windows drive information could not be read. The volume may be unavailable or access may be restricted. Try Refresh.");
        }
    }

    private static bool IsVolumeRoot(string? root) => root is not null &&
        ((root.Length == 3 && char.IsAsciiLetter(root[0]) && root[1] == ':' && root[2] == '\\')
        || (root.Length == 49 && root.StartsWith(@"\\?\Volume{", StringComparison.OrdinalIgnoreCase)
            && root.EndsWith(@"}\", StringComparison.Ordinal)
            && Guid.TryParseExact(root.AsSpan(11, 36), "D", out _)));
}
