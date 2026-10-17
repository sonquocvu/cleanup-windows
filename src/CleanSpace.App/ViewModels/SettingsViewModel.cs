using CleanSpace.Application.UseCases;
using CleanSpace.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CleanSpace.App.ViewModels;

public partial class SettingsViewModel(ThemeSettingsService settings) : ObservableObject
{
    public IReadOnlyList<ThemePreference> ThemeOptions { get; } = Enum.GetValues<ThemePreference>();

    [ObservableProperty]
    public partial ThemePreference SelectedTheme { get; set; } = ThemePreference.System;

    [ObservableProperty]
    public partial ThemePreference CurrentTheme { get; private set; } = ThemePreference.System;

    [ObservableProperty]
    public partial string StatusMessage { get; private set; } = "Appearance is saved locally on this device.";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    [NotifyCanExecuteChangedFor(nameof(SaveThemeCommand))]
    public partial bool IsBusy { get; private set; } = true;

    public bool CanEdit => !IsBusy;

    public async Task InitializeAsync()
    {
        var result = await settings.LoadAsync();
        CurrentTheme = SelectedTheme = result.Settings.Theme;
        StatusMessage = result.Notice ?? "Appearance is saved locally on this device.";
        IsBusy = false;
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task SaveThemeAsync(CancellationToken cancellationToken)
    {
        IsBusy = true;
        try
        {
            var result = await settings.SaveAsync(SelectedTheme, cancellationToken);
            CurrentTheme = SelectedTheme = result.Settings.Theme;
            StatusMessage = result.Notice ?? "Appearance saved.";
        }
        finally
        {
            IsBusy = false;
        }
    }
}
