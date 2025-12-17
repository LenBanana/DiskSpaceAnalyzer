using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DiskSpaceAnalyzer.Models;
using DiskSpaceAnalyzer.Models.Robocopy;
using DiskSpaceAnalyzer.Models.FileCopy;
using DiskSpaceAnalyzer.Services.FileCopy;

namespace DiskSpaceAnalyzer.Services.Robocopy;

/// <summary>
/// Main implementation of robocopy operations.
/// Manages process lifecycle, progress monitoring, and pause/resume functionality.
/// Implements both IFileCopyService (generic) and IRobocopyService (legacy) for full compatibility.
/// </summary>
public class RobocopyService : IFileCopyService, IRobocopyService
{
    private readonly IFileSystemService _fileSystemService;
    private readonly ProcessSuspender _processSuspender;
    private readonly RobocopyCommandBuilder _commandBuilder;
    private readonly RobocopyLogParser _logParser;
    
    private Process? _currentProcess;
    private FileSystemWatcher? _logWatcher;
    private string? _currentLogPath;
    private DateTime _startTime;
    private DateTime _pauseStartTime;
    private TimeSpan _totalPausedDuration;
    private bool _isPaused;
    
    // Progress tracking
    private long _totalBytes;
    private long _totalFiles;
    private long _totalDirectories;
    private long _lastFileCount;
    private long _lastByteCount;
    private readonly List<string> _outputLines = new();
    private readonly object _outputLock = new();
    
    public RobocopyService(
        IFileSystemService fileSystemService,
        ProcessSuspender processSuspender,
        RobocopyCommandBuilder commandBuilder,
        RobocopyLogParser logParser)
    {
        _fileSystemService = fileSystemService;
        _processSuspender = processSuspender;
        _commandBuilder = commandBuilder;
        _logParser = logParser;
    }
    
    public async Task<RobocopyResult> CopyAsync(
        RobocopyOptions options,
        IProgress<RobocopyProgress> progress,
        CancellationToken cancellationToken)
    {
        var result = new RobocopyResult
        {
            StartTime = DateTime.Now,
            State = RobocopyJobState.Scanning
        };
        
        _startTime = DateTime.Now;
        _totalPausedDuration = TimeSpan.Zero;
        _isPaused = false;
        
        try
        {
            // Step 1: Pre-scan source directory
            await PreScanSourceAsync(options, progress, cancellationToken);
            
            // Step 2: Setup log file
            _currentLogPath = Path.Combine(Path.GetTempPath(), $"robocopy_{Guid.NewGuid():N}.log");
            options.LogFilePath = _currentLogPath;
            
            // Step 3: Build command and start process
            var arguments = _commandBuilder.BuildArguments(options);
            
            result.State = RobocopyJobState.Running;
            ReportProgress(progress, result.State, "Starting robocopy...");
            
            _currentProcess = StartRobocopyProcess(arguments);
            
            // Step 4: Monitor progress
            await MonitorProgressAsync(progress, cancellationToken);
            
            // Step 5: Wait for completion
            await _currentProcess.WaitForExitAsync(cancellationToken);
            
            result.ExitCode = _currentProcess.ExitCode;
            result.State = DetermineState(result.ExitCode, cancellationToken.IsCancellationRequested);
            
            // Step 6: Parse final results
            if (File.Exists(_currentLogPath))
            {
                var logContent = await File.ReadAllTextAsync(_currentLogPath, cancellationToken);
                result = _logParser.ParseSummary(logContent, result);
                result.Errors.AddRange(_logParser.ParseErrors(logContent.Split('\n')));
                result.LogFilePath = _currentLogPath;
            }
            
            // Calculate statistics
            result.EndTime = DateTime.Now;
            result.Duration = result.EndTime - result.StartTime;
            result.ActiveDuration = result.Duration - _totalPausedDuration;
            
            if (result.ActiveDuration.TotalSeconds > 0)
            {
                result.AverageBytesPerSecond = result.BytesCopied / result.ActiveDuration.TotalSeconds;
            }
            
            var (success, message) = _logParser.InterpretExitCode(result.ExitCode);
            result.Success = success && result.State == RobocopyJobState.Completed;
            result.ExitCodeMessage = message;
            
            // Final progress report
            ReportFinalProgress(progress, result);
            
            return result;
        }
        catch (OperationCanceledException)
        {
            result.State = RobocopyJobState.Cancelled;
            result.Success = false;
            result.EndTime = DateTime.Now;
            result.Duration = result.EndTime - result.StartTime;
            
            CleanupProcess();
            
            return result;
        }
        catch (Exception ex)
        {
            result.State = RobocopyJobState.Failed;
            result.Success = false;
            result.ExitCodeMessage = ex.Message;
            result.EndTime = DateTime.Now;
            result.Duration = result.EndTime - result.StartTime;
            
            result.Errors.Add(new RobocopyError
            {
                ErrorCode = -1,
                Message = ex.Message,
                FilePath = options.SourcePath,
                Timestamp = DateTime.Now
            });
            
            CleanupProcess();
            
            return result;
        }
        finally
        {
            Cleanup();
        }
    }
    
    public void Pause()
    {
        if (_currentProcess == null || _currentProcess.HasExited || _isPaused)
            return;
        
        _processSuspender.SuspendProcess(_currentProcess);
        _isPaused = true;
        _pauseStartTime = DateTime.Now;
    }
    
    public void Resume()
    {
        if (_currentProcess == null || _currentProcess.HasExited || !_isPaused)
            return;
        
        _processSuspender.ResumeProcess(_currentProcess);
        
        // Track pause duration
        var pauseDuration = DateTime.Now - _pauseStartTime;
        _totalPausedDuration += pauseDuration;
        
        _isPaused = false;
    }
    
    public void Cancel()
    {
        CleanupProcess();
    }
    
    public bool IsRobocopyAvailable()
    {
        try
        {
            var path = GetRobocopyPath();
            return File.Exists(path);
        }
        catch
        {
            return false;
        }
    }
    
    public string GetRobocopyPath()
    {
        // Robocopy is in System32 on Windows Vista and later
        var systemPath = Environment.GetFolderPath(Environment.SpecialFolder.System);
        return Path.Combine(systemPath, "robocopy.exe");
    }
    
    public (bool IsValid, string? ErrorMessage) ValidateOptions(RobocopyOptions options)
    {
        // Validate options internally
        var (isValid, error) = options.Validate();
        if (!isValid)
            return (false, error);
        
        // Check source exists
        if (!Directory.Exists(options.SourcePath))
            return (false, $"Source directory does not exist: {options.SourcePath}");
        
        // Check destination parent exists (destination itself can be created)
        try
        {
            var destParent = Path.GetDirectoryName(options.DestinationPath);
            if (!string.IsNullOrEmpty(destParent) && !Directory.Exists(destParent))
                return (false, $"Destination parent directory does not exist: {destParent}");
        }
        catch (Exception ex)
        {
            return (false, $"Invalid destination path: {ex.Message}");
        }
        
        // Check robocopy is available
        if (!IsRobocopyAvailable())
            return (false, "Robocopy.exe not found on this system.");
        
        // Warn about dangerous options (but don't block)
        if (options.MirrorMode)
        {
            // Return warning but allow (UI should confirm with user)
        }
        
        return (true, null);
    }
    
    public string GetCurrentOutput(int maxLines = 250)
    {
        lock (_outputLock)
        {
            if (_outputLines.Count == 0)
                return string.Empty;
            
            var linesToTake = Math.Min(maxLines, _outputLines.Count);
            var recentLines = _outputLines.TakeLast(linesToTake);
            return string.Join(Environment.NewLine, recentLines);
        }
    }
    
    public string BuildCommandLine(RobocopyOptions options)
    {
        var robocopyPath = GetRobocopyPath();
        var arguments = _commandBuilder.BuildArguments(options);
        return $"{robocopyPath} {arguments}";
    }
    
    public List<DiskSpaceAnalyzer.Models.FileCopy.IntegrityCheckResult>? GetCurrentVerificationResults()
    {
        // Robocopy doesn't have built-in integrity verification
        // This would need to be implemented separately if needed
        return null;
    }
    
    /// <summary>
    /// Pre-scan source directory to get total size/file count, respecting exclusions.
    /// </summary>
    private async Task PreScanSourceAsync(
        RobocopyOptions options,
        IProgress<RobocopyProgress> progress,
        CancellationToken cancellationToken)
    {
        ReportProgress(progress, RobocopyJobState.Scanning, "Scanning source directory...");
        
        _totalBytes = 0;
        _totalFiles = 0;
        _totalDirectories = 0;
        
        await Task.Run(() => ScanDirectoryWithExclusions(
            options.SourcePath,
            options.SourcePath,
            options.ExcludeDirectories,
            options.ExcludeFiles,
            progress,
            cancellationToken), cancellationToken);
    }
    
    /// <summary>
    /// Recursively scan directory applying exclusion filters.
    /// </summary>
    private void ScanDirectoryWithExclusions(
        string sourcePath,
        string currentPath,
        List<string> excludedDirectories,
        List<string> excludedFiles,
        IProgress<RobocopyProgress> progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        
        try
        {
            var dirInfo = new DirectoryInfo(currentPath);
            
            // Get files (respecting file exclusions)
            foreach (var file in dirInfo.EnumerateFiles())
            {
                try
                {
                    // Check if file should be excluded
                    if (ShouldExcludeFile(file.FullName, file.Name, sourcePath, excludedFiles))
                        continue;
                    
                    _totalFiles++;
                    _totalBytes += file.Length;
                    
                    // Report progress periodically
                    if (_totalFiles % 100 == 0)
                    {
                        var msg = $"Scanning... {_totalFiles:N0} files, {FormatBytes(_totalBytes)}";
                        ReportProgress(progress, RobocopyJobState.Scanning, msg);
                    }
                }
                catch
                {
                    // Skip files we can't access
                }
            }
            
            // Get directories (respecting directory exclusions)
            foreach (var subDir in dirInfo.EnumerateDirectories())
            {
                try
                {
                    // Check if directory should be excluded
                    if (ShouldExcludeDirectory(subDir.FullName, subDir.Name, sourcePath, excludedDirectories))
                        continue;
                    
                    _totalDirectories++;
                    
                    // Recurse into subdirectory
                    ScanDirectoryWithExclusions(
                        sourcePath,
                        subDir.FullName,
                        excludedDirectories,
                        excludedFiles,
                        progress,
                        cancellationToken);
                }
                catch
                {
                    // Skip directories we can't access
                }
            }
        }
        catch
        {
            // Skip if we can't access the directory
        }
    }
    
    /// <summary>
    /// Check if a directory should be excluded.
    /// Mimics robocopy /XD behavior: matches names, paths, and wildcards.
    /// </summary>
    private bool ShouldExcludeDirectory(string fullPath, string directoryName, string sourcePath, List<string> excludedDirectories)
    {
        if (excludedDirectories == null || excludedDirectories.Count == 0)
            return false;
        
        foreach (var excluded in excludedDirectories)
        {
            // Case 1: Simple name match (e.g., "node_modules" matches any directory named "node_modules")
            if (!excluded.Contains("\\") && !excluded.Contains("*") && !excluded.Contains("?"))
            {
                if (directoryName.Equals(excluded, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            // Case 2: Wildcard pattern in name (e.g., "*cache", "temp*")
            else if (!excluded.Contains("\\") && (excluded.Contains("*") || excluded.Contains("?")))
            {
                if (MatchesWildcard(directoryName, excluded))
                    return true;
            }
            // Case 3: Path-based exclusion (e.g., "src\\bin", "lib\\temp")
            else if (excluded.Contains("\\"))
            {
                // Get relative path from source
                string relativePath = GetRelativePath(sourcePath, fullPath);
                
                // Check if relative path matches or ends with the exclusion pattern
                if (relativePath.Equals(excluded, StringComparison.OrdinalIgnoreCase) ||
                    relativePath.EndsWith("\\"+excluded, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
                
                // Also check with wildcards if pattern contains them
                if (excluded.Contains("*") || excluded.Contains("?"))
                {
                    if (MatchesWildcard(relativePath, excluded))
                        return true;
                }
            }
        }
        
        return false;
    }
    
    /// <summary>
    /// Get relative path from base to target.
    /// Uses string manipulation instead of Uri for better performance and robustness.
    /// </summary>
    private string GetRelativePath(string basePath, string targetPath)
    {
        // Normalize paths
        basePath = Path.GetFullPath(basePath).TrimEnd('\\');
        targetPath = Path.GetFullPath(targetPath).TrimEnd('\\');
        
        // If target is the base, return empty
        if (targetPath.Equals(basePath, StringComparison.OrdinalIgnoreCase))
            return string.Empty;
        
        // Target must start with base path
        if (!targetPath.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
            return targetPath; // Not relative, return full path
        
        // Remove base path and leading separator
        var relative = targetPath.Substring(basePath.Length).TrimStart('\\');
        return relative;
    }
    
    /// <summary>
    /// Check if a file should be excluded.
    /// Mimics robocopy /XF behavior: matches names, paths, and wildcards.
    /// </summary>
    private bool ShouldExcludeFile(string fullPath, string fileName, string sourcePath, List<string> excludedFiles)
    {
        if (excludedFiles == null || excludedFiles.Count == 0)
            return false;
        
        foreach (var excluded in excludedFiles)
        {
            // Case 1: Simple name or wildcard pattern (e.g., "*.log", "Thumbs.db")
            if (!excluded.Contains("\\"))
            {
                if (MatchesWildcard(fileName, excluded))
                    return true;
            }
            // Case 2: Path-based exclusion (e.g., "src\\*.log", "logs\\debug.txt")
            else
            {
                // Get relative path from source
                string relativePath = GetRelativePath(sourcePath, fullPath);
                
                // Check exact match or path ending match
                if (relativePath.Equals(excluded, StringComparison.OrdinalIgnoreCase) ||
                    relativePath.EndsWith("\\" + excluded, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
                
                // Check with wildcards
                if (excluded.Contains("*") || excluded.Contains("?"))
                {
                    if (MatchesWildcard(relativePath, excluded))
                        return true;
                }
            }
        }
        
        return false;
    }
    
    /// <summary>
    /// Simple wildcard matching (* and ?).
    /// </summary>
    private bool MatchesWildcard(string fileName, string pattern)
    {
        // Convert wildcard pattern to regex
        var regexPattern = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";
        
        return System.Text.RegularExpressions.Regex.IsMatch(
            fileName, 
            regexPattern, 
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }
    
    /// <summary>
    /// Format bytes for display.
    /// </summary>
    private string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:F1} {sizes[order]}";
    }
    
    /// <summary>
    /// Start robocopy process with output redirection.
    /// </summary>
    private Process StartRobocopyProcess(string arguments)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = GetRobocopyPath(),
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            }
        };
        
        // Handle output asynchronously
        process.OutputDataReceived += OnOutputDataReceived;
        process.ErrorDataReceived += OnErrorDataReceived;
        
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        
        return process;
    }
    
    /// <summary>
    /// Handle stdout from robocopy process.
    /// </summary>
    private void OnOutputDataReceived(object sender, DataReceivedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Data))
            return;
        
        lock (_outputLock)
        {
            _outputLines.Add(e.Data);
            
            // Keep only recent lines to avoid memory issues
            if (_outputLines.Count > 1000)
            {
                _outputLines.RemoveRange(0, 500);
            }
        }
    }
    
    /// <summary>
    /// Handle stderr from robocopy process.
    /// </summary>
    private void OnErrorDataReceived(object sender, DataReceivedEventArgs e)
    {
        if (!string.IsNullOrEmpty(e.Data))
        {
            lock (_outputLock)
            {
                _outputLines.Add($"ERROR: {e.Data}");
            }
        }
    }
    
    /// <summary>
    /// Monitor progress by reading output.
    /// </summary>
    private async Task MonitorProgressAsync(
        IProgress<RobocopyProgress> progress,
        CancellationToken cancellationToken)
    {
        // Poll output periodically for updates
        while (_currentProcess != null && !_currentProcess.HasExited)
        {
            if (cancellationToken.IsCancellationRequested)
                break;
            
            await Task.Delay(500, cancellationToken); // Update every 500ms
            
            if (_isPaused)
                continue; // Don't update while paused
            
            // Read output and update progress
            await UpdateProgressFromOutputAsync(progress);
        }
    }
    
    /// <summary>
    /// Read stdout output and report progress.
    /// </summary>
    private async Task UpdateProgressFromOutputAsync(IProgress<RobocopyProgress> progress)
    {
        string[] outputSnapshot;
        
        lock (_outputLock)
        {
            outputSnapshot = _outputLines.ToArray();
        }
        
        if (outputSnapshot.Length == 0)
            return;
        
        try
        {
            // Parse current file from recent output
            var currentFile = _logParser.ParseCurrentFile(outputSnapshot.TakeLast(20));
            
            // Estimate progress from all output
            var avgFileSize = _totalFiles > 0 ? _totalBytes / _totalFiles : 0;
            var (fileCount, estimatedBytes) = _logParser.EstimateProgress(
                string.Join("\n", outputSnapshot), 
                avgFileSize);
            
            // Update counters (only increase, never decrease)
            if (fileCount > _lastFileCount)
                _lastFileCount = fileCount;
            
            if (estimatedBytes > _lastByteCount)
                _lastByteCount = estimatedBytes;
            
            // Parse errors
            var errors = _logParser.ParseErrors(outputSnapshot);
            
            // Report progress
            var elapsed = DateTime.Now - _startTime;
            var progressData = new RobocopyProgress
            {
                State = _isPaused ? RobocopyJobState.Paused : RobocopyJobState.Running,
                StatusMessage = _isPaused ? "Paused" : "Copying files...",
                FilesCopied = _lastFileCount,
                TotalFiles = _totalFiles,
                BytesCopied = Math.Min(_lastByteCount, _totalBytes), // Don't exceed total
                TotalBytes = _totalBytes,
                CurrentFile = currentFile ?? string.Empty,
                StartTime = _startTime,
                Elapsed = elapsed,
                PausedDuration = _totalPausedDuration,
                ErrorCount = errors.Count
            };
            
            progress?.Report(progressData);
        }
        catch
        {
            // Ignore errors parsing output
        }
    }
    
    /// <summary>
    /// Determine final state from exit code.
    /// </summary>
    private RobocopyJobState DetermineState(int exitCode, bool wasCancelled)
    {
        if (wasCancelled)
            return RobocopyJobState.Cancelled;
        
        if (exitCode >= 16)
            return RobocopyJobState.Failed;
        
        if (exitCode >= 8)
            return RobocopyJobState.Completed; // Completed with errors
        
        return RobocopyJobState.Completed;
    }
    
    /// <summary>
    /// Helper to report progress.
    /// </summary>
    private void ReportProgress(IProgress<RobocopyProgress> progress, RobocopyJobState state, string message)
    {
        progress?.Report(new RobocopyProgress
        {
            State = state,
            StatusMessage = message,
            TotalBytes = _totalBytes,
            TotalFiles = _totalFiles,
            TotalDirectories = _totalDirectories,
            StartTime = _startTime
        });
    }
    
    /// <summary>
    /// Report final progress.
    /// </summary>
    private void ReportFinalProgress(IProgress<RobocopyProgress> progress, RobocopyResult result)
    {
        progress?.Report(new RobocopyProgress
        {
            State = result.State,
            StatusMessage = result.ExitCodeMessage,
            FilesCopied = result.FilesCopied,
            TotalFiles = result.TotalFiles,
            BytesCopied = result.BytesCopied,
            TotalBytes = result.TotalBytes,
            StartTime = result.StartTime,
            Elapsed = result.Duration,
            PausedDuration = _totalPausedDuration,
            ErrorCount = result.Errors.Count
        });
    }
    
    /// <summary>
    /// Kill current process.
    /// </summary>
    private void CleanupProcess()
    {
        try
        {
            if (_currentProcess != null && !_currentProcess.HasExited)
            {
                _currentProcess.Kill(true); // Kill entire process tree
                _currentProcess.WaitForExit(5000);
            }
        }
        catch
        {
            // Ignore errors during cleanup
        }
    }
    
    /// <summary>
    /// Cleanup resources.
    /// </summary>
    private void Cleanup()
    {
        _logWatcher?.Dispose();
        _logWatcher = null;
        
        if (_currentProcess != null)
        {
            _currentProcess.OutputDataReceived -= OnOutputDataReceived;
            _currentProcess.ErrorDataReceived -= OnErrorDataReceived;
            _currentProcess.Dispose();
            _currentProcess = null;
        }
        
        lock (_outputLock)
        {
            _outputLines.Clear();
        }
    }

    #region IFileCopyService Implementation (Adapter Methods)

    /// <summary>
    /// Generic copy method - wraps robocopy-specific CopyAsync with conversion.
    /// </summary>
    async Task<FileCopyResult> IFileCopyService.CopyAsync(
        FileCopyOptions options,
        IProgress<FileCopyProgress>? progress,
        CancellationToken cancellationToken)
    {
        // Convert FileCopyOptions to RobocopyOptions
        var robocopyOptions = FileCopyOptionsMapper.ToRobocopyOptions(options);

        // Wrap progress reporter
        IProgress<RobocopyProgress>? robocopyProgress = null;
        if (progress != null)
        {
            robocopyProgress = new Progress<RobocopyProgress>(roboProgress =>
            {
                progress.Report(FileCopyProgressMapper.ToFileCopyProgress(roboProgress));
            });
        }

        // Execute robocopy operation
        var robocopyResult = await CopyAsync(robocopyOptions, robocopyProgress!, cancellationToken);

        // Convert result back to generic format
        return FileCopyResultMapper.ToFileCopyResult(robocopyResult);
    }

    /// <summary>
    /// Validate options - converts to robocopy options and validates.
    /// </summary>
    Task<(bool IsValid, string? ErrorMessage, List<string>? Warnings)> IFileCopyService.ValidateOptionsAsync(FileCopyOptions options)
    {
        var robocopyOptions = FileCopyOptionsMapper.ToRobocopyOptions(options);
        var (isValid, error) = ValidateOptions(robocopyOptions);

        List<string>? warnings = null;
        if (robocopyOptions.MirrorMode)
        {
            warnings = new List<string>
            {
                "⚠️ Mirror mode will delete files at destination that don't exist at source"
            };
        }

        return Task.FromResult((isValid, error, warnings));
    }

    /// <summary>
    /// Get operation description - returns robocopy command line.
    /// </summary>
    string IFileCopyService.GetOperationDescription(FileCopyOptions options)
    {
        var robocopyOptions = FileCopyOptionsMapper.ToRobocopyOptions(options);
        return BuildCommandLine(robocopyOptions);
    }

    /// <summary>
    /// Get robocopy engine capabilities.
    /// </summary>
    CopyEngineCapabilities IFileCopyService.GetCapabilities()
    {
        return new CopyEngineCapabilities
        {
            EngineType = CopyEngineType.Robocopy,
            Name = "Robocopy",
            Description = "Windows Robocopy - proven reliability with decades of optimization",
            SupportsParallelCopy = true,
            SupportsPauseResume = true,
            SupportsMirrorMode = true,
            SupportsBackupMode = true,
            SupportsNetworkOptimization = true,
            SupportsSymbolicLinks = true,
            SupportsSecurityInfo = true,
            RequiresExternalTool = true,
            SupportsByteProgressTracking = false,
            LocalPerformance = "Fast",
            NetworkPerformance = "Very Fast",
            HDDPerformance = "Fast",
            Platform = "Windows",
            MinimumWindowsVersion = "Windows Vista",
            OptimalScenarios = new List<string>
            {
                "Network share operations",
                "Backup operations with locked files",
                "Mirror/sync operations",
                "Operations requiring security preservation"
            },
            LimitedScenarios = new List<string>
            {
                "Local SSD operations (slower than native)",
                "Need byte-level progress tracking"
            }
        };
    }

    /// <summary>
    /// Get engine type.
    /// </summary>
    CopyEngineType IFileCopyService.GetEngineType()
    {
        return CopyEngineType.Robocopy;
    }

    /// <summary>
    /// Check if robocopy is available.
    /// </summary>
    bool IFileCopyService.IsAvailable()
    {
        return IsRobocopyAvailable();
    }

    #endregion
}
