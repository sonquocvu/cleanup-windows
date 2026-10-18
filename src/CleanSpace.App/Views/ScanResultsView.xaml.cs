using CleanSpace.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CleanSpace.App.Views;

public sealed partial class ScanResultsView : UserControl
{
    public static readonly DependencyProperty ViewModelProperty = DependencyProperty.Register(
        nameof(ViewModel), typeof(ScanResultsViewModel), typeof(ScanResultsView), new PropertyMetadata(null));
    public ScanResultsViewModel ViewModel
    {
        get => (ScanResultsViewModel)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }
    public ScanResultsView() => InitializeComponent();
}
