using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using DiskSpaceAnalyzer.Models.FileCopy;

namespace DiskSpaceAnalyzer.Services.FileCopy;

/// <summary>
///     Implementation of IFileIntegrityService for verifying file integrity after copy operations.
///     Engine-agnostic - can be used by any copy engine (Robocopy, Native, etc.).
///     Supports multiple verification methods from quick metadata checks to cryptographic hashes.
/// </summary>
public class FileIntegrityService : IFileIntegrityService
{
    // Thresholds for file size classification
    private const long SmallFileThreshold = 10485760; // 10 MB
    private const long LargeFileThreshold = 104857600; // 100 MB

    // Fixed concurrency settings - conservative values work well for both SSD and HDD
    private const int SmallFileConcurrency = 8; // High concurrency for small files
    private const int LargeFileConcurrency = 4; // Lower concurrency for large files
    private const int MaxRetryAttempts = 3;

    private readonly OptimizedHashCalculator _hashCalculator = new();
    private readonly ConcurrentBag<string> _queueFailures = [];

    private readonly List<IntegrityCheckResult> _results = [];
    private readonly object _resultsLock = new();
    private readonly object _statsLock = new();

    // Performance tracking
    private readonly Stopwatch _verificationTimer = new();
    private long _bytesVerified;
    private CancellationTokenSource? _cancellationTokenSource;
    private string _currentFile = string.Empty;
    private string _destinationPath = string.Empty;
    private long _filesFailed;
    private long _filesPassed;

    // Retry tracking
    private long _filesRetrying;

    // Statistics
    private long _filesVerified;
    private Channel<FileCopyInfo>? _largeFileChannel;
    private DateTime _lastProgressUpdate = DateTime.Now;

    private IntegrityCheckMethod _method = IntegrityCheckMethod.None;
    private Channel<FileCopyInfo>? _retryChannel;

    // Channels for multi-tiered processing pipeline
    private Channel<FileCopyInfo>? _smallFileChannel;
    private string _sourcePath = string.Empty;
    private long _totalBytesQueued;
    private long _totalFilesQueued; // Track total files queued for accurate percentage
    private long _totalRetryAttempts;

    private List<Task>? _workerTasks;

    public event EventHandler<IntegrityCheckResult>? FileVerified;
    public event EventHandler<IntegrityProgress>? ProgressChanged;

    public void Start(IntegrityCheckMethod method, string sourcePath, string destinationPath, long totalFiles,
        CancellationToken cancellationToken)
    {
        if (method == IntegrityCheckMethod.None)
            return;

        _method = method;
        _sourcePath = sourcePath;
        _destinationPath = destinationPath;
        IsActive = true;
        IsComplete = false;

        // Reset statistics
        _filesVerified = 0;
        _filesPassed = 0;
        _filesFailed = 0;
        _bytesVerified = 0;
        _totalBytesQueued = 0;
        _totalFilesQueued = totalFiles; // Initialize with expected total for accurate progress

        lock (_resultsLock)
        {
            _results.Clear();
        }

        _queueFailures.Clear();

        // Create unbounded channels - no queue limits, backpressure handled by worker availability
        // Memory impact is minimal as we're only queuing lightweight FileCopyInfo metadata
        _smallFileChannel = Channel.CreateUnbounded<FileCopyInfo>();
        _largeFileChannel = Channel.CreateUnbounded<FileCopyInfo>();
        _retryChannel = Channel.CreateUnbounded<FileCopyInfo>();

        // Reset retry tracking
        _filesRetrying = 0;
        _totalRetryAttempts = 0;

        // Start verification tasks
        _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _verificationTimer.Restart();

        _workerTasks = [];

        // Start small file workers (high concurrency)
        for (var i = 0; i < SmallFileConcurrency; i++)
            _workerTasks.Add(Task.Run(() => SmallFileWorkerAsync(_cancellationTokenSource.Token),
                _cancellationTokenSource.Token));

        // Start large file workers (lower concurrency)
        for (var i = 0; i < LargeFileConcurrency; i++)
            _workerTasks.Add(Task.Run(() => LargeFileWorkerAsync(_cancellationTokenSource.Token),
                _cancellationTokenSource.Token));

        // Start retry worker
        _workerTasks.Add(Task.Run(() => RetryWorkerAsync(_cancellationTokenSource.Token),
            _cancellationTokenSource.Token));
    }

    public void QueueFile(FileCopyInfo fileInfo)
    {
        if (_method == IntegrityCheckMethod.None || !IsActive)
            return;

        // Track total bytes (but not files - total file count is set in Start())
        Interlocked.Add(ref _totalBytesQueued, fileInfo.FileSize);

        // Route to appropriate channel based on file size
        var channel = fileInfo.FileSize < SmallFileThreshold ? _smallFileChannel : _largeFileChannel;

        // Write to unbounded channel - should never fail unless channel is closed
        if (!channel!.Writer.TryWrite(fileInfo))
        {
            // Channel is closed - track this failure
            _queueFailures.Add(fileInfo.RelativePath);

            // Create and store failed result for UI display
            var failedResult = new IntegrityCheckResult
            {
                RelativePath = fileInfo.RelativePath,
                DestinationPath = fileInfo.DestinationFullPath,
                Method = _method,
                FileSize = fileInfo.FileSize,
                VerifiedAt = DateTime.Now,
                IsValid = false,
                ErrorMessage = "Failed to queue for verification - service stopped"
            };

            lock (_resultsLock)
            {
                _results.Add(failedResult);
            }
        }

        // Report progress update
        ReportProgress();
    }

    public IntegrityProgress GetProgress()
    {
        lock (_statsLock)
        {
            var elapsed = _verificationTimer.Elapsed.TotalSeconds;
            var filesPerSecond = elapsed > 0 ? _filesVerified / elapsed : 0;
            var bytesPerSecond = elapsed > 0 ? _bytesVerified / elapsed : 0;
            var throughputMBps = bytesPerSecond / (1024.0 * 1024.0);

            // Calculate total queued files from both channels
            // Note: Reader.Count can throw if channel is closed, so catch and default to 0
            var smallFileQueueCount = 0;
            var largeFileQueueCount = 0;
            try
            {
                smallFileQueueCount = _smallFileChannel?.Reader.Count ?? 0;
                largeFileQueueCount = _largeFileChannel?.Reader.Count ?? 0;
            }
            catch (Exception)
            {
                // Channels are closed or otherwise inaccessible - no items queued
            }

            var totalQueued = smallFileQueueCount + largeFileQueueCount;

            // Calculate queue failures
            var queueFailureCount = _queueFailures.Count;

            return new IntegrityProgress
            {
                FilesVerified = _filesVerified,
                TotalFiles = _totalFilesQueued,
                FilesPassed = _filesPassed,
                FilesFailed = _filesFailed + queueFailureCount,
                FilesQueued = totalQueued,
                FilesRetrying = _filesRetrying,
                TotalRetryAttempts = _totalRetryAttempts,
                BytesVerified = _bytesVerified,
                TotalBytes = _totalBytesQueued,
                CurrentFile = _currentFile,
                Method = _method,
                IsActive = IsActive,
                IsComplete = totalQueued == 0 && _filesRetrying == 0,
                StartTime = _verificationTimer.Elapsed == TimeSpan.Zero
                    ? DateTime.Now
                    : DateTime.Now - _verificationTimer.Elapsed,
                Elapsed = _verificationTimer.Elapsed
            };
        }
    }

    public async Task WaitForCompletionAsync()
    {
        if (_workerTasks == null || _workerTasks.Count == 0)
            return;

        // Signal that no more files will be queued
        _smallFileChannel?.Writer.Complete();
        _largeFileChannel?.Writer.Complete();
        _retryChannel?.Writer.Complete();

        try
        {
            // Wait for all workers to finish processing
            await Task.WhenAll(_workerTasks);
        }
        catch (OperationCanceledException)
        {
            // Expected when cancelled
        }
    }

    public async Task StopAsync()
    {
        IsActive = false;

        // Signal channels to complete
        _smallFileChannel?.Writer.Complete();
        _largeFileChannel?.Writer.Complete();
        _retryChannel?.Writer.Complete();

        _cancellationTokenSource?.Cancel();

        if (_workerTasks != null && _workerTasks.Count > 0)
            try
            {
                await Task.WhenAll(_workerTasks);
            }
            catch (OperationCanceledException)
            {
                // Expected
            }

        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;
        _workerTasks = null;

        _hashCalculator?.Dispose();

        IsComplete = true;
        ReportProgress();
    }

    /// <summary>
    ///     Get all verification results.
    /// </summary>
    public List<IntegrityCheckResult> GetResults()
    {
        lock (_resultsLock)
        {
            return [.._results];
        }
    }

    /// <summary>
    ///     Get only failed verification results.
    /// </summary>
    public List<IntegrityCheckResult> GetFailedResults()
    {
        lock (_resultsLock)
        {
            return _results.Where(r => !r.IsValid).ToList();
        }
    }

    /// <summary>
    ///     Queue multiple files for verification in batch.
    /// </summary>
    public void QueueFiles(IEnumerable<FileCopyInfo> files)
    {
        foreach (var file in files) QueueFile(file);
    }

    /// <summary>
    ///     Whether the service is currently active.
    /// </summary>
    public bool IsActive { get; private set; }

    /// <summary>
    ///     Whether all queued verifications have completed.
    /// </summary>
    public bool IsComplete { get; private set; }

    /// <summary>
    ///     Worker for small files - optimized for high throughput with many concurrent operations.
    /// </summary>
    private async Task SmallFileWorkerAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var fileInfo in _smallFileChannel!.Reader.ReadAllAsync(cancellationToken))
                await VerifyFileAsync(fileInfo, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Expected when cancelled
        }
    }

    /// <summary>
    ///     Worker for large files - optimized for sustained throughput with lower concurrency.
    /// </summary>
    private async Task LargeFileWorkerAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var fileInfo in _largeFileChannel!.Reader.ReadAllAsync(cancellationToken))
                await VerifyFileAsync(fileInfo, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Expected when cancelled
        }
    }

    /// <summary>
    ///     Worker for retry queue - waits for retry time and requeues files.
    /// </summary>
    private async Task RetryWorkerAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var fileInfo in _retryChannel!.Reader.ReadAllAsync(cancellationToken))
            {
                // Wait until retry time
                var delay = fileInfo.NextRetryTime - DateTime.Now;
                if (delay > TimeSpan.Zero) await Task.Delay(delay, cancellationToken);

                // Decrement retry counter as we're about to process it
                Interlocked.Decrement(ref _filesRetrying);

                // Route to appropriate channel based on file size
                var channel = fileInfo.FileSize < SmallFileThreshold ? _smallFileChannel : _largeFileChannel;

                // Try to write to channel - if it fails, mark as failed
                if (!channel!.Writer.TryWrite(fileInfo))
                {
                    // Channel is closed - create failed result and store it
                    var failedResult = new IntegrityCheckResult
                    {
                        RelativePath = fileInfo.RelativePath,
                        DestinationPath = fileInfo.DestinationFullPath,
                        Method = _method,
                        FileSize = fileInfo.FileSize,
                        VerifiedAt = DateTime.Now,
                        IsValid = false,
                        ErrorMessage = "Verification cancelled - retry queue closed"
                    };

                    lock (_statsLock)
                    {
                        _filesFailed++;
                    }

                    lock (_resultsLock)
                    {
                        _results.Add(failedResult);
                    }

                    // Raise events
                    FileVerified?.Invoke(this, failedResult);
                }

                ReportProgress();
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when cancelled
        }
    }

    private async Task VerifyFileAsync(FileCopyInfo fileInfo, CancellationToken cancellationToken)
    {
        var startTime = DateTime.Now;
        var sw = Stopwatch.StartNew();

        lock (_statsLock)
        {
            _currentFile = fileInfo.RelativePath;
        }

        var result = new IntegrityCheckResult
        {
            RelativePath = fileInfo.RelativePath,
            DestinationPath = fileInfo.DestinationFullPath,
            Method = _method,
            FileSize = fileInfo.FileSize,
            VerifiedAt = startTime
        };

        try
        {
            // Check if destination file exists
            if (!File.Exists(fileInfo.DestinationFullPath))
            {
                result.IsValid = false;
                result.ErrorMessage = "Destination file not found";
            }
            else
            {
                // Perform verification based on method
                result.IsValid = _method switch
                {
                    IntegrityCheckMethod.Metadata => await VerifyMetadataAsync(fileInfo, result, cancellationToken),
                    IntegrityCheckMethod.MD5 => await VerifyHashAsync(fileInfo, result, HashAlgorithmName.MD5,
                        cancellationToken),
                    IntegrityCheckMethod.SHA256 => await VerifyHashAsync(fileInfo, result, HashAlgorithmName.SHA256,
                        cancellationToken),
                    IntegrityCheckMethod.XXHash64 => await VerifyHashAsync(fileInfo, result,
                        OptimizedHashCalculator.XXHash64, cancellationToken),
                    IntegrityCheckMethod.Blake3 => await VerifyHashAsync(fileInfo, result,
                        OptimizedHashCalculator.Blake3, cancellationToken),
                    _ => false
                };
            }
        }
        catch (OperationCanceledException)
        {
            // Don't retry on cancellation - just rethrow
            throw;
        }
        catch (Exception ex) when (fileInfo.AttemptCount < MaxRetryAttempts)
        {
            // Retry on any error - could be transient (file locked, network issue, etc.)
            // Log exception type and details for debugging
            Debug.WriteLine(
                $"Verification error (attempt {fileInfo.AttemptCount + 1}/{MaxRetryAttempts}): {ex.GetType().Name} - {ex.Message}");
            await QueueForRetryAsync(fileInfo, $"Verification error (will retry): {ex.Message}", cancellationToken);
            return; // Don't count as failed yet
        }
        catch (Exception ex)
        {
            // All retries exhausted or non-retryable error
            result.IsValid = false;
            result.ErrorMessage = ex.Message;
        }

        sw.Stop();
        result.Duration = sw.Elapsed;

        // Update statistics
        lock (_statsLock)
        {
            _filesVerified++;
            _bytesVerified += fileInfo.FileSize;

            if (result.IsValid)
                _filesPassed++;
            else
                _filesFailed++;

            _currentFile = string.Empty;
        }

        // Store result
        lock (_resultsLock)
        {
            _results.Add(result);
        }

        // Raise events
        FileVerified?.Invoke(this, result);
        ReportProgress();
    }

    private async Task<bool> VerifyMetadataAsync(FileCopyInfo fileInfo, IntegrityCheckResult result,
        CancellationToken cancellationToken)
    {
        await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            var sourceInfo = new FileInfo(fileInfo.SourceFullPath);
            var destInfo = new FileInfo(fileInfo.DestinationFullPath);

            // Check file size
            if (sourceInfo.Length != destInfo.Length)
            {
                result.ErrorMessage = $"Size mismatch: source={sourceInfo.Length}, dest={destInfo.Length}";
                return false;
            }

            // Check last modified time (allow 2 second tolerance for filesystem precision)
            var timeDiff = Math.Abs((sourceInfo.LastWriteTime - destInfo.LastWriteTime).TotalSeconds);
            if (timeDiff > 2)
            {
                result.ErrorMessage = $"Timestamp mismatch: diff={timeDiff:F1}s";
                return false;
            }

            return true;
        }, cancellationToken);

        return result.ErrorMessage == null;
    }

    private async Task<bool> VerifyHashAsync(FileCopyInfo fileInfo, IntegrityCheckResult result,
        HashAlgorithmName algorithmName, CancellationToken cancellationToken)
    {
        try
        {
            // Use optimized hash calculator with buffer pooling and memory-mapped I/O
            var sourceHash =
                await _hashCalculator.CalculateFileHashAsync(fileInfo.SourceFullPath, algorithmName, cancellationToken);
            result.ExpectedHash = sourceHash;

            var destHash =
                await _hashCalculator.CalculateFileHashAsync(fileInfo.DestinationFullPath, algorithmName,
                    cancellationToken);
            result.ActualHash = destHash;

            // Compare hashes
            if (!string.Equals(sourceHash, destHash, StringComparison.OrdinalIgnoreCase))
            {
                result.ErrorMessage = $"Hash mismatch: expected={sourceHash}, actual={destHash}";
                return false;
            }

            return true;
        }
        catch (IOException)
        {
            // Let IOException propagate to outer catch block for retry logic
            throw;
        }
        catch (UnauthorizedAccessException)
        {
            // Let UnauthorizedAccessException propagate to outer catch block for retry logic
            throw;
        }
        catch (Exception ex)
        {
            result.ErrorMessage = $"Hash calculation failed: {ex.Message}";
            return false;
        }
    }

    private void ReportProgress()
    {
        // Throttle progress updates (max once per 500ms)
        var now = DateTime.Now;
        if ((now - _lastProgressUpdate).TotalMilliseconds < 500)
            return;

        _lastProgressUpdate = now;

        var progress = GetProgress();
        ProgressChanged?.Invoke(this, progress);
    }

    /// <summary>
    ///     Queue a file for retry verification with exponential backoff.
    /// </summary>
    private async Task QueueForRetryAsync(FileCopyInfo fileInfo, string reason, CancellationToken cancellationToken)
    {
        // Increment attempt count
        fileInfo.AttemptCount++;

        // Track retry attempt
        Interlocked.Increment(ref _totalRetryAttempts);
        Interlocked.Increment(ref _filesRetrying);

        // Calculate exponential backoff delay
        // Attempt 1->2: 500ms, Attempt 2->3: 1500ms
        var delayMs = 500 * Math.Pow(3, fileInfo.AttemptCount - 1);
        fileInfo.NextRetryTime = DateTime.Now.AddMilliseconds(delayMs);

        // Write to retry channel
        await _retryChannel!.Writer.WriteAsync(fileInfo, cancellationToken);

        ReportProgress();
    }

    /// <summary>
    ///     Check if an IOException is due to file access issues (file in use).
    /// </summary>
    private bool IsFileAccessException(IOException ex)
    {
        // Common HRESULTs for file in use:
        // 0x80070020: ERROR_SHARING_VIOLATION
        // 0x80070021: ERROR_LOCK_VIOLATION
        const int ERROR_SHARING_VIOLATION = unchecked((int)0x80070020);
        const int ERROR_LOCK_VIOLATION = unchecked((int)0x80070021);

        var hresult = ex.HResult;
        return hresult == ERROR_SHARING_VIOLATION ||
               hresult == ERROR_LOCK_VIOLATION ||
               ex.Message.Contains("being used by another process", StringComparison.OrdinalIgnoreCase) ||
               ex.Message.Contains("cannot access the file", StringComparison.OrdinalIgnoreCase);
    }
}