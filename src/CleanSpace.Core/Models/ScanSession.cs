namespace CleanSpace.Core.Models;

public enum ScanStatus { Idle, Scanning, Completed, Partial, Cancelled, Failed }

public enum ScanWarning
{
    ScopeUnavailable, EntryUnavailable, UnsafeEntry, ChangedEntry, DuplicateIdentity,
    InvalidMetadata, EntryLimit, DepthLimit, TimeLimit, MemoryLimit, ProviderFailure, AlreadyScanning
}

public sealed record ScanScope(string Source, string VolumeIdentity);

public sealed record ScanTotals(long Files, long LogicalBytes, long SkippedEntries, long EntriesExamined);

public sealed record ScanProgress(Guid SessionId, ScanTotals Totals, TimeSpan Elapsed);

public sealed record ScanSession(Guid Id, ScanStatus Status, ScanScope? Scope,
    DateTimeOffset StartedAt, TimeSpan Duration, ScanTotals Totals, IReadOnlyList<ScanWarning> Warnings);

public sealed record ScanLimits
{
    public int MaxEntries { get; }
    public int MaxDepth { get; }
    public TimeSpan MaxDuration { get; }
    public int MaxTrackedIdentities { get; }

    public ScanLimits(int maxEntries = 50_000, int maxDepth = 16, TimeSpan? maxDuration = null,
        int maxTrackedIdentities = 16_384)
    {
        var duration = maxDuration ?? TimeSpan.FromSeconds(10);
        if (maxEntries is < 1 or > 100_000 || maxDepth is < 0 or > 32
            || duration <= TimeSpan.Zero || duration > TimeSpan.FromSeconds(30)
            || maxTrackedIdentities is < 1 or > 100_001)
        {
            throw new ArgumentOutOfRangeException(nameof(maxEntries), "Scan budgets exceed supported bounds.");
        }
        MaxEntries = maxEntries;
        MaxDepth = maxDepth;
        MaxDuration = duration;
        MaxTrackedIdentities = maxTrackedIdentities;
    }
}
