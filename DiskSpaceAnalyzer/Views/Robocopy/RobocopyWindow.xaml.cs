using System;
using System.ComponentModel;
using System.Windows;
using DiskSpaceAnalyzer.ViewModels;

namespace DiskSpaceAnalyzer.Views.Robocopy;

/// <summary>
/// Interaction logic for RobocopyWindow.xaml
/// </summary>
public partial class RobocopyWindow : Window
{
    private readonly RobocopyViewModel _viewModel;
    
    public RobocopyWindow(RobocopyViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        
        // Subscribe to property changes to auto-scroll log
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
    }
    
    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RobocopyViewModel.LogOutput))
        {
            // Auto-scroll to bottom when log output changes
            LogScrollViewer.ScrollToBottom();
        }
    }
    
    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        // Unsubscribe from events
        _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
    }
}
