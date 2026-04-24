using System;
using System.Collections.Generic;

namespace DiskSpaceAnalyzer.Models.FileCopy;

/// <summary>
///     Configuration options for a file copy operation.
///     Generic across all copy engines - specific engines may use only relevant options.
///     Designed to be extensible - add new options without breaking existing code.
/// </summary>
public class FileCopyOptions
{
    /// <summary>Source directory path.</summary>
    public string SourcePath { get; set; } = string.Empty;

    /// <summary>Destination directory path.</summary>
    public string DestinationPath { get; set; } = string.Empty;

    /// <summary>Selected configuration preset.</summary>
    public FileCopyPreset Preset { get; set; } = FileCopyPreset.Copy;

    /// <summary>Preferred copy engine (Auto = automatic selection).</summary>
    public CopyEngineType PreferredEngine { get; set; } = CopyEngineType.Auto;

    // Core options

    /// <summary>Copy subdirectories, including empty ones.</summary>
    public bool CopySubdirectories { get; set; } = true;

    /// <summary>
    ///     Mirror mode - delete files at destination that don't exist at source.
    ///     WARNING: Destructive operation!
    /// </summary>
    public bool MirrorMode { get; set; }

    /// <summary>Use parallel/multi-threaded copying when supported by engine.</summary>
    public bool UseParallelCopy { get; set; } = true;

    /// <summary>
    ///     Number of parallel operations (threads/tasks).
    ///     Interpretation varies by engine:
    ///     - Robocopy: /MT:n flag (1-128)
    ///     - Native: Parallel.ForEachAsync degree of parallelism
    /// </summary>
    public int ParallelismDegree { get; set; } = 8;

    // Retry options

    /// <summary>Number of retries on failed copies.</summary>
    public int RetryCount { get; set; } = 3;

    /// <summary>Wait time between retries in seconds.</summary>
    public int RetryWaitSeconds { get; set; } = 5;

    // Copy behavior options

    /// <summary>
    ///     Copy attributes and timestamps.
    ///     When true: preserve all file metadata (created, modified, attributes).
    ///     When false: files get current timestamp and default attributes.
    /// </summary>
    public bool PreserveAttributes { get; set; } = true;

    /// <summary>
    ///     Use backup mode to copy files that are normally locked.
    ///     Requires: SeBackupPrivilege and administrator rights.
    ///     Supported by: Robocopy, potentially Native with Volume Shadow Copy.
    /// </summary>
    public bool BackupMode { get; set; }

    /// <summary>Copy symbolic links as links rather than following to targets.</summary>
    public bool CopySymbolicLinks { get; set; } = false;

    /// <summary>
    ///     Copy NTFS security information (ACLs, ownership).
    ///     Requires: Appropriate permissions.
    /// </summary>
    public bool CopySecurity { get; set; }

    // File selection and filtering

    /// <summary>
    ///     File patterns to include (empty = all files).
    ///     Supports wildcards: *.txt, data*.*, etc.
    /// </summary>
    public List<string> IncludeFiles { get; set; } = new();

    /// <summary>
    ///     Directory names to exclude (relative or absolute).
    ///     Examples: "node_modules", ".git", "temp"
    /// </summary>
    public List<string> ExcludeDirectories { get; set; } = new();

    /// <summary>
    ///     File names or patterns to exclude.
    ///     Supports wildcards: *.tmp, ~$*, etc.
    /// </summary>
    public List<string> ExcludeFiles { get; set; } = new();

    /// <summary>Exclude files larger than specified size in bytes.</summary>
    public long? MaxFileSize { get; set; }

    /// <summary>Exclude files smaller than specified size in bytes.</summary>
    public long? MinFileSize { get; set; }

    // Advanced options

    /// <summary>Only create directory structure without copying file contents.</summary>
    public bool CreateTreeOnly { get; set; } = false;

    /// <summary>Only copy files that already exist at destination (update existing).</summary>
    public bool ExcludeOlder { get; set; }

    /// <summary>Exclude newer files from source (don't overwrite newer destination files).</summary>
    public bool ExcludeNewer { get; set; } = false;

    /// <summary>
    ///     Move files (delete from source after successful copy).
    ///     Cannot be combined with MirrorMode.
    /// </summary>
    public bool MoveFiles { get; set; }

    /// <summary>Move files and directories (includes directory deletion).</summary>
    public bool MoveFilesAndDirectories { get; set; } = false;

    // Integrity verification

    /// <summary>Enable file integrity verification after copy.</summary>
    public bool EnableIntegrityCheck { get; set; } = false;

    /// <summary>Method to use for integrity verification.</summary>
    public IntegrityCheckMethod IntegrityCheckMethod { get; set; } = IntegrityCheckMethod.Metadata;

    // Engine-specific options

    /// <summary>
    ///     Extended options for engine-specific features.
    ///     Use this for features that don't apply to all engines.
    ///     Example: { "RobocopyCopyFlags": "DAT", "NativeBufferSize": 81920 }
    /// </summary>
    public Dictionary<string, object> ExtendedOptions { get; set; } = new();

    /// <summary>
    ///     Internal log file path (managed by service).
    /// </summary>
    internal string? LogFilePath { get; set; }

    /// <summary>
    ///     Validate the options for common errors.
    /// </summary>
    public (bool IsValid, string? ErrorMessage) Validate()
    {
        if (string.IsNullOrWhiteSpace(SourcePath))
            return (false, "Source path is required.");

        if (string.IsNullOrWhiteSpace(DestinationPath))
            return (false, "Destination path is required.");

        if (SourcePath.Equals(DestinationPath, StringComparison.OrdinalIgnoreCase))
            return (false, "Source and destination cannot be the same.");

        if (ParallelismDegree < 1 || ParallelismDegree > 128)
            return (false, "Parallelism degree must be between 1 and 128.");

        if (RetryCount < 0)
            return (false, "Retry count cannot be negative.");

        if (RetryWaitSeconds < 0)
            return (false, "Retry wait time cannot be negative.");

        if (MoveFiles && MirrorMode)
            return (false, "Move and Mirror modes cannot be used together.");

        if (MirrorMode && !CopySubdirectories)
            return (false, "Mirror mode requires copying subdirectories.");

        return (true, null);
    }

    /// <summary>
    ///     Create options from a preset.
    /// </summary>
    public static FileCopyOptions FromPreset(FileCopyPreset preset)
    {
        var options = new FileCopyOptions { Preset = preset };

        switch (preset)
        {
            case FileCopyPreset.Copy:
                options.CopySubdirectories = true;
                options.UseParallelCopy = true;
                options.PreserveAttributes = true;
                break;

            case FileCopyPreset.Sync:
                options.CopySubdirectories = true;
                options.UseParallelCopy = true;
                options.PreserveAttributes = true;
                options.ExcludeOlder = true;
                break;

            case FileCopyPreset.Mirror:
                options.CopySubdirectories = true;
                options.MirrorMode = true;
                options.UseParallelCopy = true;
                options.PreserveAttributes = true;
                break;

            case FileCopyPreset.Backup:
                options.CopySubdirectories = true;
                options.CopySecurity = true;
                options.BackupMode = true;
                options.PreserveAttributes = true;
                options.UseParallelCopy = false; // More reliable single-threaded for backups
                options.RetryCount = 5; // More retries for important backup operations
                break;

            case FileCopyPreset.Move:
                options.CopySubdirectories = true;
                options.UseParallelCopy = true;
                options.PreserveAttributes = true;
                options.MoveFiles = true;
                break;

            case FileCopyPreset.Custom:
                // Default settings, user will customize
                break;
        }

        return options;
    }

    /// <summary>
    ///     Create a deep copy of these options.
    /// </summary>
    public FileCopyOptions Clone()
    {
        var clone = (FileCopyOptions)MemberwiseClone();
        clone.IncludeFiles = new List<string>(IncludeFiles);
        clone.ExcludeDirectories = new List<string>(ExcludeDirectories);
        clone.ExcludeFiles = new List<string>(ExcludeFiles);
        clone.ExtendedOptions = new Dictionary<string, object>(ExtendedOptions);
        return clone;
    }
}