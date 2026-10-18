using CleanSpace.App.ViewModels;
using CleanSpace.Application.Abstractions;
using CleanSpace.Application.DTOs;
using CleanSpace.Application.UseCases;
using CleanSpace.Core.Models;
using Microsoft.UI.Xaml;

namespace CleanSpace.App;

// Separate, non-shipping executable linking production XAML/ViewModels. Only fake scope/drive data.
public partial class App : Microsoft.UI.Xaml.Application
{
    private MainWindow? window;
    public App() => InitializeComponent();
    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        var settings = new SettingsViewModel(new(new MemoryThemeStore()));
        var dashboard = new DashboardViewModel(new(new FakeDrive()));
        var scan = new ScanResultsViewModel(new(new Scenarios()), scanAvailable: true);
        window = new MainWindow(new(settings, dashboard, scan)) { Title = "CleanSpace — Quick Scan UI probe (fake data)" };
        window.Closed += (_, _) => { scan.Dispose(); dashboard.Dispose(); };
        window.Activate();
        await Task.WhenAll(settings.InitializeAsync(), dashboard.InitializeAsync());
    }
    private sealed class FakeDrive : IDriveInfoProvider
    {
        public Task<DriveInfoSnapshot> GetSystemDriveAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new DriveInfoSnapshot(@"Q:\", 1000, 500, 500, "NTFS", DateTimeOffset.UtcNow));
    }
    private sealed class MemoryThemeStore : IThemeSettingsStore
    {
        private ThemeSettings value = ThemeSettings.Default;
        public Task<ThemeSettings?> ReadAsync(CancellationToken token) => Task.FromResult<ThemeSettings?>(value);
        public Task WriteAsync(ThemeSettings settings, CancellationToken token) { value = settings; return Task.CompletedTask; }
    }
    private sealed class Scenarios : IScanScopeProvider
    {
        private int calls;
        public IScanScopeLease Open(CancellationToken token)
        {
            var call = ++calls;
            if (call == 1) { throw new IOException(@"private-path\must-never-be-shown"); }
            return new Lease(new FakeDirectory(call == 2 ? 1000 : 12, call == 2 ? 80 : 20, call == 3));
        }
    }
    private sealed class Lease(FakeDirectory root) : IScanScopeLease
    {
        public ScanScope Scope => new("Fake Temp", "fake-volume");
        public IScanDirectory Root => root;
        public void Validate() { }
        public void Dispose() => root.Dispose();
    }
    private sealed class FakeDirectory(int count, int delay, bool unsafeEntry) : IScanDirectory
    {
        private int current;
        public ScanEntry Identity => new("root", 1, 0, 1, 0x10);
        public void Validate() { }
        public ScanEntry? ReadNext()
        {
            Thread.Sleep(delay);
            return current++ < count ? new("file" + current, (ulong)current + 1, 4096, 1,
                unsafeEntry && current == 1 ? 0x400u : 0x80u) : null;
        }
        public IScanDirectory OpenDirectory(ScanEntry entry) => throw new IOException();
        public ScanEntry ReadFileMetadata(ScanEntry entry) => entry;
        public void Dispose() { }
    }
}
