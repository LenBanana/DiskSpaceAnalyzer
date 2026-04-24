using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskSpaceAnalyzer.Models.FileCopy;
using DiskSpaceAnalyzer.Models.Robocopy;
using DiskSpaceAnalyzer.Services;
using DiskSpaceAnalyzer.Services.FileCopy;
using DiskSpaceAnalyzer.Services.Robocopy;
using DiskSpaceAnalyzer.Views.Robocopy;
using Microsoft.VisualBasic;
using Microsoft.Win32;

namespace DiskSpaceAnalyzer.ViewModels;

/// <summary>
///     ViewModel for the File Copy window (formerly Robocopy-specific).
///     Manages UI state, commands, and coordinates with file copy engines via factory pattern.
///     Supports multiple copy engines: Robocopy and Native C#, with intelligent auto-selection.
/// </summary>
public partial class FileCopyViewModel : BaseViewModel
{
    #region Command Preview

    /// <summary>
    ///     Gets an engine-aware preview of the operation.
    ///     Shows command line for Robocopy, structured summary for Native.
    /// </summary>
    public string CommandPreview
    {
        get
        {
            try
            {
                if (string.IsNullOrWhiteSpace(SourcePath) || string.IsNullOrWhiteSpace(DestinationPath))
                    return "Select source and destination to preview operation...";

                var options = BuildFileCopyOptions();
                var engineToUse = DetermineActualEngine(options);

                return engineToUse == CopyEngineType.Robocopy
                    ? BuildRobocopyCommandPreview(options)
                    : BuildNativeOperationPreview(options);
            }
            catch
            {
                return "Unable to build operation preview";
            }
        }
    }

    #endregion

    #region Options Building

    private FileCopyOptions BuildFileCopyOptions()
    {
        return new FileCopyOptions
        {
            SourcePath = SourcePath,
            DestinationPath = DestinationPath,
            PreferredEngine = SelectedEngine,
            CopySubdirectories = CopySubdirectories,
            MirrorMode = MirrorMode,
            UseParallelCopy = UseMultithreading,
            ParallelismDegree = ThreadCount,
            BackupMode = BackupMode,
            CopySecurity = CopySecurity,
            RetryCount = 3,
            RetryWaitSeconds = 5,
            ExcludeDirectories = ExcludedDirectories.ToList(),
            ExcludeFiles = ExcludedFiles.ToList(),
            EnableIntegrityCheck = EnableIntegrityCheck,
            IntegrityCheckMethod = IntegrityCheckMethod
        };
    }

    #endregion

    #region Dependencies

    private readonly IFileCopyServiceFactory _factory;
    private readonly IDialogService _dialogService;
    private readonly IExclusionPresetService _exclusionPresetService;
    private readonly IGitIgnoreParserService _gitIgnoreParserService;
    private CancellationTokenSource? _cancellationTokenSource;

    #endregion

    #region Path Selection

    [ObservableProperty] private string _sourcePath = string.Empty;
    [ObservableProperty] private string _destinationPath = string.Empty;

    #endregion

    #region Engine Selection & Recommendation

    [ObservableProperty] private CopyEngineType _selectedEngine = CopyEngineType.Auto;
    [ObservableProperty] private string _engineRecommendationSummary = string.Empty;
    [ObservableProperty] private string _engineRecommendationDetails = string.Empty;
    [ObservableProperty] private bool _showRecommendationDetails;
    [ObservableProperty] private double _recommendationConfidence;
    [ObservableProperty] private string _expectedPerformance = string.Empty;
    [ObservableProperty] private string _scenarioSummary = string.Empty;
    [ObservableProperty] private bool _hasEngineWarnings;
    [ObservableProperty] private bool _isNativeEngineAvailable = true; // Always available
    [ObservableProperty] private bool _isRobocopyEngineAvailable;

    public ObservableCollection<CopyEngineType> AvailableEngines { get; } = [];
    public ObservableCollection<string> EngineWarnings { get; } = [];

    #endregion

    #region Copy Options

    [ObservableProperty] private RobocopyPreset _selectedPreset = RobocopyPreset.Copy;
    [ObservableProperty] private bool _copySubdirectories = true;
    [ObservableProperty] private bool _mirrorMode;
    [ObservableProperty] private bool _useMultithreading = true;
    [ObservableProperty] private int _threadCount = 8;
    [ObservableProperty] private bool _backupMode;
    [ObservableProperty] private bool _copySecurity;

    #endregion

    #region Exclusions

    [ObservableProperty] private ExclusionPreset? _selectedExclusionPreset;
    [ObservableProperty] private string _newExclusionText = string.Empty;
    [ObservableProperty] private bool _isExcludingFolder = true;

    public ObservableCollection<string> ExcludedDirectories { get; } = [];
    public ObservableCollection<string> ExcludedFiles { get; } = [];
    public ObservableCollection<ExclusionPreset> ExclusionPresets { get; } = [];

    #endregion

    #region State & Progress

    [ObservableProperty] private RobocopyJobState _currentState = RobocopyJobState.Ready;
    [ObservableProperty] private string _statusMessage = "Ready to copy";
    [ObservableProperty] private bool _isRunning;
    [ObservableProperty] private bool _isPaused;
    [ObservableProperty] private bool _canStart = true;
    [ObservableProperty] private double _progressPercentage;
    [ObservableProperty] private string _currentFile = string.Empty;
    [ObservableProperty] private long _filesCopied;
    [ObservableProperty] private long _totalFiles;
    [ObservableProperty] private long _bytesCopied;
    [ObservableProperty] private long _totalBytes;
    [ObservableProperty] private double _transferSpeedMBps;
    [ObservableProperty] private string _estimatedTimeRemaining = string.Empty;
    [ObservableProperty] private string _elapsedTime = string.Empty;

    #endregion

    #region Integrity Verification

    [ObservableProperty] private bool _enableIntegrityCheck = true;
    [ObservableProperty] private IntegrityCheckMethod _integrityCheckMethod = IntegrityCheckMethod.Blake3;
    [ObservableProperty] private long _filesVerified;
    [ObservableProperty] private long _filesVerifiedPassed;
    [ObservableProperty] private long _filesVerifiedFailed;
    [ObservableProperty] private long _filesRetrying;
    [ObservableProperty] private double _verificationProgressPercentage;
    [ObservableProperty] private string _currentVerificationFile = string.Empty;

    #endregion

    #region Results & Errors

    [ObservableProperty] private RobocopyResult? _result;
    [ObservableProperty] private string _logFilePath = string.Empty;

    public ObservableCollection<RobocopyError> Errors { get; } = [];
    [ObservableProperty] private bool _hasErrors;
    [ObservableProperty] private int _errorCount;

    #endregion

    #region Log Viewer

    [ObservableProperty] private bool _isLogVisible = true;
    [ObservableProperty] private string _logOutput = string.Empty;
    [ObservableProperty] private int _logLineCount;
    [ObservableProperty] private bool _isLogTruncated;

    private Window? _logWindow;
    private const int MaxDisplayLines = 1000;
    private readonly Queue<string> _logLines = new();

    #endregion

    #region Constructor & Initialization

    public FileCopyViewModel(
        IFileCopyServiceFactory factory,
        IDialogService dialogService,
        IExclusionPresetService exclusionPresetService,
        IGitIgnoreParserService gitIgnoreParserService)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _dialogService = dialogService ?? throw new ArgumentNullException(nameof(dialogService));
        _exclusionPresetService =
            exclusionPresetService ?? throw new ArgumentNullException(nameof(exclusionPresetService));
        _gitIgnoreParserService =
            gitIgnoreParserService ?? throw new ArgumentNullException(nameof(gitIgnoreParserService));

        // Load available engines
        LoadAvailableEngines();

        // Load exclusion presets
        LoadExclusionPresets();

        // Initialize engine recommendation
        UpdateEngineRecommendation();
    }

    private void LoadAvailableEngines()
    {
        AvailableEngines.Clear();

        var available = _factory.GetAvailableEngines();

        // Track availability for UI bindings
        IsNativeEngineAvailable = available.Contains(CopyEngineType.Native);
        IsRobocopyEngineAvailable = available.Contains(CopyEngineType.Robocopy);

        // Always add Auto first
        AvailableEngines.Add(CopyEngineType.Auto);

        // Add all engines (both available and unavailable for display)
        AvailableEngines.Add(CopyEngineType.Native);
        AvailableEngines.Add(CopyEngineType.Robocopy);

        // Show warning if robocopy unavailable
        if (!IsRobocopyEngineAvailable) StatusMessage = "Note: Robocopy unavailable - Native C# engine will be used";
    }

    #endregion

    #region Property Change Handlers

    partial void OnSourcePathChanged(string value)
    {
        OnPropertyChanged(nameof(CommandPreview));
        UpdateEngineRecommendation();
    }

    partial void OnDestinationPathChanged(string value)
    {
        OnPropertyChanged(nameof(CommandPreview));
        UpdateEngineRecommendation();
    }

    partial void OnSelectedEngineChanged(CopyEngineType value)
    {
        OnPropertyChanged(nameof(CommandPreview));
        UpdateEngineRecommendation();
    }

    partial void OnCopySubdirectoriesChanged(bool value)
    {
        OnPropertyChanged(nameof(CommandPreview));
    }

    partial void OnMirrorModeChanged(bool value)
    {
        OnPropertyChanged(nameof(CommandPreview));
        UpdateEngineRecommendation();
    }

    partial void OnUseMultithreadingChanged(bool value)
    {
        OnPropertyChanged(nameof(CommandPreview));
    }

    partial void OnThreadCountChanged(int value)
    {
        OnPropertyChanged(nameof(CommandPreview));
    }

    partial void OnBackupModeChanged(bool value)
    {
        OnPropertyChanged(nameof(CommandPreview));
        UpdateEngineRecommendation();
    }

    partial void OnCopySecurityChanged(bool value)
    {
        OnPropertyChanged(nameof(CommandPreview));
        UpdateEngineRecommendation();
    }

    partial void OnSelectedExclusionPresetChanged(ExclusionPreset? value)
    {
        if (value != null && value.Id != "custom")
        {
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
        var options = RobocopyOptions.FromPreset(value);

        CopySubdirectories = options.CopySubdirectories;
        MirrorMode = options.MirrorMode;
        UseMultithreading = options.UseMultithreading;
        BackupMode = options.BackupMode;
        CopySecurity = options.CopySecurity;
        ThreadCount = options.ThreadCount;

        OnPropertyChanged(nameof(CommandPreview));
    }

    #endregion

    #region Engine Recommendation Logic

    /// <summary>
    ///     Updates the engine recommendation based on current options.
    /// </summary>
    private void UpdateEngineRecommendation()
    {
        if (string.IsNullOrWhiteSpace(SourcePath) || string.IsNullOrWhiteSpace(DestinationPath))
        {
            EngineRecommendationSummary = "Select source and destination to see recommendation";
            EngineRecommendationDetails = string.Empty;
            EngineWarnings.Clear();
            HasEngineWarnings = false;
            RecommendationConfidence = 0;
            ExpectedPerformance = string.Empty;
            ScenarioSummary = string.Empty;
            return;
        }

        try
        {
            var options = BuildFileCopyOptions();
            var recommendation = _factory.RecommendEngine(options);

            // Build compact summary
            var engineIcon = GetEngineIcon(recommendation.RecommendedEngine);
            var engineName = GetEngineFriendlyName(recommendation.RecommendedEngine);
            var shortReason = GetShortReason(recommendation);

            EngineRecommendationSummary = $"{engineIcon} {engineName} - {shortReason}";

            // Build detailed explanation
            EngineRecommendationDetails = FormatRecommendationDetails(recommendation);

            // Update properties
            RecommendationConfidence = recommendation.Confidence;
            ExpectedPerformance = recommendation.ExpectedPerformance;
            ScenarioSummary = recommendation.ScenarioSummary;

            // Update warnings
            EngineWarnings.Clear();
            foreach (var warning in recommendation.Warnings)
                EngineWarnings.Add(warning);

            HasEngineWarnings = EngineWarnings.Any();
        }
        catch (Exception ex)
        {
            EngineRecommendationSummary = $"Unable to generate recommendation: {ex.Message}";
            EngineRecommendationDetails = string.Empty;
        }
    }

    private string GetEngineIcon(CopyEngineType engine)
    {
        return engine switch
        {
            CopyEngineType.Native => "🚀",
            CopyEngineType.Robocopy => "📋",
            _ => "💡"
        };
    }

    private string GetEngineFriendlyName(CopyEngineType engine)
    {
        return engine switch
        {
            CopyEngineType.Native => "Native C#",
            CopyEngineType.Robocopy => "Robocopy",
            CopyEngineType.Auto => "Auto",
            _ => engine.ToString()
        };
    }

    private string GetShortReason(EngineRecommendation recommendation)
    {
        // Extract first sentence or key phrase
        var reasoning = recommendation.Reasoning;
        var firstSentence = reasoning.Split('.').FirstOrDefault()?.Trim();

        if (string.IsNullOrEmpty(firstSentence))
            return reasoning;

        // Shorten if too long
        if (firstSentence.Length > 60)
            return firstSentence.Substring(0, 57) + "...";

        return firstSentence;
    }

    private string FormatRecommendationDetails(EngineRecommendation recommendation)
    {
        var details = new StringBuilder();

        details.AppendLine($"Recommended Engine: {GetEngineFriendlyName(recommendation.RecommendedEngine)}");
        details.AppendLine();
        details.AppendLine("Reasoning:");
        details.AppendLine(recommendation.Reasoning);
        details.AppendLine();
        details.AppendLine($"Confidence: {recommendation.Confidence:P0}");
        details.AppendLine($"Expected Performance: {recommendation.ExpectedPerformance}");
        details.AppendLine($"Scenario: {recommendation.ScenarioSummary}");

        if (recommendation.Alternatives.Any())
        {
            details.AppendLine();
            details.AppendLine("Alternatives:");
            foreach (var alt in recommendation.Alternatives)
                details.AppendLine($"  • {GetEngineFriendlyName(alt.Key)}: {alt.Value}");
        }

        return details.ToString();
    }

    #endregion

    #region Command Preview Builders

    private CopyEngineType DetermineActualEngine(FileCopyOptions options)
    {
        if (SelectedEngine != CopyEngineType.Auto)
            return SelectedEngine;

        var recommendation = _factory.RecommendEngine(options);
        return recommendation.RecommendedEngine;
    }

    private string BuildRobocopyCommandPreview(FileCopyOptions options)
    {
        // Convert to RobocopyOptions for command building
        var robocopyOptions = new RobocopyOptions
        {
            SourcePath = options.SourcePath,
            DestinationPath = options.DestinationPath,
            CopySubdirectories = CopySubdirectories,
            MirrorMode = MirrorMode,
            UseMultithreading = UseMultithreading,
            ThreadCount = ThreadCount,
            BackupMode = BackupMode,
            CopySecurity = CopySecurity,
            RetryCount = 3,
            RetryWaitSeconds = 5,
            ExcludeDirectories = ExcludedDirectories.ToList(),
            ExcludeFiles = ExcludedFiles.ToList(),
            EnableIntegrityCheck = EnableIntegrityCheck,
            IntegrityCheckMethod = IntegrityCheckMethod,
            Preset = SelectedPreset
        };

        // Get robocopy service to build command line
        var robocopyService = _factory.CreateService(CopyEngineType.Robocopy);
        if (robocopyService is IRobocopyService roboService) return roboService.BuildCommandLine(robocopyOptions);

        return "Robocopy command line";
    }

    private string BuildNativeOperationPreview(FileCopyOptions options)
    {
        var preview = new StringBuilder();

        preview.AppendLine("Operation Preview:");
        preview.AppendLine($"🚀 Native C# Engine {(SelectedEngine == CopyEngineType.Auto ? "(Auto-selected)" : "")}");
        preview.AppendLine($"├─ {SourcePath}");
        preview.AppendLine($"└─ {DestinationPath}");

        if (TotalFiles > 0)
            preview.AppendLine($"├─ ~{TotalFiles:N0} files (~{FormatBytes(TotalBytes)})");

        if (UseMultithreading)
            preview.AppendLine($"├─ {ThreadCount} parallel threads");

        if (EnableIntegrityCheck)
            preview.AppendLine($"├─ Integrity verification: {IntegrityCheckMethod}");

        if (MirrorMode)
            preview.AppendLine("├─ Mirror mode (⚠️ will delete extra files)");

        if (ExcludedDirectories.Any() || ExcludedFiles.Any())
            preview.AppendLine($"├─ Exclusions: {ExcludedDirectories.Count} folders, {ExcludedFiles.Count} files");

        preview.AppendLine($"└─ Expected: {ExpectedPerformance}");

        return preview.ToString();
    }

    #endregion

    #region Path Selection Commands

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
    private void OpenSourceInExplorer()
    {
        if (Directory.Exists(SourcePath))
            Process.Start(new ProcessStartInfo
            {
                FileName = SourcePath,
                UseShellExecute = true
            });
    }

    [RelayCommand]
    private void OpenDestinationInExplorer()
    {
        if (Directory.Exists(DestinationPath))
            Process.Start(new ProcessStartInfo
            {
                FileName = DestinationPath,
                UseShellExecute = true
            });
    }

    #endregion

    #region Copy Operation Commands

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
        var options = BuildFileCopyOptions();

        // Create appropriate service via factory
        var copyService = _factory.CreateService(options);
        if (copyService == null)
        {
            await _dialogService.ShowErrorAsync("Engine Unavailable",
                "No suitable copy engine is available for this operation.");
            return;
        }

        // Validate options
        var validation = await copyService.ValidateOptionsAsync(options);
        if (!validation.IsValid)
        {
            await _dialogService.ShowErrorAsync("Validation Error",
                $"Validation failed: {validation.ErrorMessage}");
            return;
        }

        // Show warnings if any
        if (validation.Warnings != null && validation.Warnings.Any())
        {
            var warningMessage = "Please review these warnings:\n\n" +
                                 string.Join("\n", validation.Warnings);
            var proceed = await _dialogService.ShowConfirmationAsync("Warnings", warningMessage);
            if (!proceed)
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
        await ExecuteCopyAsync(copyService, options);
    }

    [RelayCommand(CanExecute = nameof(CanPause))]
    private void Pause()
    {
        try
        {
            _activeService?.Pause();
            IsPaused = true;
            StatusMessage = "Paused";
        }
        catch (NotSupportedException ex)
        {
            _dialogService.ShowWarning("Pause Not Supported",
                $"The current copy engine does not support pausing: {ex.Message}");
        }
        catch (Exception ex)
        {
            _dialogService.ShowError("Pause Failed",
                $"Failed to pause copy operation: {ex.Message}");
        }
    }

    private bool CanPause()
    {
        return IsRunning && !IsPaused;
    }

    [RelayCommand(CanExecute = nameof(CanResume))]
    private void Resume()
    {
        try
        {
            _activeService?.Resume();
            IsPaused = false;
            StatusMessage = "Copying files...";
        }
        catch (NotSupportedException ex)
        {
            _dialogService.ShowWarning("Resume Not Supported",
                $"The current copy engine does not support resuming: {ex.Message}");
        }
        catch (Exception ex)
        {
            _dialogService.ShowError("Resume Failed",
                $"Failed to resume copy operation: {ex.Message}");
        }
    }

    private bool CanResume()
    {
        return IsRunning && IsPaused;
    }

    [RelayCommand(CanExecute = nameof(IsRunning))]
    private void Cancel()
    {
        _cancellationTokenSource?.Cancel();
        StatusMessage = "Cancelling...";
    }

    #endregion

    #region Copy Execution

    private IFileCopyService? _activeService; // Track active service for pause/resume

    private async Task ExecuteCopyAsync(IFileCopyService copyService, FileCopyOptions options)
    {
        _cancellationTokenSource = new CancellationTokenSource();
        _activeService = copyService;

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
            var progress = new Progress<FileCopyProgress>(OnFileCopyProgressUpdated);

            var result = await copyService.CopyAsync(
                options,
                progress,
                _cancellationTokenSource.Token);

            // Convert result for display (assuming RobocopyResult for now - ideally we'd have generic result)
            UpdateUIFromResult(result);

            // Show completion message
            var message = BuildCompletionMessage(result);
            if (result.Success)
                _dialogService.ShowSuccess("Copy Completed", message);
            else
                _dialogService.ShowWarning("Copy Completed", message);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Operation cancelled by user";
            CurrentState = RobocopyJobState.Cancelled;
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
            _activeService = null;

            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;

            // Update command states
            PauseCommand.NotifyCanExecuteChanged();
            ResumeCommand.NotifyCanExecuteChanged();
            CancelCommand.NotifyCanExecuteChanged();
        }
    }

    private void OnFileCopyProgressUpdated(FileCopyProgress progress)
    {
        // Update all progress properties
        StatusMessage = progress.StatusMessage;
        CurrentFile = progress.CurrentFile;
        FilesCopied = progress.FilesCopied;
        TotalFiles = progress.TotalFiles;
        BytesCopied = progress.BytesCopied;
        TotalBytes = progress.TotalBytes;
        ProgressPercentage = progress.OverallProgress; // Smart progress: directories during scan, files during copy
        TransferSpeedMBps = progress.MegabytesPerSecond;
        ErrorCount = progress.ErrorCount;
        HasErrors = progress.ErrorCount > 0;

        // Update integrity verification progress
        FilesVerified = progress.FilesVerified;
        FilesVerifiedPassed = progress.FilesVerifiedPassed;
        FilesVerifiedFailed = progress.FilesVerifiedFailed;
        VerificationProgressPercentage = progress.VerificationPercentComplete;
        CurrentVerificationFile = progress.CurrentVerificationFile;

        // Format time strings
        ElapsedTime = FormatTimeSpan(progress.Elapsed);
        EstimatedTimeRemaining = FormatTimeSpan(progress.EstimatedTimeRemaining);

        // Update state
        CurrentState = MapFileCopyStateToRobocopyState(progress.State);

        // Update log view for Native engine (Robocopy updates via its own mechanism)
        if (IsLogVisible && _activeService != null && _activeService is not IRobocopyService)
            LogOutput = BuildNativeLogOutput();
    }

    private RobocopyJobState MapFileCopyStateToRobocopyState(FileCopyJobState state)
    {
        return state switch
        {
            FileCopyJobState.Scanning => RobocopyJobState.Scanning,
            FileCopyJobState.Running => RobocopyJobState.Running,
            FileCopyJobState.Verifying => RobocopyJobState
                .Running, // Map to Running since RobocopyJobState doesn't have Verifying
            FileCopyJobState.Completed => RobocopyJobState.Completed,
            FileCopyJobState.Failed => RobocopyJobState.Failed,
            FileCopyJobState.Cancelled => RobocopyJobState.Cancelled,
            FileCopyJobState.Paused => RobocopyJobState.Paused,
            _ => RobocopyJobState.Ready
        };
    }

    private void UpdateUIFromResult(FileCopyResult result)
    {
        CurrentState = MapFileCopyStateToRobocopyState(result.State);
        StatusMessage = result.Success ? "Completed successfully" : "Completed with errors";

        // For compatibility with RobocopyResult expectations
        Result = null; // Would need result conversion

        ErrorCount = result.ErrorCount;
        HasErrors = result.ErrorCount > 0;
    }

    private string BuildCompletionMessage(FileCopyResult result)
    {
        var msg = new StringBuilder();
        msg.AppendLine($"Engine: {GetEngineFriendlyName(result.EngineType)}");
        msg.AppendLine($"Files copied: {result.FilesCopied:N0} / {result.TotalFiles:N0}");
        msg.AppendLine($"Data copied: {FormatBytes(result.BytesCopied)} / {FormatBytes(result.TotalBytes)}");
        msg.AppendLine($"Elapsed time: {FormatTimeSpan(result.Elapsed)}");

        if (result.IntegrityCheckEnabled)
            msg.AppendLine(
                $"\nVerified: {result.FilesVerifiedPassed:N0} passed, {result.FilesVerifiedFailed:N0} failed");

        if (result.ErrorCount > 0) msg.AppendLine($"\n⚠️ Errors: {result.ErrorCount}");

        return msg.ToString();
    }

    #endregion

    #region Log & Result Commands

    [RelayCommand]
    private void ViewLog()
    {
        IsLogVisible = !IsLogVisible;

        if (IsLogVisible)
        {
            if (_activeService is IRobocopyService roboService)
                // Robocopy: Get actual command output
                LogOutput = roboService.GetCurrentOutput();
            else
                // Native engine: Build synthetic progress log
                LogOutput = BuildNativeLogOutput();
        }
    }

    private string BuildNativeLogOutput()
    {
        var log = new StringBuilder();
        log.AppendLine("═══════════════════════════════════════════════════════════");
        log.AppendLine("  Native C# Copy Engine - Progress Summary");
        log.AppendLine("═══════════════════════════════════════════════════════════");
        log.AppendLine();

        log.AppendLine($"Status: {StatusMessage}");
        log.AppendLine($"Current State: {CurrentState}");
        log.AppendLine();

        log.AppendLine("File Progress:");
        log.AppendLine($"  Copied: {FilesCopied:N0} / {TotalFiles:N0} ({ProgressPercentage:F1}%)");
        log.AppendLine($"  Current: {(string.IsNullOrEmpty(CurrentFile) ? "N/A" : Path.GetFileName(CurrentFile))}");
        log.AppendLine();

        log.AppendLine("Data Transfer:");
        log.AppendLine($"  Transferred: {FormatBytes(BytesCopied)} / {FormatBytes(TotalBytes)}");
        log.AppendLine($"  Speed: {TransferSpeedMBps:F2} MB/s");
        log.AppendLine($"  Elapsed: {ElapsedTime}");
        if (!string.IsNullOrEmpty(EstimatedTimeRemaining))
            log.AppendLine($"  Remaining: {EstimatedTimeRemaining}");
        log.AppendLine();

        if (EnableIntegrityCheck && FilesVerified > 0)
        {
            log.AppendLine("Integrity Verification:");
            log.AppendLine($"  Method: {IntegrityCheckMethod}");
            log.AppendLine($"  Verified: {FilesVerified:N0}");
            log.AppendLine($"  Passed: {FilesVerifiedPassed:N0}");
            log.AppendLine($"  Failed: {FilesVerifiedFailed:N0}");
            if (FilesRetrying > 0)
                log.AppendLine($"  Retrying: {FilesRetrying:N0}");
            log.AppendLine();
        }

        if (HasErrors && ErrorCount > 0)
        {
            log.AppendLine($"⚠️ Errors: {ErrorCount}");
            log.AppendLine();
        }

        log.AppendLine("═══════════════════════════════════════════════════════════");

        return log.ToString();
    }

    [RelayCommand]
    private void OpenLogWindow()
    {
        if (_logWindow == null)
        {
            _logWindow = new RobocopyLogWindow(this, _dialogService);
            _logWindow.Closed += (s, e) => _logWindow = null;
        }

        if (_logWindow.IsVisible)
            _logWindow.Activate();
        else
            _logWindow.Show();
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

    [RelayCommand]
    private void ShowFailedVerifications()
    {
        if (FilesVerifiedFailed == 0)
        {
            _dialogService.ShowInfo("No Failures",
                "No verification failures to display.");
            return;
        }

        // This would need proper result handling
        _dialogService.ShowInfo("Failed Verifications",
            $"{FilesVerifiedFailed} files failed verification.");
    }

    [RelayCommand]
    private void CopyCommand()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(CommandPreview) &&
                CommandPreview != "Select source and destination to preview operation...")
            {
                Clipboard.SetText(CommandPreview);
                StatusMessage = "Preview copied to clipboard!";
            }
        }
        catch (Exception ex)
        {
            _dialogService.ShowError("Error", $"Failed to copy to clipboard: {ex.Message}");
        }
    }

    public void CloseLogWindow()
    {
        if (_logWindow != null)
        {
            _logWindow.Closed -= (s, e) => _logWindow = null;
            _logWindow.Close();
            _logWindow = null;
        }
    }

    #endregion

    #region Exclusion Management

    private void LoadExclusionPresets()
    {
        ExclusionPresets.Clear();

        ExclusionPresets.Add(new ExclusionPreset
        {
            Id = "custom",
            Name = "Custom",
            Description = "Manually configure exclusions",
            IsBuiltIn = true
        });

        foreach (var preset in _exclusionPresetService.GetAllPresets()) ExclusionPresets.Add(preset);

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
            if (!ExcludedDirectories.Contains(text))
            {
                ExcludedDirectories.Add(text);
                SwitchToCustomPreset();
                OnPropertyChanged(nameof(CommandPreview));
            }
        }
        else
        {
            if (!ExcludedFiles.Contains(text))
            {
                ExcludedFiles.Add(text);
                SwitchToCustomPreset();
                OnPropertyChanged(nameof(CommandPreview));
            }
        }

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
        var name = Interaction.InputBox(
            "Enter a name for this exclusion preset:",
            "Save Preset",
            "My Preset");

        if (string.IsNullOrWhiteSpace(name))
            return;

        if (_exclusionPresetService.PresetExists(name))
        {
            var overwrite = _dialogService.ShowQuestion(
                "Preset Exists",
                $"A preset named '{name}' already exists. Do you want to overwrite it?");

            if (!overwrite)
                return;
        }

        var preset = new ExclusionPreset
        {
            Name = name,
            Description =
                $"User-created preset with {ExcludedDirectories.Count} folders and {ExcludedFiles.Count} files excluded",
            ExcludedDirectories = ExcludedDirectories.ToList(),
            ExcludedFiles = ExcludedFiles.ToList(),
            IsBuiltIn = false
        };

        try
        {
            _exclusionPresetService.SavePreset(preset);
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
        var dialog = new OpenFileDialog
        {
            Title = "Select .gitignore file",
            Filter = "Gitignore files (.gitignore)|.gitignore|All files (*.*)|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog() != true)
            return;

        try
        {
            var (directories, files, unsupportedPatterns) = _gitIgnoreParserService.ParseGitIgnoreFile(dialog.FileName);

            var addedDirs = 0;
            var addedFiles = 0;

            foreach (var dir in directories)
                if (!ExcludedDirectories.Contains(dir))
                {
                    ExcludedDirectories.Add(dir);
                    addedDirs++;
                }

            foreach (var file in files)
                if (!ExcludedFiles.Contains(file))
                {
                    ExcludedFiles.Add(file);
                    addedFiles++;
                }

            if (addedDirs > 0 || addedFiles > 0)
            {
                SwitchToCustomPreset();
                OnPropertyChanged(nameof(CommandPreview));
            }

            var message =
                $"Successfully imported {addedDirs} folder(s) and {addedFiles} file pattern(s) from .gitignore.";

            if (directories.Count + files.Count > addedDirs + addedFiles)
            {
                var skipped = directories.Count - addedDirs + (files.Count - addedFiles);
                message += $"\n\n{skipped} pattern(s) were skipped (already in exclusion list).";
            }

            if (unsupportedPatterns.Count > 0)
                message += $"\n\nWarning: {unsupportedPatterns.Count} complex pattern(s) were skipped.";

            if (addedDirs > 0 || addedFiles > 0)
                _dialogService.ShowSuccess("Import Complete", message);
            else
                _dialogService.ShowInfo("Import Complete", message);
        }
        catch (Exception ex)
        {
            _dialogService.ShowError("Import Failed", $"Failed to import .gitignore file:\n{ex.Message}");
        }
    }

    private void SwitchToCustomPreset()
    {
        if (SelectedExclusionPreset?.Id != "custom")
            SelectedExclusionPreset = ExclusionPresets.FirstOrDefault(p => p.Id == "custom");
    }

    #endregion

    #region Utility Methods

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
        string[] sizes = ["B", "KB", "MB", "GB", "TB"];
        double len = bytes;
        var order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }

        return $"{len:F2} {sizes[order]}";
    }

    #endregion
}