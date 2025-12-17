using System;
using System.Collections.Generic;
using System.Linq;
using DiskSpaceAnalyzer.Models.FileCopy;
using DiskSpaceAnalyzer.Models.Robocopy;

namespace DiskSpaceAnalyzer.Services.FileCopy;

/// <summary>
/// Maps between generic FileCopyResult and Robocopy-specific RobocopyResult.
/// Handles bidirectional conversion for final operation results.
/// </summary>
public static class FileCopyResultMapper
{
    /// <summary>
    /// Convert Robocopy-specific RobocopyResult to generic FileCopyResult.
    /// </summary>
    public static FileCopyResult ToFileCopyResult(RobocopyResult robocopy, CopyEngineType engineType = CopyEngineType.Robocopy)
    {
        var result = new FileCopyResult
        {
            // Core result info
            Success = robocopy.Success,
            State = MapJobState(robocopy.State),
            EngineType = engineType,
            ExitCode = robocopy.ExitCode,
            ExitCodeMessage = robocopy.ExitCodeMessage,
            
            // Summary statistics
            TotalDirectories = robocopy.TotalDirectories,
            TotalFiles = robocopy.TotalFiles,
            TotalBytes = robocopy.TotalBytes,
            DirectoriesCopied = robocopy.DirectoriesCopied,
            FilesCopied = robocopy.FilesCopied,
            BytesCopied = robocopy.BytesCopied,
            FilesFailed = robocopy.FilesFailed,
            FilesSkipped = robocopy.FilesSkipped,
            FilesExtra = robocopy.FilesExtra,
            FilesMismatched = robocopy.FilesMismatched,
            FilesDeleted = 0, // Robocopy doesn't track this separately
            
            // Timing
            StartTime = robocopy.StartTime,
            EndTime = robocopy.EndTime,
            Duration = robocopy.Duration,
            ActiveDuration = robocopy.ActiveDuration,
            PausedDuration = TimeSpan.Zero, // Calculate if needed
            AverageBytesPerSecond = robocopy.AverageBytesPerSecond,
            
            // Errors (convert RobocopyError to FileCopyError)
            Errors = robocopy.Errors.Select(e => new FileCopyError
            {
                ErrorCode = e.ErrorCode,
                Message = e.Message,
                FilePath = e.FilePath,
                Timestamp = e.Timestamp,
                SourceEngine = CopyEngineType.Robocopy
            }).ToList(),
            
            // Logging
            LogFilePath = robocopy.LogFilePath,
            
            // Integrity verification
            IntegrityCheckEnabled = robocopy.IntegrityCheckEnabled,
            IntegrityCheckCompleted = robocopy.FilesVerified > 0,
            IntegrityChecksPassed = robocopy.FilesVerifiedPassed,
            IntegrityChecksFailed = robocopy.FilesVerifiedFailed,
            IntegrityCheckDuration = TimeSpan.Zero, // Robocopy doesn't track this separately
            
            // Convert IntegrityCheckResult if available
            IntegrityResults = robocopy.FailedVerificationDetails
                .Select(f => new Models.FileCopy.IntegrityCheckResult
                {
                    RelativePath = f.RelativePath,
                    SourcePath = "", // Not available in verification info
                    DestinationPath = f.DestinationPath,
                    IsValid = false,
                    Method = f.Method,
                    ErrorMessage = f.ErrorMessage,
                    Duration = TimeSpan.Zero,
                    FileSize = f.FileSize,
                    VerifiedAt = f.VerifiedAt,
                    ExpectedHash = f.ExpectedHash,
                    ActualHash = f.ActualHash,
                    AttemptCount = f.AttemptCount
                })
                .ToList()
        };
        
        // Generate summary message
        result.SummaryMessage = FileCopyResult.GenerateSummary(result);
        
        return result;
    }
    
    /// <summary>
    /// Convert generic FileCopyResult to Robocopy-specific RobocopyResult.
    /// Used for reverse compatibility when legacy code expects RobocopyResult.
    /// </summary>
    public static RobocopyResult ToRobocopyResult(FileCopyResult generic)
    {
        var result = new RobocopyResult
        {
            // Core result info
            Success = generic.Success,
            State = MapJobState(generic.State),
            ExitCode = generic.ExitCode,
            ExitCodeMessage = generic.ExitCodeMessage,
            
            // Summary statistics
            TotalDirectories = generic.TotalDirectories,
            TotalFiles = generic.TotalFiles,
            TotalBytes = generic.TotalBytes,
            DirectoriesCopied = generic.DirectoriesCopied,
            FilesCopied = generic.FilesCopied,
            BytesCopied = generic.BytesCopied,
            FilesFailed = generic.FilesFailed,
            FilesSkipped = generic.FilesSkipped,
            FilesExtra = generic.FilesExtra,
            FilesMismatched = generic.FilesMismatched,
            
            // Timing
            StartTime = generic.StartTime,
            EndTime = generic.EndTime,
            Duration = generic.Duration,
            ActiveDuration = generic.ActiveDuration,
            AverageBytesPerSecond = generic.AverageBytesPerSecond,
            
            // Errors (convert FileCopyError to RobocopyError)
            Errors = generic.Errors.Select(e => new RobocopyError
            {
                ErrorCode = e.ErrorCode,
                Message = e.Message,
                FilePath = e.FilePath,
                Timestamp = e.Timestamp
            }).ToList(),
            
            // Logging
            LogFilePath = generic.LogFilePath,
            
            // Integrity verification
            IntegrityCheckEnabled = generic.IntegrityCheckEnabled,
            IntegrityCheckMethod = generic.IntegrityCheckEnabled && generic.IntegrityResults?.Count > 0
                ? generic.IntegrityResults[0].Method
                : Models.FileCopy.IntegrityCheckMethod.Metadata,
            FilesVerified = generic.IntegrityChecksPassed + generic.IntegrityChecksFailed,
            FilesVerifiedPassed = generic.IntegrityChecksPassed,
            FilesVerifiedFailed = generic.IntegrityChecksFailed,
            FailedVerifications = generic.IntegrityResults?
                .Where(r => !r.IsValid)
                .Select(r => r.RelativePath)
                .ToList() ?? new List<string>(),
            FailedVerificationDetails = generic.IntegrityResults?
                .Where(r => !r.IsValid)
                .Select(r => new VerificationFailureInfo
                {
                    RelativePath = r.RelativePath,
                    DestinationPath = r.DestinationPath,
                    ErrorMessage = r.ErrorMessage ?? "Unknown error",
                    FileSize = r.FileSize,
                    AttemptCount = r.AttemptCount,
                    Method = r.Method,
                    VerifiedAt = r.VerifiedAt,
                    ExpectedHash = r.ExpectedHash,
                    ActualHash = r.ActualHash
                })
                .ToList() ?? new List<VerificationFailureInfo>()
        };
        
        return result;
    }
    
    /// <summary>
    /// Map RobocopyJobState to FileCopyJobState.
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
    /// Map FileCopyJobState to RobocopyJobState.
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
            FileCopyJobState.Verifying => RobocopyJobState.Running, // Map to Running
            _ => RobocopyJobState.Ready
        };
    }
}
