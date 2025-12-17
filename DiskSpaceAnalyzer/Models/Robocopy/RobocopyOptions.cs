using System.Collections.Generic;
using DiskSpaceAnalyzer.Models.FileCopy;

namespace DiskSpaceAnalyzer.Models.Robocopy;

/// <summary>
/// Configuration options for a robocopy operation.
/// Designed to be extensible - add new options without breaking existing code.
/// </summary>
public class RobocopyOptions
{
    /// <summary>Source directory path.</summary>
    public string SourcePath { get; set; } = string.Empty;
    
    /// <summary>Destination directory path.</summary>
    public string DestinationPath { get; set; } = string.Empty;
    
    /// <summary>Selected configuration preset.</summary>
    public RobocopyPreset Preset { get; set; } = RobocopyPreset.Copy;
    
    // Core options
    /// <summary>Copy subdirectories, including empty ones (/E).</summary>
    public bool CopySubdirectories { get; set; } = true;
    
    /// <summary>Mirror mode - delete files at destination that don't exist at source (/MIR).</summary>
    public bool MirrorMode { get; set; } = false;
    
    /// <summary>Use multi-threaded copying (/MT:n).</summary>
    public bool UseMultithreading { get; set; } = true;
    
    /// <summary>Number of threads for multi-threaded copying (1-128).</summary>
    public int ThreadCount { get; set; } = 8;
    
    // Retry options
    /// <summary>Number of retries on failed copies (/R:n).</summary>
    public int RetryCount { get; set; } = 3;
    
    /// <summary>Wait time between retries in seconds (/W:n).</summary>
    public int RetryWaitSeconds { get; set; } = 5;
    
    // Copy options
    /// <summary>Copy file data, attributes, and timestamps (/COPY:DAT).</summary>
    public string CopyFlags { get; set; } = "DAT"; // D=Data, A=Attributes, T=Timestamps, S=Security, O=Owner, U=Auditing
    
    /// <summary>Use backup mode to copy files that are normally locked (/B).</summary>
    public bool BackupMode { get; set; } = false;
    
    /// <summary>Copy symbolic links as links rather than targets (/SL).</summary>
    public bool CopySymbolicLinks { get; set; } = false;
    
    // File selection
    /// <summary>File patterns to include (empty = all files).</summary>
    public List<string> IncludeFiles { get; set; } = new();
    
    /// <summary>Directory names to exclude (/XD).</summary>
    public List<string> ExcludeDirectories { get; set; } = new();
    
    /// <summary>File names or patterns to exclude (/XF).</summary>
    public List<string> ExcludeFiles { get; set; } = new();
    
    /// <summary>Exclude files larger than specified size in bytes (/MAX:n).</summary>
    public long? MaxFileSize { get; set; }
    
    /// <summary>Exclude files smaller than specified size in bytes (/MIN:n).</summary>
    public long? MinFileSize { get; set; }
    
    // Advanced options (extensibility - add here without breaking existing code)
    /// <summary>Copy files with security info (/SEC).</summary>
    public bool CopySecurity { get; set; } = false;
    
    /// <summary>Copy all file info including security (/COPYALL).</summary>
    public bool CopyAll { get; set; } = false;
    
    /// <summary>Don't copy any file info, only create directory structure (/CREATE).</summary>
    public bool CreateTreeOnly { get; set; } = false;
    
    /// <summary>Only copy files that already exist at destination (/XO).</summary>
    public bool ExcludeOlder { get; set; } = false;
    
    /// <summary>Exclude newer files from source (/XN).</summary>
    public bool ExcludeNewer { get; set; } = false;
    
    /// <summary>Move files (delete from source after copying) (/MOV).</summary>
    public bool MoveFiles { get; set; } = false;
    
    /// <summary>Move files and directories (/MOVE).</summary>
    public bool MoveFilesAndDirectories { get; set; } = false;
    
    // Integrity verification
    /// <summary>Enable file integrity verification after copy.</summary>
    public bool EnableIntegrityCheck { get; set; } = false;
    
    /// <summary>Method to use for integrity verification.</summary>
    public IntegrityCheckMethod IntegrityCheckMethod { get; set; } = IntegrityCheckMethod.Metadata;
    
    // Logging (internal - managed by service)
    internal string? LogFilePath { get; set; }
    
    /// <summary>
    /// Validate the options for common errors.
    /// </summary>
    public (bool IsValid, string? ErrorMessage) Validate()
    {
        if (string.IsNullOrWhiteSpace(SourcePath))
            return (false, "Source path is required.");
        
        if (string.IsNullOrWhiteSpace(DestinationPath))
            return (false, "Destination path is required.");
        
        if (SourcePath.Equals(DestinationPath, System.StringComparison.OrdinalIgnoreCase))
            return (false, "Source and destination cannot be the same.");
        
        if (ThreadCount < 1 || ThreadCount > 128)
            return (false, "Thread count must be between 1 and 128.");
        
        if (RetryCount < 0)
            return (false, "Retry count cannot be negative.");
        
        if (RetryWaitSeconds < 0)
            return (false, "Retry wait time cannot be negative.");
        
        if (MoveFiles && MirrorMode)
            return (false, "Move and Mirror modes cannot be used together.");
        
        return (true, null);
    }
    
    /// <summary>
    /// Create options from a preset.
    /// </summary>
    public static RobocopyOptions FromPreset(RobocopyPreset preset)
    {
        var options = new RobocopyOptions { Preset = preset };
        
        switch (preset)
        {
            case RobocopyPreset.Copy:
                options.CopySubdirectories = true;
                options.UseMultithreading = true;
                break;
            
            case RobocopyPreset.Sync:
                options.CopySubdirectories = true;
                options.UseMultithreading = true;
                options.ExcludeOlder = true;
                break;
            
            case RobocopyPreset.Mirror:
                options.CopySubdirectories = true;
                options.MirrorMode = true;
                options.UseMultithreading = true;
                break;
            
            case RobocopyPreset.Backup:
                options.CopySubdirectories = true;
                options.CopyAll = true;
                options.BackupMode = true;
                options.UseMultithreading = false; // More reliable for backup
                break;
            
            case RobocopyPreset.Custom:
                // Default settings, user will customize
                break;
        }
        
        return options;
    }
}
