using CleanSpace.Application.Abstractions;
using CleanSpace.Core.Models;
using CleanSpace.Infrastructure.Windows;

namespace CleanSpace.Infrastructure.Scanning;

public sealed class WindowsTempScopeProvider : IScanScopeProvider
{
    private static readonly Guid LocalAppData = new("F1B32785-6FBA-4FCF-9D55-7B8E7F157091");
    private static readonly Guid Profile = new("5E6C858F-0E22-4760-9AFE-EA3317B67173");

    public IScanScopeLease Open(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows()) { throw new PlatformNotSupportedException(); }
        cancellationToken.ThrowIfCancellationRequested();
        var local = WindowsScanNative.KnownFolder(LocalAppData, false);
        var defaultLocal = WindowsScanNative.KnownFolder(LocalAppData, true);
        var profile = WindowsScanNative.KnownFolder(Profile, false);
        var defaultProfile = WindowsScanNative.KnownFolder(Profile, true);
        var drive = new WindowsDriveMetadataApi();
        var systemRoot = drive.GetVolumeRoot(drive.GetWindowsDirectory());
        var volume = drive.GetVolumeIdentifier(systemRoot);
        // This adapter's directory identity format is verified for NTFS only; no fallback identity scheme.
        if (!string.Equals(drive.GetFileSystem(volume), "NTFS", StringComparison.OrdinalIgnoreCase))
        { throw new IOException("Unsupported system filesystem."); }
        var segments = ValidateLocations(local, defaultLocal, profile, defaultProfile, systemRoot);
        var chain = new List<WindowsScanDirectory>();
        try
        {
            chain.Add(new(WindowsScanNative.OpenVolume(volume), volume, ""));
            // Open each ancestor by its parent handle, without enumerating any ancestor.
            foreach (var segment in segments.Append("Temp"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                chain.Add(chain[^1].Open(segment));
            }
            if (!string.Equals(systemRoot, drive.GetVolumeRoot(drive.GetWindowsDirectory()), StringComparison.OrdinalIgnoreCase)
                || !string.Equals(volume, drive.GetVolumeIdentifier(systemRoot), StringComparison.OrdinalIgnoreCase))
            { throw new IOException("System volume changed."); }
            return new Lease(chain, new("Current-user Local AppData Temp", volume));
        }
        catch
        {
            foreach (var item in chain.AsEnumerable().Reverse()) { item.Dispose(); }
            throw;
        }
    }

    internal static string[] ValidateLocations(string local, string defaultLocal, string profile,
        string defaultProfile, string systemRoot)
    {
        static bool Equal(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        static bool Plain(string path) => path.Length >= 3 && char.IsAsciiLetter(path[0]) && path[1] == ':'
            && path[2] == '\\' && path == Path.GetFullPath(path).TrimEnd('\\') && !path.Contains('/');
        if (!Plain(local) || !Plain(profile) || !Equal(local, defaultLocal) || !Equal(profile, defaultProfile)
            || !local.StartsWith(profile + "\\", StringComparison.OrdinalIgnoreCase)
            || systemRoot.Length != 3 || !Equal(Path.GetPathRoot(local)!, systemRoot))
        { throw new IOException("Trusted scope unavailable."); }
        var segments = local[3..].Split('\\');
        if (segments.Length > 32 || segments.Any(segment => !new ScanEntry(segment, 1, 0, 0, 0).HasSafeName
            || ScanEntry.IsProtectedDirectoryName(segment)))
        { throw new IOException("Untrusted ancestry."); }
        return segments;
    }

    private sealed class Lease(List<WindowsScanDirectory> chain, ScanScope scope) : IScanScopeLease
    {
        public ScanScope Scope { get; } = scope;
        public IScanDirectory Root => chain[^1];
        public void Validate() => chain[^1].Validate();
        public void Dispose()
        {
            foreach (var item in chain.AsEnumerable().Reverse()) { item.Dispose(); }
        }
    }
}
