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
        ViewModel = viewModel;
        InitializeComponent();
        ViewModel.Settings.PropertyChanged += OnSettingsChanged;
        Closed += OnClosed;
        ApplyTheme();
    }

    private void OnNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            ViewModel.SelectedPage = ShellPage.Settings;
        }
        else if (args.SelectedItem is NavigationViewItem { Tag: string tag }
                 && Enum.TryParse<ShellPage>(tag, out var page))
        {
            ViewModel.SelectedPage = page;
        }
    }

    private void OnSettingsChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(SettingsViewModel.CurrentTheme))
        {
            ApplyTheme();
        }
    }

    private void ApplyTheme() => ContentRoot.RequestedTheme = ViewModel.Settings.CurrentTheme switch
    {
        ThemePreference.Light => ElementTheme.Light,
        ThemePreference.Dark => ElementTheme.Dark,
        _ => ElementTheme.Default
    };

    private void OnClosed(object sender, WindowEventArgs args) =>
        ViewModel.Settings.PropertyChanged -= OnSettingsChanged;
}
