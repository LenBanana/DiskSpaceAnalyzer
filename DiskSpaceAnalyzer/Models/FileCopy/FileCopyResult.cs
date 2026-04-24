using System;
using System.Collections.Generic;
using System.Linq;

namespace DiskSpaceAnalyzer.Models.FileCopy;

/// <summary>
///     Final result of a file copy operation.
///     Contains comprehensive statistics and outcomes from any copy engine.
/// </summary>
public class FileCopyResult
{
    /// <summary>Whether the operation completed successfully (no fatal errors).</summary>
    public bool Success { get; set; }

    /// <summary>Final state of the job.</summary>
    public FileCopyJobState State { get; set; }

    /// <summary>Copy engine that performed the operation.</summary>
    public CopyEngineType EngineType { get; set; }

    /// <summary>
    ///     Exit code (engine-specific).
    ///     Robocopy: 0-16 (standard robocopy exit codes)
    ///     Native: 0 = success, non-zero = failure
    /// </summary>
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

    /// <summary>Mismatched files (different size/date but same name).</summary>
    public long FilesMismatched { get; set; }

    /// <summary>Files deleted (in mirror mode).</summary>
    public long FilesDeleted { get; set; }

    // Timing

    /// <summary>When the operation started.</summary>
    public DateTime StartTime { get; set; }

    /// <summary>When the operation completed.</summary>
    public DateTime EndTime { get; set; }

    /// <summary>Total duration of the operation (includes paused time).</summary>
    public TimeSpan Duration { get; set; }

    /// <summary>Time spent actively copying (excluding pauses).</summary>
    public TimeSpan ActiveDuration { get; set; }

    /// <summary>Time spent paused.</summary>
    public TimeSpan PausedDuration { get; set; }

    /// <summary>Average transfer speed in bytes per second (based on active time).</summary>
    public double AverageBytesPerSecond { get; set; }

    /// <summary>Average transfer speed in MB/s.</summary>
    public double AverageMegabytesPerSecond => AverageBytesPerSecond / (1024 * 1024);

    // Errors

    /// <summary>Total number of errors encountered.</summary>
    public int ErrorCount => Errors.Count;

    /// <summary>List of all errors encountered.</summary>
    public List<FileCopyError> Errors { get; set; } = new();

    /// <summary>Path to the log file (if logging enabled).</summary>
    public string LogFilePath { get; set; } = string.Empty;

    /// <summary>
    ///     Summary message suitable for display to user.
    ///     Example: "Copied 1,234 files (5.2 GB) in 2m 34s"
    /// </summary>
    public string SummaryMessage { get; set; } = string.Empty;

    // Integrity verification results

    /// <summary>Whether integrity verification was enabled.</summary>
    public bool IntegrityCheckEnabled { get; set; }

    /// <summary>Whether integrity verification completed.</summary>
    public bool IntegrityCheckCompleted { get; set; }

    /// <summary>Number of files that passed integrity verification.</summary>
    public long IntegrityChecksPassed { get; set; }

    /// <summary>Number of files that failed integrity verification.</summary>
    public long IntegrityChecksFailed { get; set; }

    /// <summary>Total time spent on integrity verification.</summary>
    public TimeSpan IntegrityCheckDuration { get; set; }

    /// <summary>Detailed integrity verification results (if available).</summary>
    public List<IntegrityCheckResult>? IntegrityResults { get; set; }

    /// <summary>Alias for IntegrityChecksPassed (backwards compatibility).</summary>
    public long FilesVerifiedPassed => IntegrityChecksPassed;

    /// <summary>Alias for IntegrityChecksFailed (backwards compatibility).</summary>
    public long FilesVerifiedFailed => IntegrityChecksFailed;

    /// <summary>Alias for IntegrityChecksPassed + IntegrityChecksFailed.</summary>
    public long FilesVerified => IntegrityChecksPassed + IntegrityChecksFailed;

    /// <summary>Total elapsed time (alias for Duration).</summary>
    public TimeSpan Elapsed => Duration;

    // Calculated properties

    /// <summary>Overall success rate as percentage (0-100).</summary>
    public double SuccessRate
    {
        get
        {
            var totalAttempted = FilesCopied + FilesFailed;
            return totalAttempted > 0 ? FilesCopied * 100.0 / totalAttempted : 100.0;
        }
    }

    /// <summary>Whether any errors occurred.</summary>
    public bool HasErrors => Errors.Count > 0 || FilesFailed > 0;

    /// <summary>Whether any files failed integrity checks.</summary>
    public bool HasIntegrityFailures => IntegrityCheckEnabled && IntegrityChecksFailed > 0;

    /// <summary>Number of permission-related errors.</summary>
    public int PermissionErrorCount => Errors.Count(e => e.IsPermissionError);

    /// <summary>
    ///     Generate a user-friendly summary message.
    /// </summary>
    public static string GenerateSummary(FileCopyResult result)
    {
        if (result.State == FileCopyJobState.Cancelled)
            return $"Operation cancelled. Copied {result.FilesCopied:N0} of {result.TotalFiles:N0} files.";

        if (result.State == FileCopyJobState.Failed)
            return $"Operation failed. {result.FilesFailed:N0} errors occurred.";

        var duration = result.ActiveDuration.TotalSeconds > 0
            ? FormatDuration(result.ActiveDuration)
            : FormatDuration(result.Duration);

        var size = FormatBytes(result.BytesCopied);

        var summary = $"Copied {result.FilesCopied:N0} files ({size}) in {duration}";

        if (result.FilesSkipped > 0)
            summary += $", skipped {result.FilesSkipped:N0} (up-to-date)";

        if (result.FilesFailed > 0)
            summary += $", {result.FilesFailed:N0} failed";

        if (result.IntegrityCheckEnabled)
        {
            if (result.IntegrityChecksFailed > 0)
                summary += $" | {result.IntegrityChecksFailed:N0} integrity failures";
            else if (result.IntegrityCheckCompleted)
                summary += " | All integrity checks passed";
        }

        return summary;
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalHours >= 1)
            return $"{(int)duration.TotalHours}h {duration.Minutes}m";
        if (duration.TotalMinutes >= 1)
            return $"{(int)duration.TotalMinutes}m {duration.Seconds}s";
        return $"{duration.TotalSeconds:F1}s";
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes >= 1024L * 1024 * 1024 * 1024)
            return $"{bytes / (1024.0 * 1024 * 1024 * 1024):F2} TB";
        if (bytes >= 1024L * 1024 * 1024)
            return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        if (bytes >= 1024L * 1024)
            return $"{bytes / (1024.0 * 1024):F2} MB";
        if (bytes >= 1024)
            return $"{bytes / 1024.0:F2} KB";
        return $"{bytes} bytes";
    }
}