using System;

namespace DiskSpaceAnalyzer.Models.FileCopy;

/// <summary>
/// Represents the current progress of a file copy operation.
/// Designed to be immutable for thread-safety across all copy engines.
/// </summary>
public class FileCopyProgress
{
    /// <summary>Current state of the job.</summary>
    public FileCopyJobState State { get; init; } = FileCopyJobState.Ready;
    
    /// <summary>Current status message (human-readable).</summary>
    public string StatusMessage { get; init; } = string.Empty;
    
    /// <summary>Copy engine being used.</summary>
    public CopyEngineType EngineType { get; init; } = CopyEngineType.Auto;
    
    // File progress
    
    /// <summary>Number of files successfully copied.</summary>
    public long FilesCopied { get; init; }
    
    /// <summary>Total number of files to copy.</summary>
    public long TotalFiles { get; init; }
    
    /// <summary>Number of directories created.</summary>
    public long DirectoriesCopied { get; init; }
    
    /// <summary>Total number of directories.</summary>
    public long TotalDirectories { get; init; }
    
    /// <summary>Number of files that failed to copy.</summary>
    public long FilesFailed { get; init; }
    
    /// <summary>Number of files skipped (already up-to-date).</summary>
    public long FilesSkipped { get; init; }
    
    // Byte progress
    
    /// <summary>Bytes successfully copied.</summary>
    public long BytesCopied { get; init; }
    
    /// <summary>Total bytes to copy.</summary>
    public long TotalBytes { get; init; }
    
    /// <summary>Current file being processed (relative or full path).</summary>
    public string CurrentFile { get; init; } = string.Empty;
    
    /// <summary>
    /// Bytes copied for the current file (if engine supports byte-level tracking).
    /// </summary>
    public long CurrentFileBytesCopied { get; init; }
    
    /// <summary>
    /// Total size of current file being copied.
    /// </summary>
    public long CurrentFileSize { get; init; }
    
    // Timing
    
    /// <summary>When the operation started.</summary>
    public DateTime StartTime { get; init; }
    
    /// <summary>Elapsed time since start (includes paused time).</summary>
    public TimeSpan Elapsed { get; init; }
    
    /// <summary>Time spent paused (excluded from active time calculations).</summary>
    public TimeSpan PausedDuration { get; init; }
    
    // Error tracking
    
    /// <summary>Number of errors encountered during copy.</summary>
    public int ErrorCount { get; init; }
    
    // Integrity verification tracking
    
    /// <summary>Number of files verified for integrity.</summary>
    public long FilesVerified { get; init; }
    
    /// <summary>Number of files that passed verification.</summary>
    public long FilesVerifiedPassed { get; init; }
    
    /// <summary>Number of files that failed verification.</summary>
    public long FilesVerifiedFailed { get; init; }
    
    /// <summary>Number of files currently being retried due to verification failure.</summary>
    public long FilesRetrying { get; init; }
    
    /// <summary>Current file being verified (if in verification phase).</summary>
    public string CurrentVerificationFile { get; init; } = string.Empty;
    
    /// <summary>Verification progress percentage (0-100).</summary>
    public double VerificationPercentComplete => FilesCopied > 0 
        ? Math.Min(100, FilesVerified * 100.0 / FilesCopied) 
        : 0;
    
    // Calculated properties
    
    /// <summary>Overall progress percentage based on bytes (0-100).</summary>
    public double PercentComplete => TotalBytes > 0 ? Math.Min(100, BytesCopied * 100.0 / TotalBytes) : 0;
    
    /// <summary>Progress percentage based on file count (0-100).</summary>
    public double FilePercentComplete => TotalFiles > 0 ? Math.Min(100, FilesCopied * 100.0 / TotalFiles) : 0;
    
    /// <summary>Progress percentage for current file (0-100).</summary>
    public double CurrentFilePercentComplete => CurrentFileSize > 0 
        ? Math.Min(100, CurrentFileBytesCopied * 100.0 / CurrentFileSize) : 0;
    
    /// <summary>Active copy time (excludes paused duration).</summary>
    public TimeSpan ActiveDuration => Elapsed - PausedDuration;
    
    /// <summary>Transfer speed in bytes per second (based on active time).</summary>
    public double BytesPerSecond
    {
        get
        {
            var activeSeconds = ActiveDuration.TotalSeconds;
            return activeSeconds > 0 ? BytesCopied / activeSeconds : 0;
        }
    }
    
    /// <summary>Estimated time remaining based on current transfer speed.</summary>
    public TimeSpan EstimatedTimeRemaining
    {
        get
        {
            if (BytesPerSecond <= 0 || BytesCopied <= 0 || TotalBytes <= BytesCopied) 
                return TimeSpan.Zero;
            
            var remainingBytes = TotalBytes - BytesCopied;
            return TimeSpan.FromSeconds(remainingBytes / BytesPerSecond);
        }
    }
    
    /// <summary>Transfer speed in MB/s.</summary>
    public double MegabytesPerSecond => BytesPerSecond / (1024 * 1024);
    
    /// <summary>Transfer speed in GB/s.</summary>
    public double GigabytesPerSecond => BytesPerSecond / (1024 * 1024 * 1024);
    
    /// <summary>
    /// Get formatted speed string with appropriate unit (B/s, KB/s, MB/s, GB/s).
    /// </summary>
    public string FormattedSpeed
    {
        get
        {
            if (BytesPerSecond >= 1024 * 1024 * 1024)
                return $"{GigabytesPerSecond:F2} GB/s";
            if (BytesPerSecond >= 1024 * 1024)
                return $"{MegabytesPerSecond:F2} MB/s";
            if (BytesPerSecond >= 1024)
                return $"{BytesPerSecond / 1024:F2} KB/s";
            return $"{BytesPerSecond:F0} B/s";
        }
    }
    
    /// <summary>
    /// Whether the operation is in a terminal state (completed, cancelled, or failed).
    /// </summary>
    public bool IsTerminal => State is FileCopyJobState.Completed 
                                    or FileCopyJobState.Cancelled 
                                    or FileCopyJobState.Failed;
    
    /// <summary>
    /// Whether the operation is actively running (not paused, terminal, or ready).
    /// </summary>
    public bool IsActive => State is FileCopyJobState.Running 
                                  or FileCopyJobState.Scanning 
                                  or FileCopyJobState.Verifying;
}
