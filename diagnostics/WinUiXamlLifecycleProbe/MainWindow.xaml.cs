using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace WinUiXamlLifecycleProbe;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        Console.Error.WriteLine("Lifecycle: before MainWindow.InitializeComponent");
        InitializeComponent();
        Console.Error.WriteLine("Lifecycle: MainWindow.InitializeComponent returned; NavigationView constructed");
    }

    private void OnNavigationLoaded(object sender, RoutedEventArgs args)
    {
        var applied = Navigation.ApplyTemplate();
        Console.Error.WriteLine($"Lifecycle: NavigationView loaded; ApplyTemplate={applied}; TemplatePresent={Navigation.Template is not null}; VisualChildren={VisualTreeHelper.GetChildrenCount(Navigation)}");
    }
}
