using CleanSpace.App.ViewModels;
using CleanSpace.Application.Abstractions;
using CleanSpace.Application.DTOs;
using CleanSpace.Application.UseCases;
using CleanSpace.Core.Models;
using Microsoft.UI.Xaml;

namespace CleanSpace.App;

// Separate diagnostic executable, never referenced/shipped by the production solution.
// All views and ViewModels are linked from the production source, with fake ports only here.
public partial class App : Microsoft.UI.Xaml.Application
{
    private MainWindow? window;
    private DashboardViewModel? dashboard;

    public App() => InitializeComponent();

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        var settings = new SettingsViewModel(new(new MemoryThemeStore()));
        dashboard = new DashboardViewModel(new(new ScenarioProvider()));
        var scan = new ScanResultsViewModel(new(new UnavailableScanScope()));
        window = new MainWindow(new(settings, dashboard, scan)) { Title = "CleanSpace — Drive UI probe (fake data)" };
        window.Closed += (_, _) => scan.Dispose();
        window.Closed += (_, _) => dashboard.Dispose();
        window.Activate();
        await Task.WhenAll(settings.InitializeAsync(), dashboard.InitializeAsync());
    }

    private sealed class ScenarioProvider : IDriveInfoProvider
    {
        private int calls;
        public async Task<DriveInfoSnapshot> GetSystemDriveAsync(CancellationToken cancellationToken)
        {
            var call = ++calls;
            // Initial request and each Refresh remain pending for inspection; navigation must still work.
            await Task.Delay(TimeSpan.FromSeconds(12), cancellationToken);
            if (call == 1) { throw new IOException(@"private-path\must-never-be-shown"); }
            return call == 2
                ? new(@"Q:\", 1024L * 1024 * 1024 * 100, 1024L * 1024 * 1024 * 25,
                    1024L * 1024 * 1024 * 10, "NTFS", DateTimeOffset.UtcNow)
                : new(@"Q:\", null, null, null, null, DateTimeOffset.UtcNow);
        }
    }

    private sealed class UnavailableScanScope : IScanScopeProvider
    {
        public IScanScopeLease Open(CancellationToken cancellationToken) => throw new IOException("Scan unavailable in drive probe.");
    }

    private sealed class MemoryThemeStore : IThemeSettingsStore
    {
        private ThemeSettings settings = ThemeSettings.Default;
        public Task<ThemeSettings?> ReadAsync(CancellationToken cancellationToken) => Task.FromResult<ThemeSettings?>(settings);
        public Task WriteAsync(ThemeSettings value, CancellationToken cancellationToken)
        {
            settings = value;
            return Task.CompletedTask;
        }
    }
}
