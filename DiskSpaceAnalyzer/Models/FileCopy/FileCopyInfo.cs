using System;

namespace DiskSpaceAnalyzer.Models.FileCopy;

/// <summary>
/// Information about a file that was copied and needs integrity verification.
/// Used by IFileIntegrityService to queue and track verification tasks.
/// </summary>
public class FileCopyInfo
{
    /// <summary>Relative path from source root.</summary>
    public string RelativePath { get; set; } = string.Empty;
    
    /// <summary>Full path to the source file.</summary>
    public string SourceFullPath { get; set; } = string.Empty;
    
    /// <summary>Full path to the destination file.</summary>
    public string DestinationFullPath { get; set; } = string.Empty;
    
    /// <summary>File size in bytes.</summary>
    public long FileSize { get; set; }
    
    /// <summary>When the file was copied.</summary>
    public DateTime CopiedAt { get; set; }
    
    /// <summary>Source file last modified time.</summary>
    public DateTime SourceLastModified { get; set; }
    
    /// <summary>Number of verification attempts for this file.</summary>
    public int AttemptCount { get; set; }
    
    /// <summary>
    /// Time when this file should be retried (for retry queue).
    /// Used to delay retries when destination file is still being written or locked.
    /// </summary>
    public DateTime NextRetryTime { get; set; }
}
