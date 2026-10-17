using CleanSpace.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CleanSpace.App.Views;

public sealed partial class SettingsView : UserControl
{
    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel), typeof(SettingsViewModel), typeof(SettingsView), new PropertyMetadata(null));

    public SettingsViewModel ViewModel
    {
        get => (SettingsViewModel)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public SettingsView() => InitializeComponent();
}
