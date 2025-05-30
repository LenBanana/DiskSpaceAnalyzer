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

    private void ResetToRoot_Click(object sender, RoutedEventArgs e)
    {
        TreeMapControl.CurrentRoot = null;
    }

    private void NavigateUp_Click(object sender, RoutedEventArgs e)
    {
        var mouseEventArgs = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Right)
        {
            RoutedEvent = Mouse.MouseDownEvent
        };
        TreeMapControl.RaiseEvent(mouseEventArgs);
    }
}