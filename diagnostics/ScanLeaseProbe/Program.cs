using System.Diagnostics;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using CleanSpace.Application.Abstractions;
using Microsoft.Win32.SafeHandles;
using ScanSharingProbe;

namespace ScanLeaseProbe;

internal static class Program
{
    private static readonly List<object> Evidence = [];
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private static int checks;
    private static async Task<int> Main(string[] args)
    {
        if (!OperatingSystem.IsWindows()) { throw new PlatformNotSupportedException(); }
        if (new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator))
        { throw new InvalidOperationException("Normal user token required."); }
        if (args.FirstOrDefault() == "--child") { return Fixture.Child(args); }
        Directory.CreateDirectory(Fixture.Artifacts);
        var report = Path.Combine(Fixture.Artifacts, "lease-probe-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            await Sharing();
            await ProtectedMovement();
            await BetweenLeases();
            await Replacement("mkdir");
            await Replacement("junction");
            await Replacement("symlink");
            await CancellationAndFailures();
            await CompletenessAndBudgets();
            BaselinePerformance(false);
            BaselinePerformance(true);
            await File.WriteAllTextAsync(report, JsonSerializer.Serialize(new { Status = "PASS", Checks = checks,
                Elevated = false, Recommendation = "KEEP PRODUCTION SCANNING DISABLED; ARCHITECTURAL REVIEW REQUIRED", Evidence }, Json));
            Console.WriteLine(JsonSerializer.Serialize(new { Status = "PASS", Checks = checks, Report = report }, Json));
            return 0;
        }
        catch (Exception error)
        {
            await File.WriteAllTextAsync(report, JsonSerializer.Serialize(new { Status = "FAIL", Checks = checks,
                Error = error.ToString(), Evidence }, Json));
            Console.Error.WriteLine(error);
            Console.WriteLine(report);
            return 1;
        }
    }
    private static void Require(bool value, string message)
    { if (!value) { throw new IOException(message); } checks++; }
    private static string Rejected(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is IOException or OperationCanceledException or ObjectDisposedException)
        { checks++; return error.Message; }
        throw new IOException("Uncertain operation was accepted.");
    }
    private static async Task<Fixture.Reply> Operation(Fixture f, string action, string path, string extra, params int[] errors)
    {
        var result = await f.Other(action, path, extra);
        Evidence.Add(new { Scenario = "ChildOperation", Action = action, Path = path, result.Pid, result.Error, result.Detail });
        Require(errors.Contains(result.Error), $"Unexpected {action} result: {result.Error} {result.Detail}");
        return result;
    }
    private static void Released(ProtectedCursor scan)
    { Require(scan.RestrictiveHandlesClosed, "Restrictive handle leaked outside lease."); }

    private static async Task Sharing()
    {
        var f = new Fixture();
        using var scan = new ProtectedCursor(f);
        var rows = new List<object>();
        using (scan.Acquire(CancellationToken.None, "forced-sharing-matrix"))
        {
            foreach (var path in new[] { ".", "ancestor", @"ancestor\Temp", Fixture.Target })
            foreach (var mask in new[] { 0x80u, 1u, 2u, 0x100u, 0x10000u })
            {
                var result = await Operation(f, "open", path, mask.ToString("X"), mask == 0x10000 ? 32 : 0);
                rows.Add(new { Path = path, Access = $"0x{mask:X}", result.Pid, result.Error });
            }
            await Operation(f, "create", Fixture.Target + @"\created-during-lease.bin", "", 0);
        }
        Released(scan);
        foreach (var path in new[] { ".", "ancestor", @"ancestor\Temp", Fixture.Target })
        { await Operation(f, "open", path, "10000", 0); }
        Evidence.Add(new { Scenario = "SharingMatrix", DuringLease = rows, BetweenLeasesDeleteOpens = "4/4 succeeded",
            Holds = scan.Holds.ToArray(), Scope = "Four owned fixture directories only; live cursor share=7" });
    }

    private static async Task ProtectedMovement()
    {
        var f = new Fixture();
        using var scan = new ProtectedCursor(f);
        var batch = scan.Next(CancellationToken.None, () =>
        {
            foreach (var from in new[] { "ancestor", @"ancestor\Temp", Fixture.Target })
            {
                Operation(f, "move", from, @"outside\relocated", 32).GetAwaiter().GetResult();
            }
            // First leg of an out-and-back schedule is refused while the same lease protects the native query.
            Operation(f, "move", Fixture.Target, @"outside\aba", 32).GetAwaiter().GetResult();
        });
        Require(batch?.Count == 1 && scan.NativeQueries == 1, "Protected enumeration failed.");
        Released(scan);
        Evidence.Add(new { Scenario = "ProtectedRelocationAndABA", QueryInProtectedScope = true,
            AllMoveAttemptsError = 32, scan.NativeQueries, scan.Holds });
    }

    private static async Task BetweenLeases()
    {
        foreach (var ancestor in new[] { "ancestor", @"ancestor\Temp" })
        {
            var ancestorFixture = new Fixture();
            using var cursor = new ProtectedCursor(ancestorFixture);
            Released(cursor);
            var attempt = await Operation(ancestorFixture, "move", ancestor, @"outside\moved-ancestor", 0, 5);
            if (attempt.Error == 0)
            {
                Rejected(() => cursor.Next(CancellationToken.None));
                Require(cursor.NativeQueries == 0, "Moved ancestor queried.");
            }
            Evidence.Add(new { Scenario = "BetweenLeaseAncestorMove", Ancestor = ancestor, attempt.Error,
                RestrictiveHandlesHeld = 0, CursorShare = 7,
                Note = attempt.Error == 5 ? "OS refuses cross-parent ancestor move with open descendant cursor; residual interference"
                    : "Move succeeded; reacquisition rejected before query" });
        }
        var f = new Fixture();
        using var scan = new ProtectedCursor(f);
        await Operation(f, "move", Fixture.Target, @"outside\relocated", 0);
        Require(scan.NativeQueries == 0, "Cursor enumerated outside a lease.");
        await Operation(f, "create", @"outside\relocated\inserted-while-outside.bin", "", 0);
        await Operation(f, "move", @"outside\relocated", Fixture.Target, 0);
        Require(scan.NativeQueries == 0, "Out-of-scope metadata enumeration occurred.");
        Require(scan.Next(CancellationToken.None)?.Count == 2, "Restored in-scope cursor could not enumerate.");
        Released(scan);
        Evidence.Add(new { Scenario = "BetweenLeaseABA", MovesSucceeded = true,
            QueriesWhileOutside = 0, QueryAfterProtectedReacquisition = true });

        f = new Fixture();
        using var moved = new ProtectedCursor(f);
        await Operation(f, "move", Fixture.Target, @"outside\relocated", 0);
        var reason = Rejected(() => moved.Next(CancellationToken.None));
        Require(moved.NativeQueries == 0, "Moved cursor queried before containment rejection.");
        Released(moved);
        Evidence.Add(new { Scenario = "BetweenLeasePersistentMove", RejectedBeforeQuery = true, Reason = reason });
    }

    private static async Task Replacement(string kind)
    {
        var f = new Fixture();
        using var scan = new ProtectedCursor(f);
        await Operation(f, "move", Fixture.Target, @"outside\original", 0);
        var result = await Operation(f, kind, Fixture.Target, "outside", kind == "symlink" ? [0, 1314] : [0]);
        if (result.Error == 1314)
        {
            Evidence.Add(new { Scenario = "SymlinkReplacement", Status = "NOT EXERCISED", NativeError = 1314,
                Reason = "Normal-user symlink creation privilege unavailable; no settings/elevation used." });
            return;
        }
        var reason = Rejected(() => scan.Next(CancellationToken.None));
        Require(scan.NativeQueries == 0, "Replacement/reparse target was enumerated.");
        Released(scan);
        Evidence.Add(new { Scenario = "Replacement", Kind = kind, RejectedBeforeQuery = true, Reason = reason });
    }

    private static async Task CancellationAndFailures()
    {
        var f = new Fixture();
        using var scan = new ProtectedCursor(f);
        using (var cancellation = new CancellationTokenSource())
        {
            Rejected(() => scan.Next(cancellation.Token, () => cancellation.Cancel()));
            Require(scan.NativeQueries == 0, "Cancelled lease performed a query.");
        }
        Released(scan);
        await Operation(f, "open", Fixture.Target, "10000", 0);
        for (var stage = 0; stage < 4; stage++)
        {
            var failAt = stage;
            Rejected(() => scan.Acquire(CancellationToken.None, "injected-acquire-failure", i =>
            { if (i == failAt) { throw new IOException("Injected failure after open " + i); } }));
            Released(scan);
            await Operation(f, "open", Fixture.Target, "10000", 0);
        }
        Rejected(() => scan.Next(CancellationToken.None, () => throw new IOException("Injected before native query")));
        Released(scan);
        Rejected(() => scan.Next(CancellationToken.None, afterQuery: () => throw new IOException("Injected after native query")));
        Released(scan);
        using (var cancellation = new CancellationTokenSource())
        {
            scan.RestartForDedupTest();
            Rejected(() => scan.Next(cancellation.Token, afterQuery: () => cancellation.Cancel()));
        }
        Released(scan);
        scan.Dispose();
        Rejected(() => scan.Next(CancellationToken.None)); // Native-query failure with a closed cursor.
        Released(scan);
        await Operation(f, "open", Fixture.Target, "10000", 0);
        Evidence.Add(new { Scenario = "CancellationAndFailureDisposal", AllRestrictiveHandlesClosed = true,
            Cases = "Cancellation before/after query; four acquisition stages; before/after query throws; closed-cursor query",
            scan.Holds });
    }

    private static int Drain(ProtectedCursor scan)
    {
        var files = 0;
        while (scan.Next(CancellationToken.None) is { } batch) { files += batch.Count; Released(scan); }
        Released(scan);
        return files;
    }

    private static async Task CompletenessAndBudgets()
    {
        var f = new Fixture(512);
        using var scan = new ProtectedCursor(f);
        var elapsed = Stopwatch.StartNew();
        Require(Drain(scan) == 512, "Stable enumeration incomplete.");
        elapsed.Stop();
        Require(scan.Duplicates == 0 && scan.RetainedIdentities == 512, "Stable resume duplicated entries.");
        var holds = scan.Holds.ToArray();
        Evidence.Add(new { Scenario = "ShortLeasePerformance", Files = 512, ElapsedMilliseconds = elapsed.Elapsed.TotalMilliseconds,
            scan.NativeQueries, scan.DirectoryOpens, scan.MetadataQueries, scan.RetainedIdentities,
            Holds = holds, BufferBytes = ProtectedCursor.BufferBytes,
            RetainedInstrumentationBound = "128 leases / 512 disposed restrictive handle wrappers", EnumerationDepth = 0 });
        scan.RestartForDedupTest();
        Require(Drain(scan) == 0 && scan.Duplicates == 512 && scan.RetainedIdentities == 512,
            "Explicit cursor restart did not suppress all repeated identities.");
        Evidence.Add(new { Scenario = "RepeatedBatchesAndDedup", UniqueFiles = 512, RestartDuplicatesSuppressed = 512,
            scan.NativeQueries, Completeness = "Stable tree only; concurrent namespace changes are not an atomic snapshot" });
        f = new Fixture(2050);
        using var bounded = new ProtectedCursor(f);
        var reason = Rejected(() => Drain(bounded));
        Released(bounded);
        Require(bounded.RetainedIdentities <= ProtectedCursor.MaxEntries, "Identity memory exceeded bound.");
        Evidence.Add(new { Scenario = "EntryMemoryBudget", Reason = reason, bounded.RetainedIdentities });

        f = new Fixture();
        using var depth = new ProtectedCursor(f);
        await Operation(f, "mkdir", Fixture.Target + @"\nested", "", 0);
        await Operation(f, "create", Fixture.Target + @"\nested\not-enumerated.bin", "", 0);
        Require(Drain(depth) == 1 && depth.Skipped == 1, "Fixed-depth prototype traversed a subtree.");
        Evidence.Add(new { Scenario = "DepthBound", EnumerationDepth = 0, SkippedDirectories = depth.Skipped });
        Rejected(() => Fixture.Resolve(f.Owner, @"..\..\outside"));
        Rejected(() => Fixture.Resolve(Fixture.Workspace, "."));
        Evidence.Add(new { Scenario = "FixtureContainment", EscapedAndUnownedPathsRejected = true });

        f = new Fixture();
        using var timed = new ProtectedCursor(f);
        reason = Rejected(() => timed.Next(CancellationToken.None,
            () => Task.Delay(ProtectedCursor.MaxTime + TimeSpan.FromMilliseconds(50)).GetAwaiter().GetResult()));
        Released(timed);
        Require(timed.NativeQueries == 0, "Expired budget performed a query.");
        Evidence.Add(new { Scenario = "TimeBudgetWithForcedStall", Reason = reason, timed.NativeQueries, timed.Holds,
            Caveat = "Artificial scheduling stall demonstrates cooperative budget is NOT a hard handle-hold deadline" });
    }

    private static void BaselinePerformance(bool full)
    {
        var f = new Fixture(512);
        var handles = new List<SafeFileHandle>();
        var directories = new List<IScanDirectory>();
        var watch = Stopwatch.StartNew();
        var queries = 0;
        var seen = new HashSet<ulong>();
        try
        {
            handles.Add(full ? NativeProbe.CandidateRoot(f.Owner) : NativeProbe.Production<SafeFileHandle>("OpenVolume", f.Owner));
            directories.Add(NativeProbe.Bind(handles[^1], "owner", null));
            foreach (var segment in new[] { "ancestor", "Temp", "child" })
            {
                handles.Add(full ? NativeProbe.CandidateChild(handles[^1], segment, true)
                    : NativeProbe.Production<SafeFileHandle>("OpenChild", handles[^1], segment, true));
                directories.Add(NativeProbe.Bind(handles[^1], segment, directories[^1]));
            }
            var buffer = new byte[ProtectedCursor.BufferBytes];
            var first = true;
            while (true)
            {
                directories[^1].Validate();
                queries++;
                if (!NativeProbe.Production<bool>("ReadDirectory", handles[^1], buffer, first)) { break; }
                first = false;
                var offset = 0;
                do
                {
                    var next = BitConverter.ToInt32(buffer, offset);
                    var length = BitConverter.ToInt32(buffer, offset + 60);
                    if (length <= 0 || length > 510 || offset + 104 + length > buffer.Length)
                    { throw new IOException("Invalid baseline record."); }
                    var name = Encoding.Unicode.GetString(buffer, offset + 104, length);
                    if (name is not "." and not "..") { seen.Add(BitConverter.ToUInt64(buffer, offset + 96)); }
                    if (next == 0) { break; }
                    offset += next;
                } while (offset <= buffer.Length - 104);
            }
        }
        finally
        {
            foreach (var directory in directories.AsEnumerable().Reverse()) { directory.Dispose(); }
            foreach (var handle in handles.AsEnumerable().Reverse()) { handle.Dispose(); }
            watch.Stop();
        }
        Require(seen.Count == 512, "Baseline incomplete.");
        Evidence.Add(new { Scenario = "LongHandlePerformance", Mode = full ? "C fully shared" : "A original restrictive",
            Files = seen.Count, NativeQueries = queries, DirectoryOpens = handles.Count,
            ElapsedAndRetainedHandleSpanMilliseconds = watch.Elapsed.TotalMilliseconds, BufferBytes = ProtectedCursor.BufferBytes,
            Caveat = "Controlled flat-directory microbenchmark, not production use-case throughput; validation once per native batch" });
    }
}
