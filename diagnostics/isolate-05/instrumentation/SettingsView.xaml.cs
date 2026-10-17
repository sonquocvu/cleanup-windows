using CleanSpace.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CleanSpace.App.Views;

public sealed partial class SettingsView : UserControl
{
    public static readonly DependencyProperty ViewModelProperty = RegisterViewModelProperty();

    private static DependencyProperty RegisterViewModelProperty()
    {
        StartupTrace.Mark("SettingsView DependencyProperty.Register entry");
        try
        {
            var result = DependencyProperty.Register(nameof(ViewModel), typeof(SettingsViewModel), typeof(SettingsView), new PropertyMetadata(null));
            StartupTrace.Mark("SettingsView DependencyProperty.Register returned");
            return result;
        }
        catch (Exception exception)
        {
            StartupTrace.Failure("SettingsView DependencyProperty.Register", exception);
            throw;
        }
    }

    public SettingsViewModel ViewModel
    {
        get => (SettingsViewModel)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public SettingsView()
    {
        StartupTrace.Mark("SettingsView constructor entry");
        try
        {
            StartupTrace.Mark("SettingsView.InitializeComponent entry");
            InitializeComponent();
            StartupTrace.Mark("SettingsView.InitializeComponent returned");
        }
        catch (Exception exception)
        {
            StartupTrace.Failure("SettingsView constructor", exception);
            throw;
        }
    }
}
