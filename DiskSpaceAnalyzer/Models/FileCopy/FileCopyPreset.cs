namespace DiskSpaceAnalyzer.Models.FileCopy;

/// <summary>
///     Common file copy configuration presets.
///     These map to sensible defaults that can be applied to any copy engine.
/// </summary>
public enum FileCopyPreset
{
    /// <summary>
    ///     Basic copy with subdirectories.
    ///     Copies all files and folders from source to destination.
    ///     Does not delete extra files at destination.
    /// </summary>
    Copy,

    /// <summary>
    ///     Synchronize: copy all files including empty directories.
    ///     Skips files that are already up-to-date at destination.
    ///     Does not delete extra files.
    /// </summary>
    Sync,

    /// <summary>
    ///     Mirror: exact replica of source at destination.
    ///     WARNING: Deletes files at destination that don't exist in source.
    ///     Dangerous - use with caution!
    /// </summary>
    Mirror,

    /// <summary>
    ///     Backup mode: copy with all attributes and security information.
    ///     Preserves timestamps, security descriptors, and ownership.
    ///     May require administrator privileges.
    /// </summary>
    Backup,

    /// <summary>
    ///     Move operation: copy files then delete from source.
    ///     Effectively moves files from source to destination.
    ///     Cannot be combined with Mirror mode.
    /// </summary>
    Move,

    /// <summary>
    ///     Custom configuration - user has manually set options.
    /// </summary>
    Custom
}