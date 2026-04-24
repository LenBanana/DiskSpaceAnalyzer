using System.Windows;
using System.Windows.Media.Animation;
using DiskSpaceAnalyzer.ViewModels;

namespace DiskSpaceAnalyzer.Views;

public partial class ModernDialog : Window
{
    private readonly ModernDialogViewModel _viewModel;

    public ModernDialog(ModernDialogViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;

        Loaded += OnLoaded;

        // Set default button focus
        if (_viewModel.ShowInput)
        {
            InputTextBox.Focus();
            InputTextBox.SelectAll();
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Play entrance animation
        var storyboard = (Storyboard)FindResource("ShowAnimation");
        storyboard.Begin(RootBorder);
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.Result = Models.DialogResult.OK;
        DialogResult = true;
        Close();
    }

    private void YesButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.Result = Models.DialogResult.Yes;
        DialogResult = true;
        Close();
    }

    private void NoButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.Result = Models.DialogResult.No;
        DialogResult = false;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.Result = Models.DialogResult.Cancel;
        DialogResult = false;
        Close();
    }
}