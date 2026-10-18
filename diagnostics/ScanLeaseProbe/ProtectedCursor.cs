using System.Diagnostics;
using System.Text;
using CleanSpace.Application.Abstractions;
using Microsoft.Win32.SafeHandles;
using ScanSharingProbe;

namespace ScanLeaseProbe;

// Diagnostic only: one fixed fixture leaf, no recursive traversal, no production trust resolver.
// Keeping the enumeration file object fully shared preserves its cursor without a session-long deny-delete lock.
internal sealed class ProtectedCursor : IDisposable
{
    internal const int BufferBytes = 4096, MaxRecordsPerBatch = 40, MaxEntries = 2048, MaxAncestry = 8, MaxLeases = 128;
    internal static readonly TimeSpan MaxTime = TimeSpan.FromSeconds(5);
    private static readonly string[] Segments = ["ancestor", "Temp", "child"];
    private readonly Fixture fixture;
    private readonly string expectedRoot;
    private readonly List<ulong> identities = [];
    private readonly SafeFileHandle cursor;
    private readonly byte[] buffer = new byte[BufferBytes];
    private readonly HashSet<ulong> seen = [];
    private readonly long start = Stopwatch.GetTimestamp();
    private bool started, ended;
    private int examined;
    private int leaseAttempts;
    internal int NativeQueries { get; private set; }
    internal int DirectoryOpens { get; private set; }
    internal int MetadataQueries { get; private set; }
    internal int Duplicates { get; private set; }
    internal int Skipped { get; private set; }
    internal int RetainedIdentities => seen.Count;
    internal void RestartForDedupTest() { started = false; ended = false; }
    internal List<Hold> Holds { get; } = [];
    internal List<SafeFileHandle> AllRestrictiveHandles { get; } = [];
    internal bool RestrictiveHandlesClosed => AllRestrictiveHandles.All(handle => handle.IsClosed);
    internal sealed record Hold(string Phase, double Milliseconds, int HandleCount, double[] ReturnedToClosedMilliseconds);

    internal ProtectedCursor(Fixture fixture)
    {
        this.fixture = fixture;
        Fixture.Resolve(fixture.Owner, ".");
        if (new DriveInfo(Path.GetPathRoot(fixture.Owner)!).DriveFormat != "NTFS")
        { throw new IOException("Prototype requires NTFS identities."); }
        using (var anchor = NativeProbe.CandidateRoot(fixture.Owner))
        { expectedRoot = NativeProbe.Production<string>("FinalPath", anchor); }
        using var lease = Acquire(CancellationToken.None, "initialize");
        cursor = NativeProbe.CandidateChild(lease.Handles[^2], Segments[^1], true);
        try
        {
            var identity = NativeProbe.Production<ScanEntry>("Read", cursor, Segments[^1]);
            if (identity.Id != identities[^1] || identity.IsUnsafe
                || NativeProbe.Production<string>("FinalPath", cursor) != expectedRoot + "\\" + Fixture.Target)
            { throw new IOException("Cursor identity mismatch."); }
        }
        catch { cursor.Dispose(); throw; }
    }

    internal Lease Acquire(CancellationToken token, string phase, Action<int>? afterOpen = null)
    {
        token.ThrowIfCancellationRequested();
        if (++leaseAttempts > MaxLeases) { throw new IOException("Lease-count budget."); }
        if (Segments.Length + 1 > MaxAncestry) { throw new IOException("Ancestry budget."); }
        var lease = new Lease(this, phase);
        try
        {
            for (var i = 0; i <= Segments.Length; i++)
            {
                token.ThrowIfCancellationRequested();
                var handle = i == 0 ? NativeProbe.Open(fixture.Owner, 0x100081, 3)
                    : LeaseNative.Open(lease.Handles[^1], Segments[i - 1]);
                lease.ReturnedAt.Add(Stopwatch.GetTimestamp());
                lease.Handles.Add(handle);
                AllRestrictiveHandles.Add(handle);
                DirectoryOpens++;
                afterOpen?.Invoke(i);
                var entry = NativeProbe.Production<ScanEntry>("Read", handle, i == 0 ? "owner" : Segments[i - 1]);
                var path = NativeProbe.Production<string>("FinalPath", handle);
                MetadataQueries += 2;
                var expected = expectedRoot + (i == 0 ? "" : "\\" + string.Join('\\', Segments.Take(i)));
                if (entry.Id == 0 || !entry.IsDirectory || entry.IsUnsafe
                    || !string.Equals(path, expected, StringComparison.OrdinalIgnoreCase)
                    || (identities.Count > i && entry.Id != identities[i]))
                { throw new IOException("Changed/reparse ancestry; no enumeration permitted."); }
                if (identities.Count <= i) { identities.Add(entry.Id); }
            }
            token.ThrowIfCancellationRequested();
            return lease;
        }
        catch { lease.Dispose(); throw; }
    }

    internal IReadOnlyList<ScanEntry>? Next(CancellationToken token, Action? whileHeld = null, Action? afterQuery = null)
    {
        if (ended) { return null; }
        token.ThrowIfCancellationRequested();
        if (Stopwatch.GetElapsedTime(start) > MaxTime) { throw new IOException("Time budget."); }
        // The callback is a deterministic TEST scheduling/failure hook, never production code.
        // Safety depends on pinning every fixture ancestor BEFORE the native enumeration, not on a later check.
        using var lease = Acquire(token, whileHeld is null ? "batch" : "controlled-schedule");
        whileHeld?.Invoke();
        token.ThrowIfCancellationRequested();
        if (Stopwatch.GetElapsedTime(start) > MaxTime) { throw new IOException("Time budget."); }
        NativeQueries++;
        if (!NativeProbe.Production<bool>("ReadDirectory", cursor, buffer, !started))
        { ended = true; return null; }
        afterQuery?.Invoke();
        started = true;
        var result = new List<ScanEntry>(MaxRecordsPerBatch);
        var offset = 0;
        var records = 0;
        while (true)
        {
            token.ThrowIfCancellationRequested();
            if (++records > MaxRecordsPerBatch || offset > buffer.Length - 104)
            { throw new IOException("Native batch budget/record invalid."); }
            var data = buffer.AsSpan(offset);
            var next = BitConverter.ToInt32(data);
            var bytes = BitConverter.ToInt32(data[60..]);
            if (bytes <= 0 || bytes > 510 || (bytes & 1) != 0 || bytes > data.Length - 104
                || (next != 0 && (next < 104 + bytes || next % 8 != 0 || next > data.Length - 104)))
            { throw new IOException("Invalid native record."); }
            var entry = new ScanEntry(Encoding.Unicode.GetString(data.Slice(104, bytes)),
                BitConverter.ToUInt64(data[96..]), BitConverter.ToInt64(data[40..]),
                BitConverter.ToInt64(data[24..]), BitConverter.ToUInt32(data[56..]));
            if (entry.Name is not "." and not "..")
            {
                if (++examined > MaxEntries || seen.Count >= MaxEntries) { throw new IOException("Entry/memory budget."); }
                if (!entry.HasSafeName || entry.IsUnsafe || entry.Id == 0 || entry.IsDirectory) { Skipped++; }
                else if (!seen.Add(entry.Id)) { Duplicates++; }
                else { result.Add(entry); }
            }
            if (next == 0) { break; }
            offset = checked(offset + next);
        }
        return result;
    }

    internal sealed class Lease(ProtectedCursor owner, string phase) : IDisposable
    {
        private readonly long acquired = Stopwatch.GetTimestamp(); // Conservative span includes opens/validation/close.
        private bool disposed;
        internal List<SafeFileHandle> Handles { get; } = [];
        internal List<long> ReturnedAt { get; } = [];
        public void Dispose()
        {
            if (disposed) { return; }
            disposed = true;
            var durations = new double[Handles.Count];
            for (var i = Handles.Count - 1; i >= 0; i--)
            {
                Handles[i].Dispose();
                durations[i] = Stopwatch.GetElapsedTime(ReturnedAt[i]).TotalMilliseconds;
            }
            owner.Holds.Add(new(phase, Stopwatch.GetElapsedTime(acquired).TotalMilliseconds, Handles.Count, durations));
        }
    }
    public void Dispose() => cursor.Dispose();
}
