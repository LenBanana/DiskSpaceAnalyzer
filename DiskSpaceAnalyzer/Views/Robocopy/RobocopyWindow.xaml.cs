using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using DiskSpaceAnalyzer.Services;
using DiskSpaceAnalyzer.ViewModels;

namespace DiskSpaceAnalyzer.Views.Robocopy;

/// <summary>
/// Interaction logic for RobocopyWindow.xaml (Wizard version)
/// </summary>
public partial class RobocopyWindow : Window
{
    private readonly FileCopyViewModel _viewModel;
    private readonly IDialogService _dialogService;
    private int _currentStep = 1;
    
    public RobocopyWindow(FileCopyViewModel viewModel, IDialogService dialogService)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _dialogService = dialogService;
        DataContext = viewModel;
        
        // Initialize wizard state
        UpdateWizardStep();
    }
    
    private void NextButton_Click(object sender, RoutedEventArgs e)
    {
        // Validate current step before moving forward
        if (!ValidateCurrentStep())
            return;
        
        if (_currentStep < 5)
        {
            _currentStep++;
            UpdateWizardStep();
        }
    }
    
    private void PreviousButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentStep > 1)
        {
            _currentStep--;
            UpdateWizardStep();
        }
    }
    
    private bool ValidateCurrentStep()
    {
        switch (_currentStep)
        {
            case 1: // Paths
                if (string.IsNullOrWhiteSpace(_viewModel.SourcePath))
                {
                    _dialogService.ShowWarning("Validation", 
                        "Please select a source folder before continuing.");
                    return false;
                }
                if (string.IsNullOrWhiteSpace(_viewModel.DestinationPath))
                {
                    _dialogService.ShowWarning("Validation", 
                        "Please select a destination folder before continuing.");
                    return false;
                }
                break;
        }
        
        return true;
    }
    
    private void UpdateWizardStep()
    {
        // Update step indicators
        UpdateStepIndicator(1, _currentStep >= 1, _currentStep > 1);
        UpdateStepIndicator(2, _currentStep >= 2, _currentStep > 2);
        UpdateStepIndicator(3, _currentStep >= 3, _currentStep > 3);
        UpdateStepIndicator(4, _currentStep >= 4, _currentStep > 4);
        UpdateStepIndicator(5, _currentStep >= 5, _currentStep > 5);
        
        // Update connecting lines
        Line1.Fill = new SolidColorBrush(_currentStep > 1 ? Color.FromRgb(0x41, 0x96, 0xF3) : Color.FromRgb(0x55, 0x55, 0x55));
        Line2.Fill = new SolidColorBrush(_currentStep > 2 ? Color.FromRgb(0x41, 0x96, 0xF3) : Color.FromRgb(0x55, 0x55, 0x55));
        Line3.Fill = new SolidColorBrush(_currentStep > 3 ? Color.FromRgb(0x41, 0x96, 0xF3) : Color.FromRgb(0x55, 0x55, 0x55));
        Line4.Fill = new SolidColorBrush(_currentStep > 4 ? Color.FromRgb(0x41, 0x96, 0xF3) : Color.FromRgb(0x55, 0x55, 0x55));
        
        // Show/Hide content panels
        Step1Content.Visibility = _currentStep == 1 ? Visibility.Visible : Visibility.Collapsed;
        Step2Content.Visibility = _currentStep == 2 ? Visibility.Visible : Visibility.Collapsed;
        Step3Content.Visibility = _currentStep == 3 ? Visibility.Visible : Visibility.Collapsed;
        Step4Content.Visibility = _currentStep == 4 ? Visibility.Visible : Visibility.Collapsed;
        Step5Content.Visibility = _currentStep == 5 ? Visibility.Visible : Visibility.Collapsed;
        
        // Update navigation buttons
        PreviousButton.Visibility = _currentStep > 1 ? Visibility.Visible : Visibility.Collapsed;
        
        if (_currentStep < 5)
        {
            NextButton.Visibility = Visibility.Visible;
            ActionButtons.Visibility = Visibility.Collapsed;
        }
        else
        {
            NextButton.Visibility = Visibility.Collapsed;
            ActionButtons.Visibility = Visibility.Visible;
        }
    }
    
    private void UpdateStepIndicator(int stepNumber, bool isActive, bool isCompleted)
    {
        // Find the step elements by name
        var circle = FindName($"Step{stepNumber}Circle") as FrameworkElement;
        var label = FindName($"Step{stepNumber}Label") as System.Windows.Controls.TextBlock;
        var circleChild = circle?.FindName("PART_Content") as System.Windows.Controls.TextBlock;
        
        if (circle == null || label == null) return;
        
        // Get the TextBlock inside the Border
        var textBlock = FindVisualChild<System.Windows.Controls.TextBlock>(circle);
        
        if (isCompleted)
        {
            // Completed: Green with checkmark
            circle.SetResourceReference(FrameworkElement.StyleProperty, "StepIndicatorCompleted");
            if (textBlock != null)
            {
                textBlock.Text = "✓";
                textBlock.Foreground = Brushes.White;
            }
            label.Foreground = new SolidColorBrush(Color.FromRgb(0x4C, 0xAF, 0x50));
            label.FontWeight = FontWeights.SemiBold;
        }
        else if (isActive)
        {
            // Active: Blue
            circle.SetResourceReference(FrameworkElement.StyleProperty, "StepIndicatorActive");
            if (textBlock != null)
            {
                textBlock.Text = stepNumber.ToString();
                textBlock.Foreground = Brushes.White;
            }
            label.Foreground = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0));
            label.FontWeight = FontWeights.SemiBold;
        }
        else
        {
            // Inactive: Gray
            circle.SetResourceReference(FrameworkElement.StyleProperty, "StepIndicatorInactive");
            if (textBlock != null)
            {
                textBlock.Text = stepNumber.ToString();
                textBlock.Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
            }
            label.Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
            label.FontWeight = FontWeights.Normal;
        }
    }
    
    // Helper method to find child elements in the visual tree
    private T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            
            if (child is T typedChild)
                return typedChild;
            
            var result = FindVisualChild<T>(child);
            if (result != null)
                return result;
        }
        
        return null;
    }
    
    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        
        // Close the log window if it's open
        _viewModel.CloseLogWindow();
    }
}
