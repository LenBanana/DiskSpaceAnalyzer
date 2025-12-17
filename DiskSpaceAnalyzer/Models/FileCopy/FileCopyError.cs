using System;

namespace DiskSpaceAnalyzer.Models.FileCopy;

/// <summary>
/// Represents an error that occurred during a file copy operation.
/// Generic across all copy engines - specific engines may add additional context.
/// </summary>
public class FileCopyError
{
    /// <summary>System error code (Windows HRESULT, errno, etc.).</summary>
    public int ErrorCode { get; set; }
    
    /// <summary>Hexadecimal error code representation (e.g., "0x00000005").</summary>
    public string HexCode { get; set; } = string.Empty;
    
    /// <summary>File or directory path that caused the error.</summary>
    public string FilePath { get; set; } = string.Empty;
    
    /// <summary>Error message description.</summary>
    public string Message { get; set; } = string.Empty;
    
    /// <summary>Timestamp when error occurred.</summary>
    public DateTime Timestamp { get; set; } = DateTime.Now;
    
    /// <summary>Copy engine that reported this error.</summary>
    public CopyEngineType SourceEngine { get; set; }
    
    /// <summary>Exception object if available (for native engines).</summary>
    public Exception? Exception { get; set; }
    
    /// <summary>
    /// Get a user-friendly error message based on common error codes.
    /// </summary>
    public string FriendlyMessage
    {
        get
        {
            // Common Windows error codes (applicable to most scenarios)
            return ErrorCode switch
            {
                5 => "Access denied - check file permissions",
                32 => "File is in use by another process",
                123 => "Invalid file or path name",
                206 => "Path or filename is too long",
                1314 => "Privilege not held - may require administrator rights",
                _ => !string.IsNullOrEmpty(Message) ? Message : $"Error code {ErrorCode}"
            };
        }
    }
    
    /// <summary>
    /// Whether this error is likely recoverable with a retry.
    /// </summary>
    public bool IsRetryable => ErrorCode is 32 or 33 or 1; // File in use, lock violation, etc.
    
    /// <summary>
    /// Whether this error indicates a permission issue.
    /// </summary>
    public bool IsPermissionError => ErrorCode is 5 or 1314;
    
    public override string ToString()
    {
        var engineInfo = SourceEngine != CopyEngineType.Auto ? $"[{SourceEngine}] " : "";
        return $"{engineInfo}ERROR {ErrorCode} ({HexCode}): {FriendlyMessage} - {FilePath}";
    }
}
