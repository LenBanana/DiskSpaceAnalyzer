using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskSpaceAnalyzer.Models.Robocopy;
using DiskSpaceAnalyzer.Models.FileCopy;

namespace DiskSpaceAnalyzer.ViewModels;

/// <summary>
/// ViewModel for the Failed Verification Dialog.
/// Displays detailed information about files that failed integrity verification.
/// </summary>
public partial class FailedVerificationViewModel : ObservableObject
{
    [ObservableProperty] private string _title = "Verification Failures";
    [ObservableProperty] private string _searchText = string.Empty;
    
    public ObservableCollection<VerificationFailureInfo> Failures { get; } = new();
    public ObservableCollection<VerificationFailureInfo> FilteredFailures { get; } = new();
    
    private readonly RobocopyResult _result;
    
    public int TotalFailed => Failures.Count;
    public string MethodName => Failures.FirstOrDefault()?.Method.ToString() ?? "Unknown";
    
    public FailedVerificationViewModel(RobocopyResult result)
    {
        _result = result;
        
        // Load failures
        foreach (var failure in result.FailedVerificationDetails)
        {
            Failures.Add(failure);
            FilteredFailures.Add(failure);
        }
        
        Title = $"Verification Failures ({TotalFailed} files)";
    }
    
    partial void OnSearchTextChanged(string value)
    {
        FilterFailures();
    }
    
    private void FilterFailures()
    {
        FilteredFailures.Clear();
        
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            foreach (var failure in Failures)
                FilteredFailures.Add(failure);
        }
        else
        {
            var searchLower = SearchText.ToLowerInvariant();
            foreach (var failure in Failures)
            {
                if (failure.RelativePath.ToLowerInvariant().Contains(searchLower) ||
                    failure.ErrorMessage.ToLowerInvariant().Contains(searchLower))
                {
                    FilteredFailures.Add(failure);
                }
            }
        }
    }
    
    [RelayCommand]
    private void CopyToClipboard()
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Verification Failures Report");
            sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Total Failed: {TotalFailed}");
            sb.AppendLine($"Method: {MethodName}");
            sb.AppendLine();
            sb.AppendLine("Files:");
            sb.AppendLine(new string('-', 80));
            
            foreach (var failure in Failures)
            {
                sb.AppendLine($"Path: {failure.RelativePath}");
                sb.AppendLine($"Size: {failure.FileSizeFormatted}");
                sb.AppendLine($"Error: {failure.ErrorMessage}");
                
                if (!string.IsNullOrEmpty(failure.ExpectedHash) && !string.IsNullOrEmpty(failure.ActualHash))
                {
                    sb.AppendLine($"Expected Hash: {failure.ExpectedHash}");
                    sb.AppendLine($"Actual Hash: {failure.ActualHash}");
                }
                
                sb.AppendLine();
            }
            
            Clipboard.SetText(sb.ToString());
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to copy to clipboard: {ex.Message}", "Error", 
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    
    [RelayCommand]
    private void ExportToFile()
    {
        try
        {
            var defaultFileName = $"verification_failures_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "Text files (*.txt)|*.txt|CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                FileName = defaultFileName,
                DefaultExt = ".txt"
            };
            
            if (dialog.ShowDialog() == true)
            {
                var extension = Path.GetExtension(dialog.FileName).ToLowerInvariant();
                
                if (extension == ".csv")
                {
                    ExportToCsv(dialog.FileName);
                }
                else
                {
                    ExportToText(dialog.FileName);
                }
                
                MessageBox.Show($"Report exported successfully to:\n{dialog.FileName}", 
                    "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to export report: {ex.Message}", "Error", 
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    
    private void ExportToText(string filePath)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Verification Failures Report");
        sb.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Total Failed: {TotalFailed}");
        sb.AppendLine($"Method: {MethodName}");
        sb.AppendLine();
        sb.AppendLine("Files:");
        sb.AppendLine(new string('=', 100));
        
        foreach (var failure in Failures)
        {
            sb.AppendLine();
            sb.AppendLine($"File: {failure.RelativePath}");
            sb.AppendLine($"  Destination: {failure.DestinationPath}");
            sb.AppendLine($"  Size: {failure.FileSizeFormatted} ({failure.FileSize:N0} bytes)");
            sb.AppendLine($"  Error: {failure.ErrorMessage}");
            sb.AppendLine($"  Verification Method: {failure.Method}");
            sb.AppendLine($"  Attempts: {failure.AttemptCount}");
            sb.AppendLine($"  Verified At: {failure.VerifiedAt:yyyy-MM-dd HH:mm:ss}");
            
            if (!string.IsNullOrEmpty(failure.ExpectedHash) && !string.IsNullOrEmpty(failure.ActualHash))
            {
                sb.AppendLine($"  Expected Hash: {failure.ExpectedHash}");
                sb.AppendLine($"  Actual Hash: {failure.ActualHash}");
            }
            
            sb.AppendLine(new string('-', 100));
        }
        
        File.WriteAllText(filePath, sb.ToString());
    }
    
    private void ExportToCsv(string filePath)
    {
        var sb = new StringBuilder();
        
        // Header
        sb.AppendLine("Relative Path,Destination Path,Error Message,File Size (Bytes),File Size,Method,Attempts,Verified At,Expected Hash,Actual Hash");
        
        // Data rows
        foreach (var failure in Failures)
        {
            sb.AppendLine($"\"{EscapeCsv(failure.RelativePath)}\"," +
                         $"\"{EscapeCsv(failure.DestinationPath)}\"," +
                         $"\"{EscapeCsv(failure.ErrorMessage)}\"," +
                         $"{failure.FileSize}," +
                         $"\"{failure.FileSizeFormatted}\"," +
                         $"{failure.Method}," +
                         $"{failure.AttemptCount}," +
                         $"\"{failure.VerifiedAt:yyyy-MM-dd HH:mm:ss}\"," +
                         $"\"{EscapeCsv(failure.ExpectedHash ?? "")}\"," +
                         $"\"{EscapeCsv(failure.ActualHash ?? "")}\"");
        }
        
        File.WriteAllText(filePath, sb.ToString());
    }
    
    private string EscapeCsv(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        
        return value.Replace("\"", "\"\"");
    }
}
