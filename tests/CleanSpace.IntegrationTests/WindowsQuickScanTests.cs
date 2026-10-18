using CleanSpace.Application.Abstractions;
using CleanSpace.Application.UseCases;
using CleanSpace.Core.Models;
using CleanSpace.Infrastructure.Scanning;
using CleanSpace.Infrastructure.Windows;
using Xunit;

namespace CleanSpace.IntegrationTests;

public sealed class WindowsQuickScanTests
{
    [Theory]
    [InlineData(@"Q:\Users\owner\AppData\Local", @"Q:\Users\owner\AppData\Local", @"Q:\Users\owner", @"Q:\Users\owner", @"Q:\", true)]
    [InlineData(@"R:\Users\owner\AppData\Local", @"R:\Users\owner\AppData\Local", @"R:\Users\owner", @"R:\Users\owner", @"Q:\", false)]
    [InlineData(@"Q:\redirected", @"Q:\Users\owner\AppData\Local", @"Q:\Users\owner", @"Q:\Users\owner", @"Q:\", false)]
    [InlineData(@"Q:\Users\owner2\AppData\Local", @"Q:\Users\owner2\AppData\Local", @"Q:\Users\owner", @"Q:\Users\owner", @"Q:\", false)]
    [InlineData(@"\\host\share\Local", @"\\host\share\Local", @"\\host\share", @"\\host\share", @"Q:\", false)]
    [InlineData(@"Q:\Users\owner\..\escape", @"Q:\Users\owner\..\escape", @"Q:\Users\owner", @"Q:\Users\owner", @"Q:\", false)]
    [InlineData(@"Q:\Users\owner\AppData\Local", @"Q:\Users\owner\AppData\Local", @"Q:\Users\owner", @"Q:\Users\other", @"Q:\", false)]
    public void KnownFolderTrustRequiresDefaultUserAncestryAndSystemVolume(string local, string defaults,
        string profile, string defaultProfile, string system, bool allowed)
    {
        if (allowed) { Assert.Equal(new[] { "Users", "owner", "AppData", "Local" }, WindowsTempScopeProvider.ValidateLocations(local, defaults, profile, defaultProfile, system)); }
        else { Assert.Throws<IOException>(() => WindowsTempScopeProvider.ValidateLocations(local, defaults, profile, defaultProfile, system)); }
    }

    [Fact]
    public async Task RealMetadataAdapterReadsOnlyIsolatedFixtureAndRejectsJunction()
    {
        var workspace = FindWorkspace();
        var owner = Path.Combine(workspace, "artifacts", "task-003", "fixtures", Guid.NewGuid().ToString("N"));
        var fixture = Path.Combine(owner, "scan");
        Directory.CreateDirectory(Path.Combine(fixture, "nested"));
        File.WriteAllBytes(Path.Combine(fixture, "first.bin"), new byte[37]);
        File.WriteAllBytes(Path.Combine(fixture, "nested", "second.bin"), new byte[51]);
        var outside = Path.Combine(owner, "outside");
        Directory.CreateDirectory(outside);
        File.WriteAllBytes(Path.Combine(outside, "must-not-scan.bin"), new byte[99]);
        var link = Path.Combine(fixture, "link");
        // A junction requires no symlink privilege. Both endpoints are owned by this test.
        // This fixture-only process is never present in the production adapter.
        using (var create = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe")
        {
            Arguments = $"/d /c mklink /J \"{link}\" \"{outside}\"",
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        })!)
        {
            await create.WaitForExitAsync();
            Assert.Equal(0, create.ExitCode);
        }
        Assert.True((File.GetAttributes(link) & FileAttributes.ReparsePoint) != 0);
        Assert.Throws<IOException>(() => new FixtureProvider(link).Open(default));
        var before = new FileInfo(Path.Combine(fixture, "first.bin"));
        var length = before.Length;
        var modified = before.LastWriteTimeUtc;
        var result = await new QuickScanUseCase(new FixtureProvider(fixture)).ExecuteAsync();
        Assert.Equal(2, result.Totals.Files);
        Assert.Equal(88, result.Totals.LogicalBytes);
        Assert.Equal(ScanStatus.Partial, result.Status);
        Assert.Equal(1, result.Totals.SkippedEntries);
        before.Refresh();
        Assert.Equal(length, before.Length);
        Assert.Equal(modified, before.LastWriteTimeUtc);
        // Fixtures intentionally retained as evidence; no delete/cleanup API is used.
    }

    [Fact]
    public void NativeHandleRejectsTraversalAndDetectsChangesEvenWithAWriter()
    {
        var fixture = Path.Combine(FindWorkspace(), "artifacts", "task-003", "fixtures", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixture);
        var path = Path.Combine(fixture, "locked.bin");
        File.WriteAllBytes(path, new byte[1]);
        using var lease = new FixtureProvider(fixture).Open(default);
        Assert.Throws<IOException>(() => lease.Root.ReadFileMetadata(new("..\\escape", 2, 0, 0, 0)));
        var entry = lease.Root.ReadNext()!;
        using var writer = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
        // Windows permits attribute-only queries even when file data is locked. No content is read.
        Assert.Equal(entry, lease.Root.ReadFileMetadata(entry));
        writer.Write(new byte[8]);
        writer.Flush();
        Assert.NotEqual(entry, lease.Root.ReadFileMetadata(entry));
    }

    private static string FindWorkspace()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "CleanSpace.slnx"))) { current = current.Parent; }
        return current?.FullName ?? throw new IOException("Workspace unavailable.");
    }

    private sealed class FixtureProvider(string path) : IScanScopeProvider
    {
        public IScanScopeLease Open(CancellationToken cancellationToken)
        {
            var api = new WindowsDriveMetadataApi();
            var root = Path.GetPathRoot(path)!;
            var volume = api.GetVolumeIdentifier(root);
            var chain = new List<WindowsScanDirectory> { new(WindowsScanNative.OpenVolume(volume), volume, "") };
            try
            {
                foreach (var name in path[3..].Split('\\')) { chain.Add(chain[^1].Open(name)); }
                return new FixtureLease(chain);
            }
            catch { foreach (var item in chain.AsEnumerable().Reverse()) { item.Dispose(); } throw; }
        }
    }
    private sealed class FixtureLease(List<WindowsScanDirectory> chain) : IScanScopeLease
    {
        public ScanScope Scope => new("Test-owned fixture", "fixture-volume");
        public IScanDirectory Root => chain[^1];
        public void Validate() => chain[^1].Validate();
        public void Dispose() { foreach (var item in chain.AsEnumerable().Reverse()) { item.Dispose(); } }
    }
}
