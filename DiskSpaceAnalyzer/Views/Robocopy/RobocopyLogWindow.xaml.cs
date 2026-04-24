using System;
using System.ComponentModel;
using System.Windows;
using DiskSpaceAnalyzer.Services;
using DiskSpaceAnalyzer.ViewModels;

namespace DiskSpaceAnalyzer.Views.Robocopy;

/// <summary>
///     Interaction logic for RobocopyLogWindow.xaml
///     A separate window that displays the robocopy output log in real-time.
/// </summary>
public partial class RobocopyLogWindow : Window
{
    private readonly IDialogService _dialogService;
    private readonly FileCopyViewModel _viewModel;

    public RobocopyLogWindow(FileCopyViewModel viewModel, IDialogService dialogService)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _dialogService = dialogService;
        DataContext = viewModel;

        // Subscribe to property changes for auto-scrolling
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;

        // Set initial auto-scroll state
        AutoScrollCheckBox.IsChecked = true;
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FileCopyViewModel.LogOutput))
            // Auto-scroll to bottom when log output changes (if enabled)
            if (AutoScrollCheckBox.IsChecked == true && LogTextBox != null)
                LogTextBox.ScrollToEnd();
    }

    private void CopyAllButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(_viewModel.LogOutput))
            {
                Clipboard.SetText(_viewModel.LogOutput);
                _dialogService.ShowSuccess("Success",
                    $"Copied {_viewModel.LogOutput.Length:N0} characters to clipboard.");
            }
            else
            {
                _dialogService.ShowInfo("Information",
                    "No log content to copy.");
            }
        }
        catch (Exception ex)
        {
            _dialogService.ShowError("Error",
                $"Failed to copy to clipboard: {ex.Message}");
        }
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);

        // Hide instead of close to allow reopening
        e.Cancel = true;
        Hide();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);

        // Unsubscribe from events
        _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
    }
}