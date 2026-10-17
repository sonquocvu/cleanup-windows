using System.ComponentModel;
using CleanSpace.App.ViewModels;
using CleanSpace.Core.Models;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CleanSpace.App;

public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }

    public MainWindow(MainViewModel viewModel)
    {
        StartupTrace.Mark("MainWindow constructor entry");
        ViewModel = viewModel;
        try
        {
            StartupTrace.Mark("MainWindow.InitializeComponent entry");
            InitializeComponent();
            StartupTrace.Mark("MainWindow.InitializeComponent returned");
            ViewModel.Settings.PropertyChanged += OnSettingsChanged;
            Closed += OnClosed;
            ApplyTheme();
            StartupTrace.Mark("MainWindow constructor returned");
        }
        catch (Exception exception)
        {
            StartupTrace.Failure("MainWindow constructor", exception);
            throw;
        }
    }

    private void OnNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        StartupTrace.Mark("Navigation.SelectionChanged entry");
        if (args.IsSettingsSelected)
        {
            ViewModel.SelectedPage = ShellPage.Settings;
        }
        else if (args.SelectedItem is NavigationViewItem { Tag: string tag }
                 && Enum.TryParse<ShellPage>(tag, out var page))
        {
            ViewModel.SelectedPage = page;
        }
        StartupTrace.Mark("Navigation.SelectionChanged returned");
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(SettingsViewModel.CurrentTheme)) { ApplyTheme(); }
    }

    private void ApplyTheme()
    {
        StartupTrace.Mark("ApplyTheme entry");
        ContentRoot.RequestedTheme = ViewModel.Settings.CurrentTheme switch
        {
            ThemePreference.Light => ElementTheme.Light,
            ThemePreference.Dark => ElementTheme.Dark,
            _ => ElementTheme.Default
        };
        StartupTrace.Mark("ApplyTheme returned");
    }

    private void OnClosed(object sender, WindowEventArgs args) =>
        ViewModel.Settings.PropertyChanged -= OnSettingsChanged;
}
