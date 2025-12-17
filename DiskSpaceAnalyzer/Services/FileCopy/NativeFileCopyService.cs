using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DiskSpaceAnalyzer.Models.FileCopy;

namespace DiskSpaceAnalyzer.Services.FileCopy;

/// <summary>
/// Native C# file copy implementation using pure .NET async I/O.
/// Provides high-performance parallel copying with real-time byte-level progress tracking.
/// No external dependencies - works entirely within .NET runtime.
/// </summary>
public class NativeFileCopyService : IFileCopyService
{
    private readonly IFileIntegrityService _integrityService;
    private readonly IDirectoryScanService _scanService;
    
    // State management for pause/resume/cancel
    private bool _isPaused;
    private bool _isCancelled;
    private readonly object _stateLock = new();
    private DateTime _startTime;
    private DateTime _pauseStartTime;
    private TimeSpan _totalPausedDuration;
    
    // Progress tracking
    private long _totalFiles;
    private long _totalDirectories;
    private long _totalBytes;
    private long _filesCopied;
    private long _directoriesCopied;
    private long _bytesCopied;
    private long _filesFailed;
    private long _filesSkipped;
    private long _filesDeleted;
    private string _currentFile = string.Empty;
    private long _currentFileSize;
    private long _currentFileBytesCopied;
    
    // File queue for tracking
    private readonly ConcurrentBag<FileCopyError> _errors = new();
    private readonly List<FileOperationInfo> _filesToCopy = new();
    private readonly object _progressLock = new();
    
    // Thread-safe progress throttling
    private long _lastProgressReportTicks;
    private long _lastProgressReportBytes;
    
    // Configuration
    private const int DefaultBufferSize = 1024 * 1024; // 1 MB
    private const int ProgressReportIntervalMs = 100;
    private const long ProgressReportIntervalBytes = 10 * 1024 * 1024; // 10 MB
    
    public NativeFileCopyService(
        IFileIntegrityService integrityService,
        IDirectoryScanService scanService)
    {
        _integrityService = integrityService;
        _scanService = scanService;
    }
    
    #region IFileCopyService Implementation
    
    public async Task<FileCopyResult> CopyAsync(
        FileCopyOptions options,
        IProgress<FileCopyProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (options == null)
            throw new ArgumentNullException(nameof(options));
        
        // Initialize state
        ResetState();
        _startTime = DateTime.Now;
        
        var result = new FileCopyResult
        {
            StartTime = _startTime,
            State = FileCopyJobState.Scanning,
            EngineType = CopyEngineType.Native
        };
        
        try
        {
            // Phase 1: Validate options
            var (isValid, errorMessage, warnings) = await ValidateOptionsAsync(options);
            if (!isValid)
            {
                result.Success = false;
                result.State = FileCopyJobState.Failed;
                result.ExitCode = 1;
                result.ExitCodeMessage = errorMessage ?? "Validation failed";
                return result;
            }
            
            ReportProgress(progress, FileCopyJobState.Scanning, "Scanning source directory...");
            
            // Phase 2: Enumerate and filter files using shared scan service
            var scanOptions = new DirectoryScanOptions
            {
                SourcePath = options.SourcePath,
                DestinationPath = options.DestinationPath,
                IncludeSubdirectories = options.CopySubdirectories,
                ExcludeDirectories = options.ExcludeDirectories,
                ExcludeFiles = options.ExcludeFiles,
                MinFileSize = options.MinFileSize,
                MaxFileSize = options.MaxFileSize,
                BuildFileList = !options.CreateTreeOnly
            };
            
            var scanProgress = new Progress<DirectoryScanProgress>(p =>
            {
                ReportProgress(progress, FileCopyJobState.Scanning, 
                    $"Scanning... {p.FilesFound:N0} files, {FormatBytes(p.BytesFound)}");
            });
            
            var scanResult = await _scanService.ScanAsync(scanOptions, scanProgress, cancellationToken);
            
            // Transfer scan results and apply additional filtering
            _filesToCopy.Clear();
            _filesSkipped = 0;
            
            foreach (var file in scanResult.Files)
            {
                var fileOp = new FileOperationInfo
                {
                    SourcePath = file.SourcePath,
                    DestinationPath = file.DestinationPath,
                    RelativePath = file.RelativePath,
                    Size = file.Size,
                    LastModified = file.LastModified
                };
                
                // Apply post-scan filters
                
                // Check IncludeFiles whitelist (if specified, only include matching files)
                if (options.IncludeFiles.Count > 0)
                {
                    var fileName = Path.GetFileName(fileOp.SourcePath);
                    bool matches = false;
                    foreach (var pattern in options.IncludeFiles)
                    {
                        if (MatchesPattern(fileName, pattern))
                        {
                            matches = true;
                            break;
                        }
                    }
                    if (!matches)
                    {
                        _filesSkipped++;
                        continue;
                    }
                }
                
                // Check if file should be skipped (destination-based)
                if (ShouldSkipFile(fileOp, options))
                {
                    _filesSkipped++;
                    continue;
                }
                
                _filesToCopy.Add(fileOp);
            }
            
            // Update totals after filtering
            _totalFiles = _filesToCopy.Count;
            _totalDirectories = scanResult.TotalDirectories;
            _totalBytes = _filesToCopy.Sum(f => f.Size);
            
            // Add scan errors to our error collection
            foreach (var scanError in scanResult.Errors)
            {
                _errors.Add(new FileCopyError
                {
                    FilePath = scanError.Path,
                    Message = scanError.Message,
                    ErrorCode = -1,
                    Timestamp = scanError.Timestamp
                });
            }
            
            result.TotalFiles = _totalFiles;
            result.TotalDirectories = _totalDirectories;
            result.TotalBytes = _totalBytes;
            
            if (_totalFiles == 0 && !options.CreateTreeOnly)
            {
                result.Success = true;
                result.State = FileCopyJobState.Completed;
                result.ExitCode = 0;
                result.ExitCodeMessage = "No files to copy";
                result.SummaryMessage = "Source directory is empty or all files were excluded";
                return result;
            }
            
            ReportProgress(progress, FileCopyJobState.Running, $"Copying {_totalFiles:N0} files ({FormatBytes(_totalBytes)})...");
            
            // Phase 3: Start integrity verification if enabled
            EventHandler<IntegrityProgress>? integrityHandler = null;
            if (options.EnableIntegrityCheck && options.IntegrityCheckMethod != IntegrityCheckMethod.None)
            {
                _integrityService.Start(
                    options.IntegrityCheckMethod,
                    options.SourcePath,
                    options.DestinationPath,
                    _totalFiles, // Pass total file count for accurate progress calculation
                    cancellationToken);
                
                // Subscribe to progress events
                integrityHandler = (s, p) => OnIntegrityProgressChanged(progress, p);
                _integrityService.ProgressChanged += integrityHandler;
            }
            
            try
            {
                // Phase 4: Copy files in parallel
                await CopyFilesParallelAsync(options, progress, cancellationToken);
            
                // Phase 5: Handle mirror mode (delete extra files at destination)
                if (options.MirrorMode && !_isCancelled)
                {
                    await HandleMirrorModeAsync(options, progress, cancellationToken);
                }
                
                // Phase 6: Wait for integrity verification to complete
                if (options.EnableIntegrityCheck && options.IntegrityCheckMethod != IntegrityCheckMethod.None)
                {
                    ReportProgress(progress, FileCopyJobState.Verifying, "Verifying file integrity...");
                    await _integrityService.WaitForCompletionAsync();
                    
                    var integrityProgress = _integrityService.GetProgress();
                    result.IntegrityCheckEnabled = true;
                    result.IntegrityCheckCompleted = true;
                    result.IntegrityChecksPassed = integrityProgress.FilesPassed;
                    result.IntegrityChecksFailed = integrityProgress.FilesFailed;
                    result.IntegrityCheckDuration = integrityProgress.Elapsed;
                    
                    // Collect detailed failure information
                    result.IntegrityResults = _integrityService.GetResults();
                    
                    await _integrityService.StopAsync();
                }
            }
            finally
            {
                // Clean up event handler
                if (integrityHandler != null)
                {
                    _integrityService.ProgressChanged -= integrityHandler;
                }
            }
            
            // Build final result
            result.EndTime = DateTime.Now;
            result.Duration = result.EndTime - result.StartTime;
            result.ActiveDuration = result.Duration - _totalPausedDuration;
            result.PausedDuration = _totalPausedDuration;
            
            result.FilesCopied = _filesCopied;
            result.DirectoriesCopied = _directoriesCopied;
            result.BytesCopied = _bytesCopied;
            result.FilesFailed = _filesFailed;
            result.FilesSkipped = _filesSkipped;
            result.FilesDeleted = _filesDeleted;
            
            if (result.ActiveDuration.TotalSeconds > 0)
            {
                result.AverageBytesPerSecond = result.BytesCopied / result.ActiveDuration.TotalSeconds;
            }
            
            result.Errors.AddRange(_errors);
            
            // Determine final state
            if (_isCancelled)
            {
                result.State = FileCopyJobState.Cancelled;
                result.ExitCode = 1223; // ERROR_CANCELLED
                result.ExitCodeMessage = "Operation cancelled by user";
                result.Success = false;
            }
            else if (_filesFailed > 0)
            {
                result.State = FileCopyJobState.Completed;
                result.ExitCode = 1; // Partial success
                result.ExitCodeMessage = $"Completed with {_filesFailed} file failures";
                result.Success = _filesCopied > 0; // Success if at least some files copied
            }
            else
            {
                result.State = FileCopyJobState.Completed;
                result.ExitCode = 0;
                result.ExitCodeMessage = "All files copied successfully";
                result.Success = true;
            }
            
            result.SummaryMessage = GenerateSummaryMessage(result);
            
            // Final progress report
            ReportFinalProgress(progress, result);
            
            return result;
        }
        catch (OperationCanceledException)
        {
            result.State = FileCopyJobState.Cancelled;
            result.ExitCode = 1223;
            result.ExitCodeMessage = "Operation cancelled";
            result.Success = false;
            result.EndTime = DateTime.Now;
            result.Duration = result.EndTime - result.StartTime;
            return result;
        }
        catch (Exception ex)
        {
            result.State = FileCopyJobState.Failed;
            result.ExitCode = -1;
            result.ExitCodeMessage = $"Fatal error: {ex.Message}";
            result.Success = false;
            result.Errors.Add(new FileCopyError
            {
                FilePath = "N/A",
                Message = ex.Message,
                ErrorCode = ex.HResult,
                Timestamp = DateTime.Now
            });
            result.EndTime = DateTime.Now;
            result.Duration = result.EndTime - result.StartTime;
            return result;
        }
    }
    
    public void Pause()
    {
        lock (_stateLock)
        {
            if (_isPaused)
                return;
            
            _isPaused = true;
            _pauseStartTime = DateTime.Now;
        }
    }
    
    public void Resume()
    {
        lock (_stateLock)
        {
            if (!_isPaused)
                return;
            
            _isPaused = false;
            _totalPausedDuration += DateTime.Now - _pauseStartTime;
        }
    }
    
    public void Cancel()
    {
        lock (_stateLock)
        {
            _isCancelled = true;
            _isPaused = false; // Unpause to allow cancellation to proceed
        }
    }
    
    public async Task<(bool IsValid, string? ErrorMessage, List<string>? Warnings)> ValidateOptionsAsync(FileCopyOptions options)
    {
        var warnings = new List<string>();
        
        // Validate source path
        if (string.IsNullOrWhiteSpace(options.SourcePath))
            return (false, "Source path cannot be empty", null);
        
        if (!Directory.Exists(options.SourcePath))
            return (false, $"Source directory does not exist: {options.SourcePath}", null);
        
        // Validate destination path
        if (string.IsNullOrWhiteSpace(options.DestinationPath))
            return (false, "Destination path cannot be empty", null);
        
        // Check if source and destination are the same
        var sourceFull = Path.GetFullPath(options.SourcePath);
        var destFull = Path.GetFullPath(options.DestinationPath);
        
        if (sourceFull.Equals(destFull, StringComparison.OrdinalIgnoreCase))
            return (false, "Source and destination cannot be the same", null);
        
        // Check if destination is inside source
        if (destFull.StartsWith(sourceFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return (false, "Destination cannot be inside source directory", null);
        
        // Check source permissions
        try
        {
            _ = Directory.EnumerateFileSystemEntries(options.SourcePath).FirstOrDefault();
        }
        catch (UnauthorizedAccessException)
        {
            return (false, "Access denied to source directory", null);
        }
        catch (Exception ex)
        {
            return (false, $"Cannot access source directory: {ex.Message}", null);
        }
        
        // Check destination permissions (create if doesn't exist)
        try
        {
            if (!Directory.Exists(options.DestinationPath))
            {
                Directory.CreateDirectory(options.DestinationPath);
            }
            
            // Test write access
            var testFile = Path.Combine(options.DestinationPath, $".nativetest_{Guid.NewGuid():N}");
            await File.WriteAllTextAsync(testFile, "test");
            File.Delete(testFile);
        }
        catch (UnauthorizedAccessException)
        {
            return (false, "Access denied to destination directory", null);
        }
        catch (Exception ex)
        {
            return (false, $"Cannot write to destination directory: {ex.Message}", null);
        }
        
        // Check for conflicting options
        if (options.MirrorMode && options.MoveFiles)
        {
            return (false, "Mirror mode and Move mode cannot be used together", null);
        }
        
        // Warnings
        if (options.MirrorMode)
        {
            warnings.Add("WARNING: Mirror mode will DELETE files at destination that don't exist in source");
        }
        
        if (options.BackupMode)
        {
            warnings.Add("Backup mode (copying locked files) is not supported by Native engine");
        }
        
        if (options.CopySecurity)
        {
            warnings.Add("Copying NTFS security information is limited in Native engine");
        }
        
        // Check disk space (basic check) - using fast estimate
        try
        {
            var destDrive = new DriveInfo(Path.GetPathRoot(options.DestinationPath)!);
            if (destDrive.IsReady)
            {
                // Quick estimate without blocking - respects exclusions
                var sourceSize = await _scanService.EstimateSizeAsync(
                    options.SourcePath,
                    options.ExcludeDirectories,
                    options.ExcludeFiles,
                    CancellationToken.None);
                
                if (sourceSize > 0 && destDrive.AvailableFreeSpace < sourceSize)
                {
                    warnings.Add($"Destination may have insufficient space (Free: {FormatBytes(destDrive.AvailableFreeSpace)}, Estimated need: {FormatBytes(sourceSize)})");
                }
            }
        }
        catch
        {
            // Ignore disk space check errors (non-critical)
        }
        
        return (true, null, warnings.Count > 0 ? warnings : null);
    }
    
    public string GetOperationDescription(FileCopyOptions options)
    {
        var parts = new List<string>();
        
        if (options.MirrorMode)
            parts.Add("MIRROR");
        else if (options.MoveFiles)
            parts.Add("MOVE");
        else
            parts.Add("COPY");
        
        parts.Add($"from '{options.SourcePath}'");
        parts.Add($"to '{options.DestinationPath}'");
        
        if (options.UseParallelCopy)
            parts.Add($"using {options.ParallelismDegree} parallel threads");
        
        if (options.EnableIntegrityCheck)
            parts.Add($"with {options.IntegrityCheckMethod} verification");
        
        var filters = new List<string>();
        if (options.ExcludeDirectories.Count > 0)
            filters.Add($"{options.ExcludeDirectories.Count} directory exclusions");
        if (options.ExcludeFiles.Count > 0)
            filters.Add($"{options.ExcludeFiles.Count} file exclusions");
        if (options.MinFileSize.HasValue)
            filters.Add($"min size {FormatBytes(options.MinFileSize.Value)}");
        if (options.MaxFileSize.HasValue)
            filters.Add($"max size {FormatBytes(options.MaxFileSize.Value)}");
        
        if (filters.Count > 0)
            parts.Add($"(filters: {string.Join(", ", filters)})");
        
        return string.Join(" ", parts);
    }
    
    public CopyEngineCapabilities GetCapabilities()
    {
        return NativeEngineCapabilities.Instance;
    }
    
    public CopyEngineType GetEngineType()
    {
        return CopyEngineType.Native;
    }
    
    public bool IsAvailable()
    {
        // Native engine is always available (pure .NET)
        return true;
    }
    
    #endregion
    
    #region File Filtering Helpers
    
    /// <summary>
    /// Check if a file should be skipped because it's already up-to-date.
    /// </summary>
    private bool ShouldSkipFile(FileOperationInfo fileOp, FileCopyOptions options)
    {
        if (!File.Exists(fileOp.DestinationPath))
            return false;
        
        var destInfo = new FileInfo(fileOp.DestinationPath);
        
        // ExcludeOlder: skip if destination is newer or same
        if (options.ExcludeOlder && destInfo.LastWriteTimeUtc >= fileOp.LastModified)
            return true;
        
        // ExcludeNewer: skip if source is newer
        if (options.ExcludeNewer && fileOp.LastModified > destInfo.LastWriteTimeUtc)
            return true;
        
        // Skip if identical (same size and timestamp)
        if (destInfo.Length == fileOp.Size && 
            Math.Abs((destInfo.LastWriteTimeUtc - fileOp.LastModified).TotalSeconds) < 2)
        {
            return true;
        }
        
        // Skip read-only files with matching size (e.g., Git objects, immutable content)
        // Read-only files are typically content-addressed or database files that shouldn't change
        if (destInfo.IsReadOnly && destInfo.Length == fileOp.Size)
        {
            return true;
        }
        
        return false;
    }
    
    /// <summary>
    /// Simple wildcard pattern matching for file names.
    /// Supports * (any characters) and ? (single character).
    /// </summary>
    private bool MatchesPattern(string name, string pattern)
    {
        if (pattern == "*")
            return true;
        
        if (!pattern.Contains('*') && !pattern.Contains('?'))
            return name.Equals(pattern, StringComparison.OrdinalIgnoreCase);
        
        // Convert wildcard pattern to regex
        var regexPattern = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
            .Replace("\\*", ".*")
            .Replace("\\?", ".") + "$";
        
        return System.Text.RegularExpressions.Regex.IsMatch(name, regexPattern, 
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }
    
    #endregion
    
    #region Parallel File Copying
    
    private async Task CopyFilesParallelAsync(
        FileCopyOptions options,
        IProgress<FileCopyProgress>? progress,
        CancellationToken cancellationToken)
    {
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = options.UseParallelCopy ? options.ParallelismDegree : 1,
            CancellationToken = cancellationToken
        };
        
        // Initialize thread-safe progress tracking
        Interlocked.Exchange(ref _lastProgressReportTicks, DateTime.Now.Ticks);
        Interlocked.Exchange(ref _lastProgressReportBytes, 0L);
        
        // Thread-safe directory creation cache to avoid redundant calls
        var createdDirectories = new ConcurrentDictionary<string, byte>();
        
        await Parallel.ForEachAsync(_filesToCopy, parallelOptions, async (fileOp, ct) =>
        {
            // Check pause state
            while (_isPaused && !_isCancelled)
            {
                await Task.Delay(100, ct);
            }
            
            if (_isCancelled || ct.IsCancellationRequested)
                return;
            
            try
            {
                // Update current file
                lock (_progressLock)
                {
                    _currentFile = fileOp.RelativePath;
                    _currentFileSize = fileOp.Size;
                    _currentFileBytesCopied = 0;
                }
                
                // Ensure destination directory exists (thread-safe)
                var destDir = Path.GetDirectoryName(fileOp.DestinationPath);
                if (!string.IsNullOrEmpty(destDir) && createdDirectories.TryAdd(destDir, 0))
                {
                    // Only one thread will succeed in adding to the dictionary
                    Directory.CreateDirectory(destDir); // Safe to call even if exists
                    Interlocked.Increment(ref _directoriesCopied);
                }
                
                // Copy the file with retry logic
                bool success = false;
                int attempts = 0;
                Exception? lastException = null;
                
                while (attempts <= options.RetryCount && !success && !_isCancelled)
                {
                    try
                    {
                        await CopyFileWithProgressAsync(fileOp, options, ct);
                        success = true;
                        
                        Interlocked.Increment(ref _filesCopied);
                        
                        // Queue for integrity verification
                        if (options.EnableIntegrityCheck && options.IntegrityCheckMethod != IntegrityCheckMethod.None)
                        {
                            _integrityService.QueueFile(new FileCopyInfo
                            {
                                RelativePath = fileOp.RelativePath,
                                SourceFullPath = fileOp.SourcePath,
                                DestinationFullPath = fileOp.DestinationPath,
                                FileSize = fileOp.Size,
                                CopiedAt = DateTime.Now,
                                SourceLastModified = fileOp.LastModified
                            });
                        }
                        
                        // Handle move mode
                        if (options.MoveFiles || options.MoveFilesAndDirectories)
                        {
                            File.Delete(fileOp.SourcePath);
                        }
                    }
                    catch (Exception ex)
                    {
                        lastException = ex;
                        attempts++;
                        
                        if (attempts <= options.RetryCount)
                        {
                            await Task.Delay(options.RetryWaitSeconds * 1000, ct);
                        }
                    }
                }
                
                if (!success)
                {
                    Interlocked.Increment(ref _filesFailed);
                    _errors.Add(new FileCopyError
                    {
                        FilePath = fileOp.RelativePath,
                        Message = lastException?.Message ?? "Unknown error",
                        ErrorCode = lastException?.HResult ?? -1,
                        Timestamp = DateTime.Now
                    });
                }
                
                // Report progress periodically (thread-safe)
                var currentTicks = DateTime.Now.Ticks;
                var lastReportTicks = Interlocked.Read(ref _lastProgressReportTicks);
                var elapsedMs = (currentTicks - lastReportTicks) / TimeSpan.TicksPerMillisecond;
                
                var currentBytes = Interlocked.Read(ref _bytesCopied);
                var lastBytes = Interlocked.Read(ref _lastProgressReportBytes);
                var bytesDelta = currentBytes - lastBytes;
                
                if (elapsedMs >= ProgressReportIntervalMs || bytesDelta >= ProgressReportIntervalBytes)
                {
                    // Try to update the last report time atomically
                    if (Interlocked.CompareExchange(ref _lastProgressReportTicks, currentTicks, lastReportTicks) == lastReportTicks)
                    {
                        // We won the race - report progress
                        Interlocked.Exchange(ref _lastProgressReportBytes, currentBytes);
                        ReportProgress(progress, FileCopyJobState.Running, $"Copying: {fileOp.RelativePath}");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected during cancellation
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref _filesFailed);
                _errors.Add(new FileCopyError
                {
                    FilePath = fileOp.RelativePath,
                    Message = ex.Message,
                    ErrorCode = ex.HResult,
                    Timestamp = DateTime.Now
                });
            }
        });
    }
    
    private async Task CopyFileWithProgressAsync(
        FileOperationInfo fileOp,
        FileCopyOptions options,
        CancellationToken cancellationToken)
    {
        var bufferSize = DefaultBufferSize;
        
        // Get custom buffer size from extended options if specified
        if (options.ExtendedOptions.TryGetValue("NativeBufferSize", out var bufferObj) && bufferObj is int customBuffer)
        {
            bufferSize = customBuffer;
        }
        
        var buffer = new byte[bufferSize];
        
        // Remove read-only attribute from destination if it exists (e.g., Git objects)
        if (File.Exists(fileOp.DestinationPath))
        {
            var destFileInfo = new FileInfo(fileOp.DestinationPath);
            if (destFileInfo.IsReadOnly)
            {
                destFileInfo.IsReadOnly = false;
            }
        }
        
        using var sourceStream = new FileStream(fileOp.SourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var destStream = new FileStream(fileOp.DestinationPath, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        
        long totalBytesRead = 0;
        int bytesRead;
        
        while ((bytesRead = await sourceStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
        {
            await destStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
            
            totalBytesRead += bytesRead;
            Interlocked.Add(ref _bytesCopied, bytesRead);
            
            lock (_progressLock)
            {
                _currentFileBytesCopied = totalBytesRead;
            }
        }
        
        // Preserve attributes and timestamps if requested
        if (options.PreserveAttributes)
        {
            try
            {
                var sourceInfo = new FileInfo(fileOp.SourcePath);
                var destInfo = new FileInfo(fileOp.DestinationPath);
                
                destInfo.CreationTimeUtc = sourceInfo.CreationTimeUtc;
                destInfo.LastWriteTimeUtc = sourceInfo.LastWriteTimeUtc;
                destInfo.LastAccessTimeUtc = sourceInfo.LastAccessTimeUtc;
                destInfo.Attributes = sourceInfo.Attributes;
            }
            catch
            {
                // Ignore attribute preservation errors
            }
        }
    }
    
    #endregion
    
    #region Mirror and Move Mode
    
    private async Task HandleMirrorModeAsync(
        FileCopyOptions options,
        IProgress<FileCopyProgress>? progress,
        CancellationToken cancellationToken)
    {
        ReportProgress(progress, FileCopyJobState.Running, "Mirror mode: scanning for extra files to delete...");
        
        var sourceFiles = new HashSet<string>(
            _filesToCopy.Select(f => f.RelativePath),
            StringComparer.OrdinalIgnoreCase);
        
        var filesToDelete = new List<string>();
        await Task.Run(() => FindExtraFiles(options.DestinationPath, "", sourceFiles, filesToDelete), cancellationToken);
        
        if (filesToDelete.Count > 0)
        {
            ReportProgress(progress, FileCopyJobState.Running, $"Mirror mode: deleting {filesToDelete.Count} extra files...");
            
            foreach (var fileToDelete in filesToDelete)
            {
                try
                {
                    File.Delete(fileToDelete);
                    Interlocked.Increment(ref _filesDeleted);
                }
                catch (Exception ex)
                {
                    _errors.Add(new FileCopyError
                    {
                        FilePath = fileToDelete,
                        Message = $"Failed to delete: {ex.Message}",
                        ErrorCode = ex.HResult,
                        Timestamp = DateTime.Now
                    });
                }
            }
        }
        
        // Clean up empty directories
        CleanEmptyDirectories(options.DestinationPath);
    }
    
    private void FindExtraFiles(string destRoot, string relativePath, HashSet<string> sourceFiles, List<string> filesToDelete)
    {
        var currentPath = string.IsNullOrEmpty(relativePath)
            ? destRoot
            : Path.Combine(destRoot, relativePath);
        
        try
        {
            // Check files
            foreach (var file in Directory.GetFiles(currentPath))
            {
                var fileName = Path.GetFileName(file);
                var relPath = string.IsNullOrEmpty(relativePath)
                    ? fileName
                    : Path.Combine(relativePath, fileName);
                
                if (!sourceFiles.Contains(relPath))
                {
                    filesToDelete.Add(file);
                }
            }
            
            // Recurse into subdirectories
            foreach (var dir in Directory.GetDirectories(currentPath))
            {
                var dirName = Path.GetFileName(dir);
                var newRelPath = string.IsNullOrEmpty(relativePath)
                    ? dirName
                    : Path.Combine(relativePath, dirName);
                
                FindExtraFiles(destRoot, newRelPath, sourceFiles, filesToDelete);
            }
        }
        catch
        {
            // Ignore errors during extra file scanning
        }
    }
    
    private void CleanEmptyDirectories(string path)
    {
        try
        {
            foreach (var dir in Directory.GetDirectories(path))
            {
                CleanEmptyDirectories(dir);
                
                if (!Directory.EnumerateFileSystemEntries(dir).Any())
                {
                    Directory.Delete(dir);
                }
            }
        }
        catch
        {
            // Ignore errors during cleanup
        }
    }
    
    #endregion
    
    #region Progress Reporting
    
    private void ReportProgress(IProgress<FileCopyProgress>? progress, FileCopyJobState state, string message)
    {
        if (progress == null)
            return;
        
        var activeTime = DateTime.Now - _startTime - _totalPausedDuration;
        
        // Get current verification progress (always check, not just when IsActive)
        var integrityProgress = _integrityService.GetProgress();
        long filesVerified = integrityProgress.FilesVerified;
        long filesVerifiedPassed = integrityProgress.FilesPassed;
        long filesVerifiedFailed = integrityProgress.FilesFailed;
        long filesRetrying = integrityProgress.FilesRetrying;
        long totalFilesForVerification = integrityProgress.TotalFiles;
        string currentVerificationFile = integrityProgress.CurrentFile;
        
        var progressData = new FileCopyProgress
        {
            State = state,
            StatusMessage = message,
            EngineType = CopyEngineType.Native,
            FilesCopied = _filesCopied,
            TotalFiles = _totalFiles,
            DirectoriesCopied = _directoriesCopied,
            TotalDirectories = _totalDirectories,
            FilesFailed = _filesFailed,
            FilesSkipped = _filesSkipped,
            BytesCopied = _bytesCopied,
            TotalBytes = _totalBytes,
            CurrentFile = _currentFile,
            CurrentFileBytesCopied = _currentFileBytesCopied,
            CurrentFileSize = _currentFileSize,
            StartTime = _startTime,
            Elapsed = DateTime.Now - _startTime,
            PausedDuration = _totalPausedDuration,
            // Include verification progress
            FilesVerified = filesVerified,
            FilesVerifiedPassed = filesVerifiedPassed,
            FilesVerifiedFailed = filesVerifiedFailed,
            FilesRetrying = filesRetrying,
            TotalFilesForVerification = totalFilesForVerification,
            CurrentVerificationFile = currentVerificationFile
        };
        
        progress.Report(progressData);
    }
    
    private void ReportFinalProgress(IProgress<FileCopyProgress>? progress, FileCopyResult result)
    {
        if (progress == null)
            return;
        
        var progressData = new FileCopyProgress
        {
            State = result.State,
            StatusMessage = result.SummaryMessage,
            EngineType = CopyEngineType.Native,
            FilesCopied = result.FilesCopied,
            TotalFiles = result.TotalFiles,
            DirectoriesCopied = result.DirectoriesCopied,
            TotalDirectories = result.TotalDirectories,
            FilesFailed = result.FilesFailed,
            FilesSkipped = result.FilesSkipped,
            BytesCopied = result.BytesCopied,
            TotalBytes = result.TotalBytes,
            CurrentFile = "",
            StartTime = result.StartTime,
            Elapsed = result.Duration,
            PausedDuration = result.PausedDuration,
            // Include final verification results
            FilesVerified = result.IntegrityCheckEnabled ? result.IntegrityChecksPassed + result.IntegrityChecksFailed : 0,
            FilesVerifiedPassed = result.IntegrityChecksPassed,
            FilesVerifiedFailed = result.IntegrityChecksFailed,
            FilesRetrying = 0,
            CurrentVerificationFile = ""
        };
        
        progress.Report(progressData);
    }
    
    private void OnIntegrityProgressChanged(IProgress<FileCopyProgress>? progress, IntegrityProgress integrityProgress)
    {
        if (progress == null)
            return;
        
        // Update progress with verification info
        var message = $"Verifying: {integrityProgress.FilesVerified}/{integrityProgress.TotalFiles} files checked";
        var activeTime = DateTime.Now - _startTime - _totalPausedDuration;
        
        var progressData = new FileCopyProgress
        {
            State = FileCopyJobState.Verifying,
            StatusMessage = message,
            EngineType = CopyEngineType.Native,
            FilesCopied = _filesCopied,
            TotalFiles = _totalFiles,
            DirectoriesCopied = _directoriesCopied,
            TotalDirectories = _totalDirectories,
            FilesFailed = _filesFailed,
            FilesSkipped = _filesSkipped,
            BytesCopied = _bytesCopied,
            TotalBytes = _totalBytes,
            CurrentFile = _currentFile,
            CurrentFileBytesCopied = _currentFileBytesCopied,
            CurrentFileSize = _currentFileSize,
            StartTime = _startTime,
            Elapsed = DateTime.Now - _startTime,
            PausedDuration = _totalPausedDuration,
            // Verification progress
            FilesVerified = integrityProgress.FilesVerified,
            FilesVerifiedPassed = integrityProgress.FilesPassed,
            FilesVerifiedFailed = integrityProgress.FilesFailed,
            FilesRetrying = integrityProgress.FilesRetrying,
            TotalFilesForVerification = integrityProgress.TotalFiles,
            CurrentVerificationFile = integrityProgress.CurrentFile
        };
        
        progress.Report(progressData);
    }
    
    #endregion
    
    #region Helper Methods
    
    private void ResetState()
    {
        _isPaused = false;
        _isCancelled = false;
        _totalPausedDuration = TimeSpan.Zero;
        
        _totalFiles = 0;
        _totalDirectories = 0;
        _totalBytes = 0;
        _filesCopied = 0;
        _directoriesCopied = 0;
        _bytesCopied = 0;
        _filesFailed = 0;
        _filesSkipped = 0;
        _filesDeleted = 0;
        _currentFile = string.Empty;
        _currentFileSize = 0;
        _currentFileBytesCopied = 0;
        
        _errors.Clear();
        _filesToCopy.Clear();
    }
    
    private string GenerateSummaryMessage(FileCopyResult result)
    {
        if (result.State == FileCopyJobState.Cancelled)
            return $"Cancelled: {result.FilesCopied:N0} of {result.TotalFiles:N0} files copied";
        
        if (result.FilesFailed > 0)
            return $"Completed with errors: {result.FilesCopied:N0} files copied, {result.FilesFailed:N0} failed ({FormatBytes(result.BytesCopied)} in {FormatDuration(result.ActiveDuration)})";
        
        return $"Success: {result.FilesCopied:N0} files copied ({FormatBytes(result.BytesCopied)} in {FormatDuration(result.ActiveDuration)} at {FormatBytes((long)result.AverageBytesPerSecond)}/s)";
    }
    
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
        return $"{len:0.##} {sizes[order]}";
    }
    
    private string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
            return $"{(int)duration.TotalHours}h {duration.Minutes}m {duration.Seconds}s";
        if (duration.TotalMinutes >= 1)
            return $"{(int)duration.TotalMinutes}m {duration.Seconds}s";
        return $"{duration.Seconds}s";
    }
    
    #endregion
    
    #region Helper Classes
    
    private class FileOperationInfo
    {
        public string SourcePath { get; set; } = string.Empty;
        public string DestinationPath { get; set; } = string.Empty;
        public string RelativePath { get; set; } = string.Empty;
        public long Size { get; set; }
        public DateTime LastModified { get; set; }
    }
    
    #endregion
}
