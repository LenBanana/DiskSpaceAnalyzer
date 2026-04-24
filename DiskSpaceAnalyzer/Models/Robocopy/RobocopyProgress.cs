using System;

namespace DiskSpaceAnalyzer.Models.Robocopy;

/// <summary>
///     Represents the current progress of a robocopy operation.
///     Designed to be immutable for thread-safety.
/// </summary>
public class RobocopyProgress
{
    /// <summary>Current state of the job.</summary>
    public RobocopyJobState State { get; init; } = RobocopyJobState.Ready;

    /// <summary>Current status message.</summary>
    public string StatusMessage { get; init; } = string.Empty;

    // File progress
    /// <summary>Number of files successfully copied.</summary>
    public long FilesCopied { get; init; }

    /// <summary>Total number of files to copy.</summary>
    public long TotalFiles { get; init; }

    /// <summary>Number of directories created.</summary>
    public long DirectoriesCopied { get; init; }

    /// <summary>Total number of directories.</summary>
    public long TotalDirectories { get; init; }

    // Byte progress
    /// <summary>Bytes successfully copied.</summary>
    public long BytesCopied { get; init; }

    /// <summary>Total bytes to copy.</summary>
    public long TotalBytes { get; init; }

    /// <summary>Current file being processed.</summary>
    public string CurrentFile { get; init; } = string.Empty;

    // Timing
    /// <summary>When the operation started.</summary>
    public DateTime StartTime { get; init; }

    /// <summary>Elapsed time since start.</summary>
    public TimeSpan Elapsed { get; init; }

    /// <summary>Time spent paused (excluded from elapsed).</summary>
    public TimeSpan PausedDuration { get; init; }

    // Calculated properties
    /// <summary>Overall progress percentage based on bytes (0-100).</summary>
    public double PercentComplete => TotalBytes > 0 ? BytesCopied * 100.0 / TotalBytes : 0;

    /// <summary>Progress percentage based on file count (0-100).</summary>
    public double FilePercentComplete => TotalFiles > 0 ? FilesCopied * 100.0 / TotalFiles : 0;

    /// <summary>Transfer speed in bytes per second.</summary>
    public double BytesPerSecond
    {
        get
        {
            var activeTime = Elapsed - PausedDuration;
            return activeTime.TotalSeconds > 0 ? BytesCopied / activeTime.TotalSeconds : 0;
        }
    }

    /// <summary>Estimated time remaining.</summary>
    public TimeSpan EstimatedTimeRemaining
    {
        get
        {
            if (BytesPerSecond <= 0 || BytesCopied <= 0) return TimeSpan.Zero;
            var remainingBytes = TotalBytes - BytesCopied;
            return TimeSpan.FromSeconds(remainingBytes / BytesPerSecond);
        }
    }

    /// <summary>Transfer speed in MB/s.</summary>
    public double MegabytesPerSecond => BytesPerSecond / (1024 * 1024);

    // Error tracking
    /// <summary>Number of errors encountered.</summary>
    public int ErrorCount { get; init; }

    /// <summary>Number of skipped files.</summary>
    public long SkippedFiles { get; init; }

    // Integrity verification
    /// <summary>Number of files verified for integrity.</summary>
    public long FilesVerified { get; init; }

    /// <summary>Number of files that passed verification.</summary>
    public long FilesVerifiedPassed { get; init; }

    /// <summary>Number of files that failed verification.</summary>
    public long FilesVerifiedFailed { get; init; }

    /// <summary>Number of files currently retrying verification.</summary>
    public long FilesRetrying { get; init; }

    /// <summary>Integrity verification progress percentage (0-100).</summary>
    public double VerificationPercentComplete { get; init; }

    /// <summary>Current file being verified.</summary>
    public string CurrentVerificationFile { get; init; } = string.Empty;

    /// <summary>
    ///     Create a new progress with updated values.
    /// </summary>
    public RobocopyProgress With(
        RobocopyJobState? state = null,
        string? statusMessage = null,
        long? filesCopied = null,
        long? bytesCopied = null,
        string? currentFile = null,
        TimeSpan? elapsed = null,
        int? errorCount = null)
    {
        return new RobocopyProgress
        {
            State = state ?? State,
            StatusMessage = statusMessage ?? StatusMessage,
            FilesCopied = filesCopied ?? FilesCopied,
            TotalFiles = TotalFiles,
            DirectoriesCopied = DirectoriesCopied,
            TotalDirectories = TotalDirectories,
            BytesCopied = bytesCopied ?? BytesCopied,
            TotalBytes = TotalBytes,
            CurrentFile = currentFile ?? CurrentFile,
            StartTime = StartTime,
            Elapsed = elapsed ?? Elapsed,
            PausedDuration = PausedDuration,
            ErrorCount = errorCount ?? ErrorCount,
            SkippedFiles = SkippedFiles,
            FilesVerified = FilesVerified,
            FilesVerifiedPassed = FilesVerifiedPassed,
            FilesVerifiedFailed = FilesVerifiedFailed,
            VerificationPercentComplete = VerificationPercentComplete,
            CurrentVerificationFile = CurrentVerificationFile
        };
    }
}