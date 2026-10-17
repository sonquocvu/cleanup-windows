using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;

namespace WinUiXamlLifecycleProbe;

// Diagnostic only. Main, InitializeComponent and metadata are XAML compiler generated.
public partial class App : Microsoft.UI.Xaml.Application
{
    private MainWindow? window;

    public App()
    {
        Console.Error.WriteLine("Lifecycle: Application constructor entered");
        UnhandledException += (_, args) => Console.Error.WriteLine(
            $"Lifecycle: unhandled {args.Exception.GetType().Name}, HRESULT 0x{args.Exception.HResult:X8}");
        Console.Error.WriteLine($"Lifecycle: generated metadata interface = {this is IXamlMetadataProvider}");
        Console.Error.WriteLine("Lifecycle: before InitializeComponent");
        InitializeComponent();
        Console.Error.WriteLine("Lifecycle: InitializeComponent returned");
        if (Environment.GetCommandLineArgs().Contains("--constructor-resources"))
        {
            CheckResources("constructor after InitializeComponent");
        }
        Console.Error.WriteLine("Lifecycle: Application constructor completed");
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Console.Error.WriteLine("Lifecycle: OnLaunched entered");
        CheckResources("OnLaunched");
        Console.Error.WriteLine("Lifecycle: before MainWindow construction");
        window = new MainWindow();
        Console.Error.WriteLine("Lifecycle: MainWindow constructed");
        window.Closed += (_, _) => Console.Error.WriteLine("Lifecycle: window closed normally");
        Console.Error.WriteLine("Lifecycle: before activation");
        window.Activate();
        Console.Error.WriteLine("Lifecycle: activated");
    }

    private void CheckResources(string stage)
    {
        Console.Error.WriteLine($"Lifecycle: before Application.Resources access ({stage})");
        var resources = Resources;
        Console.Error.WriteLine($"Lifecycle: Application.Resources accessed ({stage})");
        var dictionaries = resources.MergedDictionaries;
        Console.Error.WriteLine($"Lifecycle: XamlControlsResources present = {dictionaries.OfType<XamlControlsResources>().Any()}");
    }
}
