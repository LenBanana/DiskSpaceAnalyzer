using System;

namespace DiskSpaceAnalyzer.Models.FileCopy;

/// <summary>
/// Result of a single file integrity verification.
/// Contains detailed information about verification success or failure.
/// </summary>
public class IntegrityCheckResult
{
    /// <summary>Relative path of the verified file.</summary>
    public string RelativePath { get; set; } = string.Empty;
    
    /// <summary>Full path to the source file that was used for comparison.</summary>
    public string SourcePath { get; set; } = string.Empty;
    
    /// <summary>Full path to the destination file that was verified.</summary>
    public string DestinationPath { get; set; } = string.Empty;
    
    /// <summary>Whether the file passed integrity verification.</summary>
    public bool IsValid { get; set; }
    
    /// <summary>Verification method used.</summary>
    public IntegrityCheckMethod Method { get; set; }
    
    /// <summary>Error message if verification failed.</summary>
    public string? ErrorMessage { get; set; }
    
    /// <summary>How long the verification took.</summary>
    public TimeSpan Duration { get; set; }
    
    /// <summary>File size in bytes.</summary>
    public long FileSize { get; set; }
    
    /// <summary>When the verification was performed.</summary>
    public DateTime VerifiedAt { get; set; }
    
    /// <summary>Expected hash value (if applicable for hash-based methods).</summary>
    public string? ExpectedHash { get; set; }
    
    /// <summary>Actual hash value found (if applicable for hash-based methods).</summary>
    public string? ActualHash { get; set; }
    
    /// <summary>
    /// For metadata verification: expected file size.
    /// </summary>
    public long? ExpectedSize { get; set; }
    
    /// <summary>
    /// For metadata verification: actual file size found.
    /// </summary>
    public long? ActualSize { get; set; }
    
    /// <summary>
    /// For metadata verification: expected last modified time.
    /// </summary>
    public DateTime? ExpectedModifiedTime { get; set; }
    
    /// <summary>
    /// For metadata verification: actual last modified time found.
    /// </summary>
    public DateTime? ActualModifiedTime { get; set; }
    
    /// <summary>
    /// Number of verification attempts before success or final failure.
    /// </summary>
    public int AttemptCount { get; set; } = 1;
    
    /// <summary>
    /// Verification speed in bytes per second.
    /// </summary>
    public double BytesPerSecond => Duration.TotalSeconds > 0 ? FileSize / Duration.TotalSeconds : 0;
}
