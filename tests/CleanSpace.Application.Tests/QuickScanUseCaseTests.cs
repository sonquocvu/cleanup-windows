using CleanSpace.Application.Abstractions;
using CleanSpace.Application.UseCases;
using CleanSpace.Core.Models;
using Xunit;

namespace CleanSpace.Application.Tests;

public sealed class QuickScanUseCaseTests
{
    private static ScanEntry FileEntry(ulong id = 2, long size = 12, string name = "file") => new(name, id, size, 1, 0x80);

    [Fact]
    public async Task EmptyAndRepeatedSessionsHaveFreshIdsAndNoAccumulation()
    {
        var provider = new FakeProvider(() => new FakeDirectory([]));
        var scan = new QuickScanUseCase(provider);
        var first = await scan.ExecuteAsync();
        var second = await scan.ExecuteAsync();
        Assert.Equal(ScanStatus.Completed, first.Status);
        Assert.Equal(new ScanTotals(0, 0, 0, 0), first.Totals);
        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(first.Totals, second.Totals);
        Assert.Equal(2, provider.Opens);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("denied")]
    [InlineData("unexpected")]
    public async Task UnavailableRootReturnsRedactedSafeFailure(string failure)
    {
        var provider = new FakeProvider(() => throw failure switch
        {
            "missing" => new DirectoryNotFoundException("private-path"),
            "denied" => new UnauthorizedAccessException("private-path"),
            _ => new InvalidOperationException("private-path")
        });
        var result = await new QuickScanUseCase(provider).ExecuteAsync();
        Assert.Equal(ScanStatus.Failed, result.Status);
        Assert.Null(result.Scope);
        Assert.Equal(0, result.Totals.Files);
        Assert.DoesNotContain("private-path", result.ToString());
    }

    [Theory]
    [InlineData(0x400u)] // junction/symlink/reparse
    [InlineData(0x410u)]
    [InlineData(0x1000u)]
    [InlineData(0x40000u)]
    [InlineData(0x400000u)]
    [InlineData(0x40u)]
    public async Task UnsafeNodesAreNeverOpened(uint attributes)
    {
        var directory = new FakeDirectory([FileEntry() with { Attributes = attributes }]);
        var result = await new QuickScanUseCase(new FakeProvider(() => directory)).ExecuteAsync();
        Assert.Equal(ScanStatus.Partial, result.Status);
        Assert.Equal(1, result.Totals.SkippedEntries);
        Assert.Equal(0, directory.MetadataReads + directory.DirectoryOpens);
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("..\\escape")]
    [InlineData("Z:\\escape")]
    [InlineData("\\\\host\\share")]
    [InlineData("file:stream")]
    [InlineData("..")]
    [InlineData("file.")]
    [InlineData("file ")]
    public async Task EscapingOrAmbiguousNamesAreRejectedBeforeAccess(string name)
    {
        var directory = new FakeDirectory([FileEntry(name: name)]);
        var result = await new QuickScanUseCase(new FakeProvider(() => directory)).ExecuteAsync();
        Assert.Equal(1, result.Totals.SkippedEntries);
        Assert.Equal(0, directory.MetadataReads);
    }

    [Fact]
    public async Task ChangedFileAndChangedDirectoryFailClosed()
    {
        var directory = new FakeDirectory([FileEntry(), FileEntry(3) with { Attributes = 0x10 }])
        {
            ReadFile = entry => entry with { LogicalBytes = entry.LogicalBytes + 1 },
            OpenChild = _ => new FakeDirectory([]) { Identity = FileEntry(99) with { Attributes = 0x10 } }
        };
        var result = await new QuickScanUseCase(new FakeProvider(() => directory)).ExecuteAsync();
        Assert.Equal(0, result.Totals.Files);
        Assert.Equal(2, result.Totals.SkippedEntries);
        Assert.Contains(ScanWarning.ChangedEntry, result.Warnings);
    }

    [Fact]
    public async Task InaccessibleEntryAndDirectoryYieldPartialTotals()
    {
        var directory = new FakeDirectory([FileEntry(), FileEntry(3), FileEntry(4) with { Attributes = 0x10 }])
        {
            ReadFile = entry => entry.Id == 3 ? throw new IOException() : entry,
            OpenChild = _ => throw new UnauthorizedAccessException()
        };
        var result = await new QuickScanUseCase(new FakeProvider(() => directory)).ExecuteAsync();
        Assert.Equal(ScanStatus.Partial, result.Status);
        Assert.Equal(new ScanTotals(1, 12, 2, 3), result.Totals);
    }

    [Fact]
    public async Task DuplicateIdsIncludingHardLinksAreCountedOnce()
    {
        var directory = new FakeDirectory([FileEntry(), FileEntry(name: "hardlink"), FileEntry(1, name: "cycle") with { Attributes = 0x10 }]);
        var result = await new QuickScanUseCase(new FakeProvider(() => directory)).ExecuteAsync();
        Assert.Equal(new ScanTotals(1, 12, 2, 3), result.Totals);
        Assert.Equal(1, directory.MetadataReads);
    }

    [Fact]
    public async Task InvalidAndOverflowingLengthsAreExcludedWithoutCorruptingTotals()
    {
        var directory = new FakeDirectory([FileEntry(2, -1), FileEntry(3, long.MaxValue), FileEntry(4, 1), FileEntry(0),
            FileEntry(5) with { ModifiedTime = -1 }]);
        var result = await new QuickScanUseCase(new FakeProvider(() => directory)).ExecuteAsync();
        Assert.Equal(1, result.Totals.Files);
        Assert.Equal(long.MaxValue, result.Totals.LogicalBytes);
        Assert.Equal(4, result.Totals.SkippedEntries);
    }

    [Fact]
    public async Task EntryBudgetBoundsHugeLazyProvider()
    {
        var directory = new FakeDirectory(Enumerable.Range(2, 1_000_000).Select(i => FileEntry((ulong)i)));
        var result = await new QuickScanUseCase(new FakeProvider(() => directory)).ExecuteAsync(limits: new(123));
        Assert.Equal(123, result.Totals.Files);
        Assert.Equal(123, directory.EnumerationCalls);
        Assert.Contains(ScanWarning.EntryLimit, result.Warnings);
        Assert.True(directory.Disposed);
    }

    [Fact]
    public async Task DepthBudgetSkipsSubtreesWithoutOpeningThem()
    {
        var directory = new FakeDirectory([FileEntry() with { Attributes = 0x10 }]);
        var result = await new QuickScanUseCase(new FakeProvider(() => directory)).ExecuteAsync(limits: new(maxDepth: 0));
        Assert.Contains(ScanWarning.DepthLimit, result.Warnings);
        Assert.Equal(0, directory.DirectoryOpens);
    }

    [Fact]
    public async Task RetainedIdentityMemoryBudgetStopsWithPartialTotals()
    {
        var directory = new FakeDirectory(Enumerable.Range(2, 100).Select(i => FileEntry((ulong)i)));
        var result = await new QuickScanUseCase(new FakeProvider(() => directory)).ExecuteAsync(limits: new(maxTrackedIdentities: 3));
        Assert.Equal(2, result.Totals.Files); // Root identity uses one slot.
        Assert.Contains(ScanWarning.MemoryLimit, result.Warnings);
        Assert.Equal(ScanStatus.Partial, result.Status);
    }

    [Theory]
    [InlineData("Windows")]
    [InlineData("System32")]
    [InlineData("WinSxS")]
    [InlineData("Program Files")]
    [InlineData("Downloads")]
    public async Task ProtectedDirectoryNamesAreNeverTraversed(string name)
    {
        var directory = new FakeDirectory([FileEntry(name: name) with { Attributes = 0x10 }]);
        var result = await new QuickScanUseCase(new FakeProvider(() => directory)).ExecuteAsync();
        Assert.Equal(1, result.Totals.SkippedEntries);
        Assert.Equal(0, directory.DirectoryOpens);
    }

    [Fact]
    public async Task TimeBudgetIncludesScopeEstablishmentAndInFlightMetadata()
    {
        var clock = new FakeClock();
        var directory = new FakeDirectory([FileEntry()]) { OnRead = () => clock.Ticks += TimeSpan.TicksPerSecond * 2 };
        var result = await new QuickScanUseCase(new FakeProvider(() => directory), clock)
            .ExecuteAsync(limits: new(maxDuration: TimeSpan.FromSeconds(1)));
        Assert.Contains(ScanWarning.TimeLimit, result.Warnings);
        Assert.Equal(0, result.Totals.Files);
    }

    [Fact]
    public async Task CancellationBeforeOpenDoesNotTouchProvider()
    {
        var provider = new FakeProvider(() => new FakeDirectory([]));
        var result = await new QuickScanUseCase(provider).ExecuteAsync(cancellationToken: new(true));
        Assert.Equal(ScanStatus.Cancelled, result.Status);
        Assert.Equal(0, provider.Opens);
    }

    [Fact]
    public async Task CancellationDuringEnumerationRetainsEarlierTotalsAndDisposes()
    {
        using var cts = new CancellationTokenSource();
        var directory = new FakeDirectory([FileEntry(), FileEntry(3)]);
        directory.OnRead = () => { if (directory.EnumerationCalls == 2) { cts.Cancel(); } };
        var result = await new QuickScanUseCase(new FakeProvider(() => directory)).ExecuteAsync(cancellationToken: cts.Token);
        Assert.Equal(ScanStatus.Cancelled, result.Status);
        Assert.Equal(1, result.Totals.Files);
        Assert.True(directory.Disposed);
    }

    [Fact]
    public async Task EnumerationFailureIsPartialAndReleasesSingleSessionGuard()
    {
        var directory = new FakeDirectory([]) { OnRead = () => throw new IOException() };
        var scan = new QuickScanUseCase(new FakeProvider(() => directory));
        Assert.Equal(ScanStatus.Partial, (await scan.ExecuteAsync()).Status);
        Assert.DoesNotContain(ScanWarning.AlreadyScanning, (await scan.ExecuteAsync()).Warnings);
    }

    [Fact]
    public async Task WorkerExecutionAndOverlapExclusion()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var release = new ManualResetEventSlim();
        var directory = new FakeDirectory([]) { OnRead = () => { entered.TrySetResult(); release.Wait(TimeSpan.FromSeconds(10)); } };
        var scan = new QuickScanUseCase(new FakeProvider(() => directory));
        var first = scan.ExecuteAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            var duplicate = await scan.ExecuteAsync();
            Assert.Contains(ScanWarning.AlreadyScanning, duplicate.Warnings);
            Assert.False(first.IsCompleted);
        }
        finally { release.Set(); }
        Assert.Equal(ScanStatus.Completed, (await first).Status);
    }

    [Fact]
    public async Task ProgressIsThrottledAndContainsNoPaths()
    {
        var clock = new FakeClock();
        var updates = new List<ScanProgress>();
        var directory = new FakeDirectory(Enumerable.Range(2, 1002).Select(i => FileEntry((ulong)i)))
        { OnRead = () => clock.Ticks += TimeSpan.TicksPerMillisecond };
        var result = await new QuickScanUseCase(new FakeProvider(() => directory), clock)
            .ExecuteAsync(new CaptureProgress(updates));
        Assert.InRange(updates.Count, 6, 7);
        Assert.All(updates, update => Assert.Equal(result.Id, update.SessionId));
    }

    private sealed class CaptureProgress(List<ScanProgress> updates) : IProgress<ScanProgress>
    { public void Report(ScanProgress value) => updates.Add(value); }
    private sealed class FakeClock : TimeProvider
    {
        public long Ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Ticks;
    }
    private sealed class FakeProvider(Func<FakeDirectory> factory) : IScanScopeProvider
    {
        public int Opens;
        public IScanScopeLease Open(CancellationToken cancellationToken) { Opens++; return new FakeLease(factory()); }
    }
    private sealed class FakeLease(FakeDirectory root) : IScanScopeLease
    {
        public ScanScope Scope => new("Fake trusted scope", "fake-volume");
        public IScanDirectory Root => root;
        public void Validate() { }
        public void Dispose() => root.Dispose();
    }
    private sealed class FakeDirectory(IEnumerable<ScanEntry> entries) : IScanDirectory
    {
        private readonly IEnumerator<ScanEntry> enumerator = entries.GetEnumerator();
        public ScanEntry Identity { get; init; } = FileEntry(1) with { Attributes = 0x10 };
        public int MetadataReads, DirectoryOpens, EnumerationCalls;
        public bool Disposed;
        public Action? OnRead;
        public Func<ScanEntry, ScanEntry> ReadFile = entry => entry;
        public Func<ScanEntry, IScanDirectory> OpenChild = _ => throw new IOException();
        public void Validate() { }
        public ScanEntry? ReadNext() { EnumerationCalls++; OnRead?.Invoke(); return enumerator.MoveNext() ? enumerator.Current : null; }
        public IScanDirectory OpenDirectory(ScanEntry entry) { DirectoryOpens++; return OpenChild(entry); }
        public ScanEntry ReadFileMetadata(ScanEntry entry) { MetadataReads++; return ReadFile(entry); }
        public void Dispose() { Disposed = true; enumerator.Dispose(); }
    }
}
