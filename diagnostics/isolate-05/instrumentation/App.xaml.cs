using CleanSpace.App.ViewModels;
using CleanSpace.Application.Abstractions;
using CleanSpace.Application.UseCases;
using CleanSpace.Infrastructure.Settings;
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
        StartupTrace.Mark("App constructor entry");
        UnhandledException += (_, args) => StartupTrace.Failure("WinUI UnhandledException", args.Exception);
        try
        {
            StartupTrace.Mark("App.InitializeComponent entry");
            InitializeComponent();
            StartupTrace.Mark("App.InitializeComponent returned");
            StartupTrace.Mark("DI setup entry");
            var registrations = new ServiceCollection();
            registrations.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
            registrations.AddSingleton<IThemeSettingsStore, JsonThemeSettingsStore>();
            registrations.AddSingleton<ThemeSettingsService>();
            registrations.AddSingleton<SettingsViewModel>();
            registrations.AddSingleton<MainViewModel>();
            registrations.AddSingleton<MainWindow>();
            services = registrations.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true
            });
            StartupTrace.Mark("DI setup returned");
            StartupTrace.Mark("App constructor returned");
        }
        catch (Exception exception)
        {
            StartupTrace.Failure("App constructor", exception);
            throw;
        }
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        StartupTrace.Mark("OnLaunched entry");
        try
        {
            StartupTrace.Mark("MainWindow resolution entry");
            window = services.GetRequiredService<MainWindow>();
            StartupTrace.Mark("MainWindow resolution returned");
            window.Closed += OnWindowClosed;
            StartupTrace.Mark("Window.Activate entry");
            window.Activate();
            StartupTrace.Mark("Window.Activate returned");
            StartupTrace.Mark("Settings.InitializeAsync entry");
            await services.GetRequiredService<SettingsViewModel>().InitializeAsync();
            StartupTrace.Mark("Settings.InitializeAsync returned");
        }
        catch (Exception exception)
        {
            StartupTrace.Failure("OnLaunched", exception);
            throw;
        }
    }

    private void OnWindowClosed(object sender, WindowEventArgs args)
    {
        StartupTrace.Mark("Window closed normally");
        services.Dispose();
    }
}
