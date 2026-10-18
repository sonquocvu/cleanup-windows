using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CleanSpace.App.ViewModels;

public enum ShellPage
{
    Dashboard,
    ScanResults,
    Settings
}

public partial class MainViewModel(SettingsViewModel settings, DashboardViewModel dashboard, ScanResultsViewModel scan) : ObservableObject
{
    public SettingsViewModel Settings { get; } = settings;
    public DashboardViewModel Dashboard { get; } = dashboard;
    public ScanResultsViewModel Scan { get; } = scan;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageTitle))]
    [NotifyPropertyChangedFor(nameof(PageDescription))]
    [NotifyPropertyChangedFor(nameof(IsDashboard))]
    [NotifyPropertyChangedFor(nameof(IsSettings))]
    [NotifyPropertyChangedFor(nameof(IsScanResults))]
    public partial ShellPage SelectedPage { get; set; }

    public bool IsDashboard => SelectedPage == ShellPage.Dashboard;
    public bool IsSettings => SelectedPage == ShellPage.Settings;
    public bool IsScanResults => SelectedPage == ShellPage.ScanResults;

    public bool CanQuickScan => Scan.IsScanAvailable;

    [RelayCommand(CanExecute = nameof(CanQuickScan))]
    private async Task QuickScanAsync()
    {
        if (!CanQuickScan) { return; }
        SelectedPage = ShellPage.ScanResults;
        if (Scan.CanStart) { await Scan.StartCommand.ExecuteAsync(null); }
    }

    public string PageTitle => SelectedPage switch
    {
        ShellPage.ScanResults => "Scan Results",
        ShellPage.Settings => "Settings",
        _ => "Dashboard"
    };

    public string PageDescription => SelectedPage switch
    {
        ShellPage.ScanResults => "Quick Scan is temporarily unavailable.",
        ShellPage.Settings => "Choose how CleanSpace looks on this device.",
        _ => "A read-only overview of the volume containing Windows."
    };
}
