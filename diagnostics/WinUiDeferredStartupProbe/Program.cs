using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace WinUiStartupProbe;

// Diagnostic-only executable: no CleanSpace assemblies, settings, DI, or application XAML.
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Console.Error.WriteLine("Probe: entry point");
        WinRT.ComWrappersSupport.InitializeComWrappers();
        Microsoft.UI.Xaml.Application.Start(initialization =>
        {
            Console.Error.WriteLine("Probe: application callback");
            SynchronizationContext.SetSynchronizationContext(
                new Microsoft.UI.Dispatching.DispatcherQueueSynchronizationContext(
                    Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()));
            _ = new ProbeApplication();
        });
    }
}

internal sealed class ProbeApplication : Microsoft.UI.Xaml.Application
{
    private Window? window;

    public ProbeApplication()
    {
        Console.Error.WriteLine("Probe: application constructed");
        UnhandledException += (_, args) => Console.Error.WriteLine(
            $"Probe: unhandled {args.Exception.GetType().Name}, HRESULT 0x{args.Exception.HResult:X8}");
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Console.Error.WriteLine("Probe: OnLaunched entered");
        if (Environment.GetCommandLineArgs().Contains("--controls"))
        {
            // Preserve the original receiver-before-argument evaluation order while
            // distinguishing resource access, construction, and insertion failures.
            Console.Error.WriteLine("Probe: before Application.Resources access");
            var resources = Resources;
            Console.Error.WriteLine("Probe: Application.Resources accessed");
            Console.Error.WriteLine("Probe: before MergedDictionaries access");
            var dictionaries = resources.MergedDictionaries;
            Console.Error.WriteLine("Probe: MergedDictionaries accessed");
            Console.Error.WriteLine("Probe: before XamlControlsResources construction");
            var controls = new XamlControlsResources();
            Console.Error.WriteLine("Probe: XamlControlsResources constructed");
            Console.Error.WriteLine("Probe: before controls resource insertion");
            dictionaries.Add(controls);
            Console.Error.WriteLine("Probe: controls resources added");
        }
        Console.Error.WriteLine("Probe: before Window construction");
        window = new Window { Title = "WinUI startup probe" };
        Console.Error.WriteLine("Probe: before TextBlock construction");
        window.Content = new TextBlock { Text = "Minimal WinUI startup probe" };
        Console.Error.WriteLine("Probe: before activation");
        window.Activate();
        Console.Error.WriteLine("Probe: activated");
    }
}

