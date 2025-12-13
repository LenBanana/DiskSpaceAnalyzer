using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskSpaceAnalyzer.Models.Robocopy;
using DiskSpaceAnalyzer.Services;
using DiskSpaceAnalyzer.Services.Robocopy;

namespace DiskSpaceAnalyzer.ViewModels;

/// <summary>
/// ViewModel for the Robocopy window.
/// Manages UI state, commands, and coordinates with RobocopyService.
/// </summary>
public partial class RobocopyViewModel : BaseViewModel
{
    private readonly IRobocopyService _robocopyService;
    private readonly IDialogService _dialogService;
    private CancellationTokenSource? _cancellationTokenSource;
    
    [ObservableProperty] private string _sourcePath = string.Empty;
    [ObservableProperty] private string _destinationPath = string.Empty;
    
    [ObservableProperty] private RobocopyPreset _selectedPreset = RobocopyPreset.Copy;
    
    // Options
    [ObservableProperty] private bool _copySubdirectories = true;
    [ObservableProperty] private bool _mirrorMode = false;
    [ObservableProperty] private bool _useMultithreading = true;
    [ObservableProperty] private int _threadCount = 8;
    [ObservableProperty] private bool _backupMode = false;
    [ObservableProperty] private bool _copySecurity = false;
    
    // State
    [ObservableProperty] private RobocopyJobState _currentState = RobocopyJobState.Ready;
    [ObservableProperty] private string _statusMessage = "Ready to copy";
    [ObservableProperty] private bool _isRunning = false;
    [ObservableProperty] private bool _isPaused = false;
    [ObservableProperty] private bool _canStart = true;
    
    // Progress
    [ObservableProperty] private double _progressPercentage = 0;
    [ObservableProperty] private string _currentFile = string.Empty;
    [ObservableProperty] private long _filesCopied = 0;
    [ObservableProperty] private long _totalFiles = 0;
    [ObservableProperty] private long _bytesCopied = 0;
    [ObservableProperty] private long _totalBytes = 0;
    [ObservableProperty] private double _transferSpeedMBps = 0;
    [ObservableProperty] private string _estimatedTimeRemaining = string.Empty;
    [ObservableProperty] private string _elapsedTime = string.Empty;
    
    // Results
    [ObservableProperty] private RobocopyResult? _result;
    [ObservableProperty] private string _logFilePath = string.Empty;
    
    // Errors
    public ObservableCollection<RobocopyError> Errors { get; } = new();
    [ObservableProperty] private bool _hasErrors = false;
    [ObservableProperty] private int _errorCount = 0;
    
    // Log viewer
    [ObservableProperty] private bool _isLogVisible = true;
    [ObservableProperty] private string _logOutput = string.Empty;
    
    // Command preview
    public string CommandPreview
    {
        get
        {
            try
            {
                if (string.IsNullOrWhiteSpace(SourcePath) || string.IsNullOrWhiteSpace(DestinationPath))
                    return "Select source and destination to preview command...";
                
                var options = BuildOptions();
                return _robocopyService.BuildCommandLine(options);
            }
            catch
            {
                return "Unable to build command preview";
            }
        }
    }
    
    public RobocopyViewModel(
        IRobocopyService robocopyService,
        IDialogService dialogService)
    {
        _robocopyService = robocopyService;
        _dialogService = dialogService;
        
        // Validate robocopy availability on startup
        if (!_robocopyService.IsRobocopyAvailable())
        {
            StatusMessage = "ERROR: Robocopy not found on this system!";
            CanStart = false;
        }
    }
    
    // Notify command preview updates when any option changes
    partial void OnSourcePathChanged(string value) => OnPropertyChanged(nameof(CommandPreview));
    partial void OnDestinationPathChanged(string value) => OnPropertyChanged(nameof(CommandPreview));
    partial void OnCopySubdirectoriesChanged(bool value) => OnPropertyChanged(nameof(CommandPreview));
    partial void OnMirrorModeChanged(bool value) => OnPropertyChanged(nameof(CommandPreview));
    partial void OnUseMultithreadingChanged(bool value) => OnPropertyChanged(nameof(CommandPreview));
    partial void OnThreadCountChanged(int value) => OnPropertyChanged(nameof(CommandPreview));
    partial void OnBackupModeChanged(bool value) => OnPropertyChanged(nameof(CommandPreview));
    partial void OnCopySecurityChanged(bool value) => OnPropertyChanged(nameof(CommandPreview));
    partial void OnSelectedPresetChanged(RobocopyPreset value)
    {
        // Update options based on preset
        var options = RobocopyOptions.FromPreset(value);
        
        CopySubdirectories = options.CopySubdirectories;
        MirrorMode = options.MirrorMode;
        UseMultithreading = options.UseMultithreading;
        BackupMode = options.BackupMode;
        CopySecurity = options.CopySecurity;
        ThreadCount = options.ThreadCount;
        
        OnPropertyChanged(nameof(CommandPreview));
    }
    
    [RelayCommand]
    private void BrowseSource()
    {
        var path = _dialogService.SelectFolder("Select source folder");
        if (!string.IsNullOrEmpty(path))
            SourcePath = path;
    }
    
    [RelayCommand]
    private void BrowseDestination()
    {
        var path = _dialogService.SelectFolder("Select destination folder");
        if (!string.IsNullOrEmpty(path))
            DestinationPath = path;
    }
    
    [RelayCommand]
    private async Task StartAsync()
    {
        // Validate paths
        if (string.IsNullOrWhiteSpace(SourcePath))
        {
            MessageBox.Show("Please select a source folder.", "Validation Error", 
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        
        if (string.IsNullOrWhiteSpace(DestinationPath))
        {
            MessageBox.Show("Please select a destination folder.", "Validation Error", 
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        
        // Build options
        var options = BuildOptions();
        
        // Validate options
        var (isValid, errorMessage) = _robocopyService.ValidateOptions(options);
        if (!isValid)
        {
            MessageBox.Show($"Validation failed: {errorMessage}", "Validation Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }
        
        // Warn about mirror mode
        if (MirrorMode)
        {
            var result = MessageBox.Show(
                "WARNING: Mirror mode will DELETE files at the destination that don't exist at the source!\n\n" +
                "Are you sure you want to continue?",
                "Mirror Mode Warning",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            
            if (result != MessageBoxResult.Yes)
                return;
        }
        
        // Start copy operation
        await ExecuteCopyAsync(options);
    }
    
    [RelayCommand(CanExecute = nameof(CanPause))]
    private void Pause()
    {
        _robocopyService.Pause();
        IsPaused = true;
        StatusMessage = "Paused";
    }
    
    private bool CanPause() => IsRunning && !IsPaused;
    
    [RelayCommand(CanExecute = nameof(CanResume))]
    private void Resume()
    {
        _robocopyService.Resume();
        IsPaused = false;
        StatusMessage = "Copying files...";
    }
    
    private bool CanResume() => IsRunning && IsPaused;
    
    [RelayCommand(CanExecute = nameof(IsRunning))]
    private void Cancel()
    {
        _cancellationTokenSource?.Cancel();
        StatusMessage = "Cancelling...";
    }
    
    [RelayCommand]
    private void ViewLog()
    {
        IsLogVisible = !IsLogVisible;
        
        if (IsLogVisible)
        {
            // Get current output from service
            LogOutput = _robocopyService.GetCurrentOutput(250);
        }
    }
    
    [RelayCommand]
    private void CopyCommand()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(CommandPreview) && 
                CommandPreview != "Select source and destination to preview command...")
            {
                Clipboard.SetText(CommandPreview);
                StatusMessage = "Command copied to clipboard!";
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to copy to clipboard: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    
    [RelayCommand]
    private void PresetChanged()
    {
        // Update options based on preset
        var options = RobocopyOptions.FromPreset(SelectedPreset);
        
        CopySubdirectories = options.CopySubdirectories;
        MirrorMode = options.MirrorMode;
        UseMultithreading = options.UseMultithreading;
        BackupMode = options.BackupMode;
        CopySecurity = options.CopySecurity;
        ThreadCount = options.ThreadCount;
    }
    
    [RelayCommand]
    private void OpenSourceInExplorer()
    {
        if (Directory.Exists(SourcePath))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = SourcePath,
                UseShellExecute = true
            });
        }
    }
    
    [RelayCommand]
    private void OpenDestinationInExplorer()
    {
        if (Directory.Exists(DestinationPath))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = DestinationPath,
                UseShellExecute = true
            });
        }
    }
    
    private RobocopyOptions BuildOptions()
    {
        return new RobocopyOptions
        {
            SourcePath = SourcePath,
            DestinationPath = DestinationPath,
            Preset = SelectedPreset,
            CopySubdirectories = CopySubdirectories,
            MirrorMode = MirrorMode,
            UseMultithreading = UseMultithreading,
            ThreadCount = ThreadCount,
            BackupMode = BackupMode,
            CopySecurity = CopySecurity,
            RetryCount = 3,
            RetryWaitSeconds = 5
        };
    }
    
    private async Task ExecuteCopyAsync(RobocopyOptions options)
    {
        _cancellationTokenSource = new CancellationTokenSource();
        
        // Reset state
        Errors.Clear();
        ErrorCount = 0;
        HasErrors = false;
        ProgressPercentage = 0;
        FilesCopied = 0;
        BytesCopied = 0;
        CurrentFile = string.Empty;
        
        IsRunning = true;
        IsPaused = false;
        CanStart = false;
        CurrentState = RobocopyJobState.Scanning;
        StatusMessage = "Starting...";
        
        // Update command states
        PauseCommand.NotifyCanExecuteChanged();
        ResumeCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        
        try
        {
            var progress = new Progress<RobocopyProgress>(OnProgressUpdated);
            
            Result = await _robocopyService.CopyAsync(
                options,
                progress,
                _cancellationTokenSource.Token);
            
            // Update UI with final results
            CurrentState = Result.State;
            StatusMessage = Result.ExitCodeMessage;
            LogFilePath = Result.LogFilePath;
            
            // Add errors to collection
            foreach (var error in Result.Errors)
            {
                Errors.Add(error);
            }
            
            ErrorCount = Errors.Count;
            HasErrors = Errors.Count > 0;
            
            // Show completion message
            var message = Result.GetSummary();
            var icon = Result.Success ? MessageBoxImage.Information : MessageBoxImage.Warning;
            MessageBox.Show(message, "Robocopy Completed", MessageBoxButton.OK, icon);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Copy operation failed: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
            
            StatusMessage = $"Failed: {ex.Message}";
            CurrentState = RobocopyJobState.Failed;
        }
        finally
        {
            IsRunning = false;
            IsPaused = false;
            CanStart = true;
            
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
            
            // Update command states
            PauseCommand.NotifyCanExecuteChanged();
            ResumeCommand.NotifyCanExecuteChanged();
            CancelCommand.NotifyCanExecuteChanged();
        }
    }
    
    private void OnProgressUpdated(RobocopyProgress progress)
    {
        // Update all progress properties
        CurrentState = progress.State;
        StatusMessage = progress.StatusMessage;
        CurrentFile = progress.CurrentFile;
        FilesCopied = progress.FilesCopied;
        TotalFiles = progress.TotalFiles;
        BytesCopied = progress.BytesCopied;
        TotalBytes = progress.TotalBytes;
        ProgressPercentage = progress.PercentComplete;
        TransferSpeedMBps = progress.MegabytesPerSecond;
        ErrorCount = progress.ErrorCount;
        HasErrors = progress.ErrorCount > 0;
        
        // Format time strings
        ElapsedTime = FormatTimeSpan(progress.Elapsed);
        EstimatedTimeRemaining = FormatTimeSpan(progress.EstimatedTimeRemaining);
        
        // Update paused state
        if (progress.State == RobocopyJobState.Paused && !IsPaused)
        {
            IsPaused = true;
            PauseCommand.NotifyCanExecuteChanged();
            ResumeCommand.NotifyCanExecuteChanged();
        }
        else if (progress.State != RobocopyJobState.Paused && IsPaused)
        {
            IsPaused = false;
            PauseCommand.NotifyCanExecuteChanged();
            ResumeCommand.NotifyCanExecuteChanged();
        }
        
        // Update log output if visible (force new string instance to trigger binding update)
        if (IsLogVisible)
        {
            var newOutput = _robocopyService.GetCurrentOutput(250);
            if (newOutput != LogOutput)
            {
                LogOutput = newOutput;
            }
        }
    }
    
    private string FormatTimeSpan(TimeSpan time)
    {
        if (time.TotalHours >= 1)
            return $"{(int)time.TotalHours}h {time.Minutes}m {time.Seconds}s";
        if (time.TotalMinutes >= 1)
            return $"{time.Minutes}m {time.Seconds}s";
        
        return $"{time.Seconds}s";
    }
    
    public string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:F2} {sizes[order]}";
    }
}
