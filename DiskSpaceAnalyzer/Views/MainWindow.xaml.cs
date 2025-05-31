using System.Windows;
using System.Windows.Input;
using DiskSpaceAnalyzer.ViewModels;

namespace DiskSpaceAnalyzer.Views;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void TreeMapControl_CurrentRootChanged(object sender, DirectoryItemViewModel currentRoot)
    {
        if (DataContext is MainViewModel viewModel) viewModel.UpdateSelectedDirectory(currentRoot);
    }
}