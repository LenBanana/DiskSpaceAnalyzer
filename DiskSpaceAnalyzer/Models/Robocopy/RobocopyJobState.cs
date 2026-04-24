namespace DiskSpaceAnalyzer.Models.Robocopy;

/// <summary>
///     Represents the current state of a robocopy job.
/// </summary>
public enum RobocopyJobState
{
    /// <summary>Job is ready to start but not yet initiated.</summary>
    Ready,

    /// <summary>Pre-scanning source directory to calculate total size.</summary>
    Scanning,

    /// <summary>Robocopy is actively copying files.</summary>
    Running,

    /// <summary>Job is paused (process suspended).</summary>
    Paused,

    /// <summary>Job completed successfully (may have warnings).</summary>
    Completed,

    /// <summary>Job was cancelled by user.</summary>
    Cancelled,

    /// <summary>Job failed with fatal errors.</summary>
    Failed
}