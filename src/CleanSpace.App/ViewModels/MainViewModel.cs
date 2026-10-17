using CommunityToolkit.Mvvm.ComponentModel;

namespace CleanSpace.App.ViewModels;

public enum ShellPage
{
    Dashboard,
    ScanResults,
    Settings
}

public partial class MainViewModel(SettingsViewModel settings) : ObservableObject
{
    public SettingsViewModel Settings { get; } = settings;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageTitle))]
    [NotifyPropertyChangedFor(nameof(PageDescription))]
    [NotifyPropertyChangedFor(nameof(IsDashboard))]
    [NotifyPropertyChangedFor(nameof(IsSettings))]
    [NotifyPropertyChangedFor(nameof(IsPlaceholder))]
    public partial ShellPage SelectedPage { get; set; }

    public bool IsDashboard => SelectedPage == ShellPage.Dashboard;
    public bool IsSettings => SelectedPage == ShellPage.Settings;
    public bool IsPlaceholder => !IsSettings;

    public string PageTitle => SelectedPage switch
    {
        ShellPage.ScanResults => "Scan Results",
        ShellPage.Settings => "Settings",
        _ => "Dashboard"
    };

    public string PageDescription => SelectedPage switch
    {
        ShellPage.ScanResults => "No scan results yet. Scanning is unavailable in this foundation release.",
        ShellPage.Settings => "Choose how CleanSpace looks on this device.",
        _ => "Drive information and scanning will become available in separately approved tasks."
    };
}
