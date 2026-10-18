using CleanSpace.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CleanSpace.App.Views;

public sealed partial class DashboardView : UserControl
{
    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel), typeof(DashboardViewModel), typeof(DashboardView), new PropertyMetadata(null));

    public DashboardViewModel ViewModel
    {
        get => (DashboardViewModel)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public DashboardView() => InitializeComponent();
}
