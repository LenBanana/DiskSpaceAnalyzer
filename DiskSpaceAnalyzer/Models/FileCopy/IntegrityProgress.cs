using System;

namespace DiskSpaceAnalyzer.Models.FileCopy;

/// <summary>
/// Progress information for file integrity verification.
/// Reports real-time status during the verification phase.
/// </summary>
public class IntegrityProgress
{
    /// <summary>Number of files verified so far.</summary>
    public long FilesVerified { get; init; }
    
    /// <summary>Total number of files to verify.</summary>
    public long TotalFiles { get; init; }
    
    /// <summary>Number of files that passed verification.</summary>
    public long FilesPassed { get; init; }
    
    /// <summary>Number of files that failed verification.</summary>
    public long FilesFailed { get; init; }
    
    /// <summary>Number of files currently queued for verification.</summary>
    public long FilesQueued { get; init; }
    
    /// <summary>Number of files currently retrying verification (due to temporary access issues).</summary>
    public long FilesRetrying { get; init; }
    
    /// <summary>Total number of retry attempts made across all files.</summary>
    public long TotalRetryAttempts { get; init; }
    
    /// <summary>Bytes verified so far.</summary>
    public long BytesVerified { get; init; }
    
    /// <summary>Total bytes to verify.</summary>
    public long TotalBytes { get; init; }
    
    /// <summary>Current file being verified (relative path).</summary>
    public string CurrentFile { get; init; } = string.Empty;
    
    /// <summary>Verification method being used.</summary>
    public IntegrityCheckMethod Method { get; init; }
    
    /// <summary>Whether verification is currently active.</summary>
    public bool IsActive { get; init; }
    
    /// <summary>Whether verification is complete (all files processed).</summary>
    public bool IsComplete { get; init; }
    
    /// <summary>When verification started.</summary>
    public DateTime StartTime { get; init; }
    
    /// <summary>Elapsed time since verification started.</summary>
    public TimeSpan Elapsed { get; init; }
    
    /// <summary>Verification progress percentage based on file count (0-100).</summary>
    public double PercentComplete => TotalFiles > 0 ? Math.Min(100, FilesVerified * 100.0 / TotalFiles) : 0;
    
    /// <summary>Verification progress percentage based on bytes (0-100).</summary>
    public double BytePercentComplete => TotalBytes > 0 ? Math.Min(100, BytesVerified * 100.0 / TotalBytes) : 0;
    
    /// <summary>Verification speed in bytes per second.</summary>
    public double BytesPerSecond => Elapsed.TotalSeconds > 0 ? BytesVerified / Elapsed.TotalSeconds : 0;
    
    /// <summary>Verification speed in MB/s.</summary>
    public double MegabytesPerSecond => BytesPerSecond / (1024 * 1024);
    
    /// <summary>Estimated time remaining.</summary>
    public TimeSpan EstimatedTimeRemaining
    {
        get
        {
            if (BytesPerSecond <= 0 || BytesVerified <= 0 || TotalBytes <= BytesVerified)
                return TimeSpan.Zero;
            
            var remainingBytes = TotalBytes - BytesVerified;
            return TimeSpan.FromSeconds(remainingBytes / BytesPerSecond);
        }
    }
    
    /// <summary>
    /// Verification success rate as percentage (0-100).
    /// </summary>
    public double SuccessRate
    {
        get
        {
            var totalCompleted = FilesPassed + FilesFailed;
            return totalCompleted > 0 ? (FilesPassed * 100.0 / totalCompleted) : 100.0;
        }
    }
}
