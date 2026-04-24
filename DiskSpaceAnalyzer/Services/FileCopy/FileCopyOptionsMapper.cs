using System.Collections.Generic;
using DiskSpaceAnalyzer.Models.FileCopy;
using DiskSpaceAnalyzer.Models.Robocopy;

namespace DiskSpaceAnalyzer.Services.FileCopy;

/// <summary>
///     Maps between generic FileCopyOptions and Robocopy-specific RobocopyOptions.
///     Handles bidirectional conversion and ExtendedOptions dictionary mapping.
/// </summary>
public static class FileCopyOptionsMapper
{
    /// <summary>
    ///     Convert generic FileCopyOptions to Robocopy-specific RobocopyOptions.
    /// </summary>
    public static RobocopyOptions ToRobocopyOptions(FileCopyOptions generic)
    {
        var robocopy = new RobocopyOptions
        {
            // Core paths
            SourcePath = generic.SourcePath,
            DestinationPath = generic.DestinationPath,
            Preset = MapPreset(generic.Preset),

            // Core options
            CopySubdirectories = generic.CopySubdirectories,
            MirrorMode = generic.MirrorMode,
            UseMultithreading = generic.UseParallelCopy,
            ThreadCount = generic.ParallelismDegree,

            // Retry options
            RetryCount = generic.RetryCount,
            RetryWaitSeconds = generic.RetryWaitSeconds,

            // Copy behavior
            BackupMode = generic.BackupMode,
            CopySymbolicLinks = generic.CopySymbolicLinks,
            CopySecurity = generic.CopySecurity,

            // File selection
            IncludeFiles = [..generic.IncludeFiles],
            ExcludeDirectories = [..generic.ExcludeDirectories],
            ExcludeFiles = [..generic.ExcludeFiles],
            MaxFileSize = generic.MaxFileSize,
            MinFileSize = generic.MinFileSize,

            // Advanced options
            CreateTreeOnly = generic.CreateTreeOnly,
            ExcludeOlder = generic.ExcludeOlder,
            ExcludeNewer = generic.ExcludeNewer,
            MoveFiles = generic.MoveFiles,
            MoveFilesAndDirectories = generic.MoveFilesAndDirectories,

            // Integrity verification
            EnableIntegrityCheck = generic.EnableIntegrityCheck,
            IntegrityCheckMethod = generic.IntegrityCheckMethod,

            // Internal
            LogFilePath = generic.LogFilePath
        };

        // Handle ExtendedOptions for robocopy-specific features
        if (generic.ExtendedOptions.Count > 0)
        {
            // CopyFlags (e.g., "DAT", "DATSOU")
            if (generic.ExtendedOptions.TryGetValue("RobocopyCopyFlags", out var copyFlags))
                robocopy.CopyFlags = copyFlags?.ToString() ?? "DAT";
            else if (generic.PreserveAttributes)
                // Default: preserve data, attributes, timestamps
                robocopy.CopyFlags = "DAT";
            else
                // Minimal: just data
                robocopy.CopyFlags = "D";

            // CopyAll override
            if (generic.ExtendedOptions.TryGetValue("RobocopyCopyAll", out var copyAll) &&
                copyAll is bool copyAllBool && copyAllBool)
                robocopy.CopyAll = true;
        }
        else
        {
            // Default CopyFlags based on PreserveAttributes
            robocopy.CopyFlags = generic.PreserveAttributes ? "DAT" : "D";
        }

        return robocopy;
    }

    /// <summary>
    ///     Convert Robocopy-specific RobocopyOptions to generic FileCopyOptions.
    /// </summary>
    public static FileCopyOptions ToFileCopyOptions(RobocopyOptions robocopy)
    {
        var generic = new FileCopyOptions
        {
            // Core paths
            SourcePath = robocopy.SourcePath,
            DestinationPath = robocopy.DestinationPath,
            Preset = MapPreset(robocopy.Preset),
            PreferredEngine = CopyEngineType.Robocopy,

            // Core options
            CopySubdirectories = robocopy.CopySubdirectories,
            MirrorMode = robocopy.MirrorMode,
            UseParallelCopy = robocopy.UseMultithreading,
            ParallelismDegree = robocopy.ThreadCount,

            // Retry options
            RetryCount = robocopy.RetryCount,
            RetryWaitSeconds = robocopy.RetryWaitSeconds,

            // Copy behavior
            PreserveAttributes = robocopy.CopyFlags.Contains('T') || robocopy.CopyFlags.Contains('A'),
            BackupMode = robocopy.BackupMode,
            CopySymbolicLinks = robocopy.CopySymbolicLinks,
            CopySecurity = robocopy.CopySecurity || robocopy.CopyAll,

            // File selection
            IncludeFiles = [..robocopy.IncludeFiles],
            ExcludeDirectories = [..robocopy.ExcludeDirectories],
            ExcludeFiles = [..robocopy.ExcludeFiles],
            MaxFileSize = robocopy.MaxFileSize,
            MinFileSize = robocopy.MinFileSize,

            // Advanced options
            CreateTreeOnly = robocopy.CreateTreeOnly,
            ExcludeOlder = robocopy.ExcludeOlder,
            ExcludeNewer = robocopy.ExcludeNewer,
            MoveFiles = robocopy.MoveFiles,
            MoveFilesAndDirectories = robocopy.MoveFilesAndDirectories,

            // Integrity verification
            EnableIntegrityCheck = robocopy.EnableIntegrityCheck,
            IntegrityCheckMethod = robocopy.IntegrityCheckMethod,

            // Internal
            LogFilePath = robocopy.LogFilePath
        };

        // Store robocopy-specific options in ExtendedOptions
        generic.ExtendedOptions["RobocopyCopyFlags"] = robocopy.CopyFlags;
        if (robocopy.CopyAll) generic.ExtendedOptions["RobocopyCopyAll"] = true;

        return generic;
    }

    /// <summary>
    ///     Map FileCopyPreset to RobocopyPreset.
    /// </summary>
    private static RobocopyPreset MapPreset(FileCopyPreset preset)
    {
        return preset switch
        {
            FileCopyPreset.Copy => RobocopyPreset.Copy,
            FileCopyPreset.Sync => RobocopyPreset.Sync,
            FileCopyPreset.Mirror => RobocopyPreset.Mirror,
            FileCopyPreset.Backup => RobocopyPreset.Backup,
            FileCopyPreset.Move => RobocopyPreset.Custom, // Robocopy doesn't have Move preset
            FileCopyPreset.Custom => RobocopyPreset.Custom,
            _ => RobocopyPreset.Copy
        };
    }

    /// <summary>
    ///     Map RobocopyPreset to FileCopyPreset.
    /// </summary>
    private static FileCopyPreset MapPreset(RobocopyPreset preset)
    {
        return preset switch
        {
            RobocopyPreset.Copy => FileCopyPreset.Copy,
            RobocopyPreset.Sync => FileCopyPreset.Sync,
            RobocopyPreset.Mirror => FileCopyPreset.Mirror,
            RobocopyPreset.Backup => FileCopyPreset.Backup,
            RobocopyPreset.Custom => FileCopyPreset.Custom,
            _ => FileCopyPreset.Copy
        };
    }
}