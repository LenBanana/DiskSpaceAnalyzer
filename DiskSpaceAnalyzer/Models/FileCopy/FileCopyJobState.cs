namespace DiskSpaceAnalyzer.Models.FileCopy;

/// <summary>
/// Represents the current state of a file copy job.
/// Applicable to all copy engines regardless of implementation.
/// </summary>
public enum FileCopyJobState
{
    /// <summary>Job is ready to start but not yet initiated.</summary>
    Ready,
    
    /// <summary>Pre-scanning source directory to calculate total size and file count.</summary>
    Scanning,
    
    /// <summary>Actively copying files.</summary>
    Running,
    
    /// <summary>Job is paused (if engine supports pause/resume).</summary>
    Paused,
    
    /// <summary>Job completed successfully (may have warnings or skipped files).</summary>
    Completed,
    
    /// <summary>Job was cancelled by user.</summary>
    Cancelled,
    
    /// <summary>Job failed with fatal errors.</summary>
    Failed,
    
    /// <summary>Verifying file integrity after copy (if enabled).</summary>
    Verifying
}
