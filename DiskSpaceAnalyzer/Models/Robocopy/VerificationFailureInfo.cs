using System;
using DiskSpaceAnalyzer.Models.FileCopy;

namespace DiskSpaceAnalyzer.Models.Robocopy;

/// <summary>
/// Detailed information about a file that failed verification.
/// Used for displaying errors to users.
/// </summary>
public class VerificationFailureInfo
{
    /// <summary>Relative path of the file that failed verification.</summary>
    public string RelativePath { get; set; } = string.Empty;
    
    /// <summary>Full destination path of the file.</summary>
    public string DestinationPath { get; set; } = string.Empty;
    
    /// <summary>Error message explaining why verification failed.</summary>
    public string ErrorMessage { get; set; } = string.Empty;
    
    /// <summary>File size in bytes.</summary>
    public long FileSize { get; set; }
    
    /// <summary>Formatted file size for display (e.g., "1.5 MB").</summary>
    public string FileSizeFormatted => FormatBytes(FileSize);
    
    /// <summary>Number of verification attempts before final failure.</summary>
    public int AttemptCount { get; set; }
    
    /// <summary>Verification method that was used.</summary>
    public IntegrityCheckMethod Method { get; set; }
    
    /// <summary>When the verification was performed.</summary>
    public DateTime VerifiedAt { get; set; }
    
    /// <summary>Expected hash (if applicable).</summary>
    public string? ExpectedHash { get; set; }
    
    /// <summary>Actual hash found (if applicable).</summary>
    public string? ActualHash { get; set; }
    
    private static string FormatBytes(long bytes)
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
}
