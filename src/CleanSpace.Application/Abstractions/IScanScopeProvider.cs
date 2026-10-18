using CleanSpace.Core.Models;

namespace CleanSpace.Application.Abstractions;

// Synchronous metadata-only ports. The use case invokes every method on a worker thread.
// A lease binds ancestry and volume identity; implementations must never follow redirections.
public interface IScanScopeProvider
{
    IScanScopeLease Open(CancellationToken cancellationToken);
}

public interface IScanScopeLease : IDisposable
{
    ScanScope Scope { get; }
    IScanDirectory Root { get; }
    void Validate();
}

public interface IScanDirectory : IDisposable
{
    ScanEntry Identity { get; }
    void Validate();
    ScanEntry? ReadNext();
    IScanDirectory OpenDirectory(ScanEntry entry);
    ScanEntry ReadFileMetadata(ScanEntry entry);
}

public sealed record ScanEntry(string Name, ulong Id, long LogicalBytes, long ModifiedTime, uint Attributes)
{
    public bool IsDirectory => (Attributes & 0x10) != 0;
    // Reparse, offline, device, recall-on-open, recall-on-data-access.
    public bool IsUnsafe => (Attributes & (0x400u | 0x1000u | 0x40u | 0x40000u | 0x400000u)) != 0;
    public bool IsProtectedDirectory => IsDirectory && IsProtectedDirectoryName(Name);

    public static bool IsProtectedDirectoryName(string name) => name.Equals("Windows", StringComparison.OrdinalIgnoreCase)
        || name.Equals("System32", StringComparison.OrdinalIgnoreCase) || name.Equals("WinSxS", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Program Files", StringComparison.OrdinalIgnoreCase) || name.Equals("Program Files (x86)", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Downloads", StringComparison.OrdinalIgnoreCase) || name.Equals("Documents", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Desktop", StringComparison.OrdinalIgnoreCase);

    public bool HasSafeName => Name.Length is > 0 and <= 255 && Name is not "." and not ".."
        && Name[^1] is not '.' and not ' ' && !Name.Any(c => c < 32 || "\\/:*?\"<>|".Contains(c));
}
