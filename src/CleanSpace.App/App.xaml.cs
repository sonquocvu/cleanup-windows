using CleanSpace.App.ViewModels;
using CleanSpace.Application.Abstractions;
using CleanSpace.Application.UseCases;
using CleanSpace.Infrastructure.Settings;
using CleanSpace.Infrastructure.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;

namespace CleanSpace.App;

public partial class App : Microsoft.UI.Xaml.Application
{
    private readonly ServiceProvider services;
    private MainWindow? window;

    public App()
    {
        InitializeComponent();
        var registrations = new ServiceCollection();
        // No providers, file logs, telemetry, hosted services, or background workers.
        registrations.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        registrations.AddSingleton<IThemeSettingsStore, JsonThemeSettingsStore>();
        registrations.AddSingleton<ThemeSettingsService>();
        registrations.AddSingleton<IDriveInfoProvider, WindowsDriveInfoProvider>();
        registrations.AddSingleton<DriveOverviewUseCase>();
        registrations.AddSingleton<DashboardViewModel>();
        registrations.AddSingleton<SettingsViewModel>();
        registrations.AddSingleton<MainViewModel>();
        registrations.AddSingleton<MainWindow>();
        services = registrations.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        window = services.GetRequiredService<MainWindow>();
        window.Closed += OnWindowClosed;
        window.Activate();
        await Task.WhenAll(services.GetRequiredService<SettingsViewModel>().InitializeAsync(),
            services.GetRequiredService<DashboardViewModel>().InitializeAsync());
    }

    private void OnWindowClosed(object sender, WindowEventArgs args) => services.Dispose();
}
