using System;
using System.Collections.Generic;

namespace DiskSpaceAnalyzer.Models.Robocopy;

/// <summary>
/// Final result of a robocopy operation.
/// </summary>
public class RobocopyResult
{
    /// <summary>Whether the operation completed successfully.</summary>
    public bool Success { get; set; }
    
    /// <summary>Final state of the job.</summary>
    public RobocopyJobState State { get; set; }
    
    /// <summary>Robocopy exit code (0-16).</summary>
    public int ExitCode { get; set; }
    
    /// <summary>Human-readable interpretation of exit code.</summary>
    public string ExitCodeMessage { get; set; } = string.Empty;
    
    // Summary statistics
    /// <summary>Total directories found in source.</summary>
    public long TotalDirectories { get; set; }
    
    /// <summary>Total files found in source.</summary>
    public long TotalFiles { get; set; }
    
    /// <summary>Total bytes in source.</summary>
    public long TotalBytes { get; set; }
    
    /// <summary>Directories created at destination.</summary>
    public long DirectoriesCopied { get; set; }
    
    /// <summary>Files successfully copied.</summary>
    public long FilesCopied { get; set; }
    
    /// <summary>Bytes successfully copied.</summary>
    public long BytesCopied { get; set; }
    
    /// <summary>Files that failed to copy.</summary>
    public long FilesFailed { get; set; }
    
    /// <summary>Files skipped (already up to date).</summary>
    public long FilesSkipped { get; set; }
    
    /// <summary>Extra files found at destination (not in source).</summary>
    public long FilesExtra { get; set; }
    
    /// <summary>Mismatched files.</summary>
    public long FilesMismatched { get; set; }
    
    // Timing
    /// <summary>When the operation started.</summary>
    public DateTime StartTime { get; set; }
    
    /// <summary>When the operation completed.</summary>
    public DateTime EndTime { get; set; }
    
    /// <summary>Total duration of the operation.</summary>
    public TimeSpan Duration { get; set; }
    
    /// <summary>Time spent actively copying (excluding pauses).</summary>
    public TimeSpan ActiveDuration { get; set; }
    
    /// <summary>Average transfer speed in bytes per second.</summary>
    public double AverageBytesPerSecond { get; set; }
    
    // Errors
    /// <summary>List of all errors encountered.</summary>
    public List<RobocopyError> Errors { get; set; } = new();
    
    /// <summary>Path to the log file.</summary>
    public string LogFilePath { get; set; } = string.Empty;
    
    /// <summary>
    /// Interpret robocopy exit code.
    /// Exit codes are bitwise combinable (0-16).
    /// </summary>
    public static string InterpretExitCode(int exitCode)
    {
        if (exitCode >= 16) return "Fatal error - no files copied";
        if (exitCode == 0) return "No changes - files already up to date";
        
        var messages = new List<string>();
        if ((exitCode & 1) != 0) messages.Add("Files copied successfully");
        if ((exitCode & 2) != 0) messages.Add("Extra files/directories detected");
        if ((exitCode & 4) != 0) messages.Add("Mismatched files detected");
        if ((exitCode & 8) != 0) messages.Add("Failed copies detected");
        
        return messages.Count > 0 ? string.Join(", ", messages) : "Unknown status";
    }
    
    /// <summary>
    /// Format the result as a human-readable summary.
    /// </summary>
    public string GetSummary()
    {
        var summary = $"Robocopy {State}\n";
        summary += $"Exit Code: {ExitCode} - {ExitCodeMessage}\n\n";
        summary += $"Files: {FilesCopied:N0} copied, {FilesSkipped:N0} skipped, {FilesFailed:N0} failed\n";
        summary += $"Bytes: {FormatBytes(BytesCopied)} of {FormatBytes(TotalBytes)}\n";
        summary += $"Duration: {Duration:hh\\:mm\\:ss}\n";
        
        if (AverageBytesPerSecond > 0)
        {
            var mbps = AverageBytesPerSecond / (1024 * 1024);
            summary += $"Speed: {mbps:F2} MB/s\n";
        }
        
        if (Errors.Count > 0)
            summary += $"\nErrors: {Errors.Count}\n";
        
        return summary;
    }
    
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
