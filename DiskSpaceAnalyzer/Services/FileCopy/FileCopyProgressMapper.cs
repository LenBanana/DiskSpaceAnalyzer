using DiskSpaceAnalyzer.Models.FileCopy;
using DiskSpaceAnalyzer.Models.Robocopy;

namespace DiskSpaceAnalyzer.Services.FileCopy;

/// <summary>
///     Maps between generic FileCopyProgress and Robocopy-specific RobocopyProgress.
///     Handles bidirectional conversion for real-time progress reporting.
/// </summary>
public static class FileCopyProgressMapper
{
    /// <summary>
    ///     Convert Robocopy-specific RobocopyProgress to generic FileCopyProgress.
    /// </summary>
    public static FileCopyProgress ToFileCopyProgress(RobocopyProgress robocopy,
        CopyEngineType engineType = CopyEngineType.Robocopy)
    {
        return new FileCopyProgress
        {
            // State
            State = MapJobState(robocopy.State),
            StatusMessage = robocopy.StatusMessage,
            EngineType = engineType,

            // File progress
            FilesCopied = robocopy.FilesCopied,
            TotalFiles = robocopy.TotalFiles,
            DirectoriesCopied = robocopy.DirectoriesCopied,
            TotalDirectories = robocopy.TotalDirectories,
            FilesFailed = robocopy.ErrorCount, // Map error count to failed files
            FilesSkipped = robocopy.SkippedFiles,

            // Byte progress
            BytesCopied = robocopy.BytesCopied,
            TotalBytes = robocopy.TotalBytes,
            CurrentFile = robocopy.CurrentFile,
            CurrentFileBytesCopied = 0, // Robocopy doesn't track byte-level per-file progress
            CurrentFileSize = 0,

            // Timing
            StartTime = robocopy.StartTime,
            Elapsed = robocopy.Elapsed,
            PausedDuration = robocopy.PausedDuration
        };
    }

    /// <summary>
    ///     Convert generic FileCopyProgress to Robocopy-specific RobocopyProgress.
    ///     Used for reverse compatibility or when wrapping generic progress.
    /// </summary>
    public static RobocopyProgress ToRobocopyProgress(FileCopyProgress generic)
    {
        return new RobocopyProgress
        {
            // State
            State = MapJobState(generic.State),
            StatusMessage = generic.StatusMessage,

            // File progress
            FilesCopied = generic.FilesCopied,
            TotalFiles = generic.TotalFiles,
            DirectoriesCopied = generic.DirectoriesCopied,
            TotalDirectories = generic.TotalDirectories,
            ErrorCount = (int)generic.FilesFailed, // Map failed files to error count
            SkippedFiles = generic.FilesSkipped,

            // Byte progress
            BytesCopied = generic.BytesCopied,
            TotalBytes = generic.TotalBytes,
            CurrentFile = generic.CurrentFile,

            // Timing
            StartTime = generic.StartTime,
            Elapsed = generic.Elapsed,
            PausedDuration = generic.PausedDuration,

            // Integrity verification
            FilesVerified = 0,
            FilesVerifiedPassed = 0,
            FilesVerifiedFailed = 0,
            FilesRetrying = 0,
            VerificationPercentComplete = 0,
            CurrentVerificationFile = string.Empty
        };
    }

    /// <summary>
    ///     Map RobocopyJobState to FileCopyJobState.
    /// </summary>
    private static FileCopyJobState MapJobState(RobocopyJobState state)
    {
        return state switch
        {
            RobocopyJobState.Ready => FileCopyJobState.Ready,
            RobocopyJobState.Scanning => FileCopyJobState.Scanning,
            RobocopyJobState.Running => FileCopyJobState.Running,
            RobocopyJobState.Paused => FileCopyJobState.Paused,
            RobocopyJobState.Completed => FileCopyJobState.Completed,
            RobocopyJobState.Cancelled => FileCopyJobState.Cancelled,
            RobocopyJobState.Failed => FileCopyJobState.Failed,
            _ => FileCopyJobState.Ready
        };
    }

    /// <summary>
    ///     Map FileCopyJobState to RobocopyJobState.
    /// </summary>
    private static RobocopyJobState MapJobState(FileCopyJobState state)
    {
        return state switch
        {
            FileCopyJobState.Ready => RobocopyJobState.Ready,
            FileCopyJobState.Scanning => RobocopyJobState.Scanning,
            FileCopyJobState.Running => RobocopyJobState.Running,
            FileCopyJobState.Paused => RobocopyJobState.Paused,
            FileCopyJobState.Completed => RobocopyJobState.Completed,
            FileCopyJobState.Cancelled => RobocopyJobState.Cancelled,
            FileCopyJobState.Failed => RobocopyJobState.Failed,
            FileCopyJobState.Verifying => RobocopyJobState
                .Running, // Map to Running since Verifying doesn't exist in Robocopy
            _ => RobocopyJobState.Ready
        };
    }
}