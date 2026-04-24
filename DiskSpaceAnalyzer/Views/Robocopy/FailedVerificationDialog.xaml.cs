using System.Windows;
using DiskSpaceAnalyzer.ViewModels;

namespace DiskSpaceAnalyzer.Views.Robocopy;

/// <summary>
///     Interaction logic for FailedVerificationDialog.xaml
/// </summary>
public partial class FailedVerificationDialog : Window
{
    public FailedVerificationDialog(FailedVerificationViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}