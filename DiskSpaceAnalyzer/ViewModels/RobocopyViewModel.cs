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
    private readonly IExclusionPresetService _exclusionPresetService;
    private readonly IGitIgnoreParserService _gitIgnoreParserService;
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
    
    // Exclusions
    [ObservableProperty] private ExclusionPreset? _selectedExclusionPreset;
    [ObservableProperty] private string _newExclusionText = string.Empty;
    [ObservableProperty] private bool _isExcludingFolder = true;
    public ObservableCollection<string> ExcludedDirectories { get; } = new();
    public ObservableCollection<string> ExcludedFiles { get; } = new();
    public ObservableCollection<ExclusionPreset> ExclusionPresets { get; } = new();
    
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
    [ObservableProperty] private int _logLineCount = 0;
    [ObservableProperty] private bool _isLogTruncated = false;
    
    // Log window management
    private Window? _logWindow;
    
    // Line limiting for performance
    private const int MaxDisplayLines = 1000;
    private readonly System.Collections.Generic.Queue<string> _logLines = new();
    
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
        IDialogService dialogService,
        IExclusionPresetService exclusionPresetService,
        IGitIgnoreParserService gitIgnoreParserService)
    {
        _robocopyService = robocopyService;
        _dialogService = dialogService;
        _exclusionPresetService = exclusionPresetService;
        _gitIgnoreParserService = gitIgnoreParserService;
        
        // Load exclusion presets
        LoadExclusionPresets();
        
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
    
    // Notify when exclusions change
    partial void OnSelectedExclusionPresetChanged(ExclusionPreset? value)
    {
        if (value != null && value.Id != "custom")
        {
            // Load preset exclusions
            ExcludedDirectories.Clear();
            ExcludedFiles.Clear();
            
            foreach (var dir in value.ExcludedDirectories)
                ExcludedDirectories.Add(dir);
            
            foreach (var file in value.ExcludedFiles)
                ExcludedFiles.Add(file);
        }
        
        OnPropertyChanged(nameof(CommandPreview));
    }
    
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
            await _dialogService.ShowWarningAsync("Validation Error", "Please select a source folder.");
            return;
        }
        
        if (string.IsNullOrWhiteSpace(DestinationPath))
        {
            await _dialogService.ShowWarningAsync("Validation Error", "Please select a destination folder.");
            return;
        }
        
        // Build options
        var options = BuildOptions();
        
        // Validate options
        var (isValid, errorMessage) = _robocopyService.ValidateOptions(options);
        if (!isValid)
        {
            await _dialogService.ShowErrorAsync("Validation Error", $"Validation failed: {errorMessage}");
            return;
        }
        
        // Warn about mirror mode
        if (MirrorMode)
        {
            var confirmed = await _dialogService.ShowConfirmationAsync(
                "Mirror Mode Warning",
                "WARNING: Mirror mode will DELETE files at the destination that don't exist at the source!\n\n" +
                "Are you sure you want to continue?");
            
            if (!confirmed)
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
    private void OpenLogWindow()
    {
        // Create log window if it doesn't exist
        if (_logWindow == null)
        {
            _logWindow = new Views.Robocopy.RobocopyLogWindow(this, _dialogService);
            
            // Clean up reference when window is closed
            _logWindow.Closed += (s, e) => _logWindow = null;
        }
        
        // Show or activate the window
        if (_logWindow.IsVisible)
        {
            _logWindow.Activate();
        }
        else
        {
            _logWindow.Show();
        }
    }
    
    [RelayCommand]
    private void OpenLogFile()
    {
        if (string.IsNullOrWhiteSpace(LogFilePath))
        {
            _dialogService.ShowInfo("No Log File", 
                "No log file has been created yet. Start the copy operation first.");
            return;
        }
        
        if (!File.Exists(LogFilePath))
        {
            _dialogService.ShowWarning("File Not Found",
                $"Log file not found at: {LogFilePath}");
            return;
        }
        
        try
        {
            // Open with default text editor
            Process.Start(new ProcessStartInfo
            {
                FileName = LogFilePath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _dialogService.ShowError("Error",
                $"Failed to open log file: {ex.Message}\n\nPath: {LogFilePath}");
        }
    }
    
    /// <summary>
    /// Closes the log window if it's open. Called when the main wizard closes.
    /// </summary>
    public void CloseLogWindow()
    {
        if (_logWindow != null)
        {
            // Unsubscribe from Closed event to prevent memory leak
            _logWindow.Closed -= (s, e) => _logWindow = null;
            
            // Force close (bypass the OnClosing override that hides the window)
            _logWindow.Closing -= null; // Clear any event handlers
            _logWindow.Close();
            _logWindow = null;
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
            _dialogService.ShowError("Error", $"Failed to copy to clipboard: {ex.Message}");
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
            RetryWaitSeconds = 5,
            ExcludeDirectories = ExcludedDirectories.ToList(),
            ExcludeFiles = ExcludedFiles.ToList()
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
            if (Result.Success)
                _dialogService.ShowSuccess("Robocopy Completed", message);
            else
                _dialogService.ShowWarning("Robocopy Completed", message);
        }
        catch (Exception ex)
        {
            _dialogService.ShowError("Error", $"Copy operation failed: {ex.Message}");
            
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
        
        // Update log output with line limiting for performance
        var newOutput = _robocopyService.GetCurrentOutput(250);
        if (!string.IsNullOrEmpty(newOutput) && newOutput != LogOutput)
        {
            UpdateLogOutput(newOutput);
        }
    }
    
    /// <summary>
    /// Updates the log output with line limiting for performance.
    /// Keeps only the last MaxDisplayLines lines to prevent UI slowdown.
    /// </summary>
    private void UpdateLogOutput(string newOutput)
    {
        if (string.IsNullOrEmpty(newOutput))
            return;
        
        // Split into lines
        var lines = newOutput.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        
        // Clear queue if this is a fresh start (output is short)
        if (lines.Length < _logLines.Count / 2)
        {
            _logLines.Clear();
        }
        
        // Add new lines to queue
        foreach (var line in lines)
        {
            _logLines.Enqueue(line);
            
            // Remove oldest lines if we exceed the limit
            while (_logLines.Count > MaxDisplayLines)
            {
                _logLines.Dequeue();
                IsLogTruncated = true;
            }
        }
        
        // Update line count
        LogLineCount = _logLines.Count;
        
        // Rebuild output string from queue
        LogOutput = string.Join(Environment.NewLine, _logLines);
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
    
    // ===== Exclusion Commands =====
    
    private void LoadExclusionPresets()
    {
        ExclusionPresets.Clear();
        
        // Add "Custom" option first
        ExclusionPresets.Add(new ExclusionPreset
        {
            Id = "custom",
            Name = "Custom",
            Description = "Manually configure exclusions",
            IsBuiltIn = true
        });
        
        // Add all presets from service
        foreach (var preset in _exclusionPresetService.GetAllPresets())
        {
            ExclusionPresets.Add(preset);
        }
        
        // Select "None" by default
        SelectedExclusionPreset = ExclusionPresets.FirstOrDefault(p => p.Id == "none");
    }
    
    [RelayCommand]
    private void AddExclusion()
    {
        var text = NewExclusionText?.Trim();
        
        if (string.IsNullOrWhiteSpace(text))
            return;
        
        if (IsExcludingFolder)
        {
            // Add to excluded directories
            if (!ExcludedDirectories.Contains(text))
            {
                ExcludedDirectories.Add(text);
                
                // Switch to custom preset
                SwitchToCustomPreset();
                
                OnPropertyChanged(nameof(CommandPreview));
            }
        }
        else
        {
            // Add to excluded files
            if (!ExcludedFiles.Contains(text))
            {
                ExcludedFiles.Add(text);
                
                // Switch to custom preset
                SwitchToCustomPreset();
                
                OnPropertyChanged(nameof(CommandPreview));
            }
        }
        
        // Clear input
        NewExclusionText = string.Empty;
    }
    
    [RelayCommand]
    private void RemoveExcludedDirectory(string directory)
    {
        ExcludedDirectories.Remove(directory);
        SwitchToCustomPreset();
        OnPropertyChanged(nameof(CommandPreview));
    }
    
    [RelayCommand]
    private void RemoveExcludedFile(string file)
    {
        ExcludedFiles.Remove(file);
        SwitchToCustomPreset();
        OnPropertyChanged(nameof(CommandPreview));
    }
    
    [RelayCommand]
    private void SaveExclusionPreset()
    {
        // Prompt for preset name
        var name = Microsoft.VisualBasic.Interaction.InputBox(
            "Enter a name for this exclusion preset:",
            "Save Preset",
            "My Preset");
        
        if (string.IsNullOrWhiteSpace(name))
            return;
        
        // Check if name already exists
        if (_exclusionPresetService.PresetExists(name))
        {
            var overwrite = _dialogService.ShowQuestion(
                "Preset Exists",
                $"A preset named '{name}' already exists. Do you want to overwrite it?");
            
            if (!overwrite)
                return;
        }
        
        // Create and save preset
        var preset = new ExclusionPreset
        {
            Name = name,
            Description = $"User-created preset with {ExcludedDirectories.Count} folders and {ExcludedFiles.Count} files excluded",
            ExcludedDirectories = ExcludedDirectories.ToList(),
            ExcludedFiles = ExcludedFiles.ToList(),
            IsBuiltIn = false
        };
        
        try
        {
            _exclusionPresetService.SavePreset(preset);
            
            // Reload presets and select the new one
            LoadExclusionPresets();
            SelectedExclusionPreset = ExclusionPresets.FirstOrDefault(p => p.Name == name);
            
            _dialogService.ShowSuccess("Success", $"Preset '{name}' saved successfully!");
        }
        catch (Exception ex)
        {
            _dialogService.ShowError("Error", $"Failed to save preset: {ex.Message}");
        }
    }
    
    [RelayCommand]
    private void DeleteExclusionPreset()
    {
        if (SelectedExclusionPreset == null || SelectedExclusionPreset.IsBuiltIn)
        {
            _dialogService.ShowWarning("Error", "Cannot delete built-in presets.");
            return;
        }
        
        var confirmed = _dialogService.ShowConfirmation(
            "Confirm Delete",
            $"Are you sure you want to delete the preset '{SelectedExclusionPreset.Name}'?");
        
        if (!confirmed)
            return;
        
        try
        {
            _exclusionPresetService.DeletePreset(SelectedExclusionPreset.Id);
            
            // Reload presets and select "None"
            LoadExclusionPresets();
            SelectedExclusionPreset = ExclusionPresets.FirstOrDefault(p => p.Id == "none");
            
            _dialogService.ShowSuccess("Success", "Preset deleted successfully!");
        }
        catch (Exception ex)
        {
            _dialogService.ShowError("Error", $"Failed to delete preset: {ex.Message}");
        }
    }
    
    [RelayCommand]
    private void ImportGitignore()
    {
        // Open file dialog to select .gitignore file
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select .gitignore file",
            Filter = "Gitignore files (.gitignore)|.gitignore|All files (*.*)|*.*",
            CheckFileExists = true
        };
        
        if (dialog.ShowDialog() != true)
            return;
        
        try
        {
            // Parse the .gitignore file
            var (directories, files, unsupportedPatterns) = _gitIgnoreParserService.ParseGitIgnoreFile(dialog.FileName);
            
            // Track what was actually added
            int addedDirs = 0;
            int addedFiles = 0;
            
            // Add directories (skip duplicates)
            foreach (var dir in directories)
            {
                if (!ExcludedDirectories.Contains(dir))
                {
                    ExcludedDirectories.Add(dir);
                    addedDirs++;
                }
            }
            
            // Add files (skip duplicates)
            foreach (var file in files)
            {
                if (!ExcludedFiles.Contains(file))
                {
                    ExcludedFiles.Add(file);
                    addedFiles++;
                }
            }
            
            // Switch to custom preset if we added anything
            if (addedDirs > 0 || addedFiles > 0)
            {
                SwitchToCustomPreset();
                OnPropertyChanged(nameof(CommandPreview));
            }
            
            // Build summary message
            var message = $"Successfully imported {addedDirs} folder(s) and {addedFiles} file pattern(s) from .gitignore.";
            
            if (directories.Count + files.Count > addedDirs + addedFiles)
            {
                var skipped = (directories.Count - addedDirs) + (files.Count - addedFiles);
                message += $"\n\n{skipped} pattern(s) were skipped (already in exclusion list).";
            }
            
            if (unsupportedPatterns.Count > 0)
            {
                message += $"\n\nWarning: {unsupportedPatterns.Count} complex pattern(s) were skipped (not supported by Robocopy):";
                message += "\n" + string.Join("\n", unsupportedPatterns.Take(5));
                
                if (unsupportedPatterns.Count > 5)
                {
                    message += $"\n... and {unsupportedPatterns.Count - 5} more";
                }
            }
            
            // Show success message
            if (addedDirs > 0 || addedFiles > 0)
            {
                _dialogService.ShowSuccess("Import Complete", message);
            }
            else if (unsupportedPatterns.Count > 0)
            {
                _dialogService.ShowWarning("Import Complete", message);
            }
            else
            {
                _dialogService.ShowInfo("Import Complete", "No new exclusions were added. All patterns were already in the exclusion list.");
            }
        }
        catch (FileNotFoundException ex)
        {
            _dialogService.ShowError("File Not Found", ex.Message);
        }
        catch (Exception ex)
        {
            _dialogService.ShowError("Import Failed", $"Failed to import .gitignore file:\n{ex.Message}");
        }
    }
    
    private void SwitchToCustomPreset()
    {
        // Only switch if not already on custom
        if (SelectedExclusionPreset?.Id != "custom")
        {
            SelectedExclusionPreset = ExclusionPresets.FirstOrDefault(p => p.Id == "custom");
        }
    }
}
