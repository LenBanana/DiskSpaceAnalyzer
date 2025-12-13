using System;

namespace DiskSpaceAnalyzer.Models.Robocopy;

/// <summary>
/// Represents an error that occurred during robocopy operation.
/// </summary>
public class RobocopyError
{
    /// <summary>Windows error code.</summary>
    public int ErrorCode { get; set; }
    
    /// <summary>Hexadecimal error code (e.g., "0x00000005").</summary>
    public string HexCode { get; set; } = string.Empty;
    
    /// <summary>File or directory that caused the error.</summary>
    public string FilePath { get; set; } = string.Empty;
    
    /// <summary>Error message description.</summary>
    public string Message { get; set; } = string.Empty;
    
    /// <summary>Timestamp when error occurred.</summary>
    public DateTime Timestamp { get; set; } = DateTime.Now;
    
    /// <summary>
    /// Get a user-friendly error message.
    /// </summary>
    public string FriendlyMessage => ErrorCode switch
    {
        5 => "Access denied - check file permissions",
        32 => "File is in use by another process",
        123 => "Invalid file or path name",
        206 => "Path or filename is too long",
        1314 => "Privilege not held - may require admin rights",
        _ => Message
    };
    
    public override string ToString()
    {
        return $"ERROR {ErrorCode} ({HexCode}): {FriendlyMessage} - {FilePath}";
    }
}
