namespace DiskSpaceAnalyzer.Models.Robocopy;

/// <summary>
/// Common robocopy configuration presets.
/// </summary>
public enum RobocopyPreset
{
    /// <summary>Basic copy with subdirectories.</summary>
    Copy,
    
    /// <summary>Synchronize: copy all including empty dirs.</summary>
    Sync,
    
    /// <summary>Mirror: copy and delete extra files at destination (DANGEROUS).</summary>
    Mirror,
    
    /// <summary>Backup mode: copy with security and timestamps.</summary>
    Backup,
    
    /// <summary>Custom configuration.</summary>
    Custom
}
