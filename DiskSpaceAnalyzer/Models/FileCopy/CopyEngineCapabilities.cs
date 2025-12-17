using System.Collections.Generic;

namespace DiskSpaceAnalyzer.Models.FileCopy;

/// <summary>
/// Describes the capabilities and characteristics of a file copy engine.
/// Used by UI and selection logic to determine which features are available
/// and which engine is best suited for a given scenario.
/// </summary>
public class CopyEngineCapabilities
{
    /// <summary>
    /// Display name of the engine (e.g., "Robocopy", "Native C#").
    /// </summary>
    public string Name { get; init; } = string.Empty;
    
    /// <summary>
    /// Short description of the engine (e.g., "Windows robocopy.exe process").
    /// </summary>
    public string Description { get; init; } = string.Empty;
    
    /// <summary>
    /// The engine type identifier.
    /// </summary>
    public CopyEngineType EngineType { get; init; }
    
    // Core Capabilities
    
    /// <summary>
    /// Whether the engine supports pausing and resuming operations.
    /// Robocopy: Yes (via process suspension)
    /// Native: Possible but complex (requires state preservation)
    /// </summary>
    public bool SupportsPauseResume { get; init; }
    
    /// <summary>
    /// Whether the engine supports mirror mode (delete files at destination not in source).
    /// Robocopy: Yes (/MIR flag)
    /// Native: Possible but requires careful implementation
    /// </summary>
    public bool SupportsMirrorMode { get; init; }
    
    /// <summary>
    /// Whether the engine has specific optimizations for network operations.
    /// Robocopy: Yes (retry logic, restartable mode, etc.)
    /// Native: No (but can be added)
    /// </summary>
    public bool SupportsNetworkOptimization { get; init; }
    
    /// <summary>
    /// Whether the engine supports Windows backup mode (copy locked files).
    /// Robocopy: Yes (/B flag)
    /// Native: No (requires SeBackupPrivilege and special APIs)
    /// </summary>
    public bool SupportsBackupMode { get; init; }
    
    /// <summary>
    /// Whether the engine can copy symbolic links as links (not targets).
    /// Robocopy: Yes (/SL flag)
    /// Native: Yes (with proper APIs)
    /// </summary>
    public bool SupportsSymbolicLinks { get; init; }
    
    /// <summary>
    /// Whether the engine can preserve NTFS security information.
    /// Robocopy: Yes (/SEC, /COPYALL flags)
    /// Native: Yes (with proper APIs)
    /// </summary>
    public bool SupportsSecurityInfo { get; init; }
    
    /// <summary>
    /// Whether the engine supports multi-threaded/parallel copying.
    /// Robocopy: Yes (/MT flag)
    /// Native: Yes (Task.Parallel, configurable)
    /// </summary>
    public bool SupportsParallelCopy { get; init; }
    
    /// <summary>
    /// Whether the engine requires an external tool/executable.
    /// Robocopy: Yes (robocopy.exe)
    /// Native: No (pure .NET)
    /// </summary>
    public bool RequiresExternalTool { get; init; }
    
    /// <summary>
    /// Whether the engine provides real-time byte-level progress (not just file-level).
    /// Robocopy: Limited (parses output, approximate)
    /// Native: Yes (direct file stream monitoring)
    /// </summary>
    public bool SupportsByteProgressTracking { get; init; }
    
    // Performance Characteristics
    
    /// <summary>
    /// Typical speed category for local SSD operations.
    /// Values: "Fast", "Very Fast", "Ultra Fast", "Medium", "Slow"
    /// </summary>
    public string LocalPerformance { get; init; } = "Medium";
    
    /// <summary>
    /// Typical speed category for network operations.
    /// Values: "Fast", "Very Fast", "Medium", "Slow"
    /// </summary>
    public string NetworkPerformance { get; init; } = "Medium";
    
    /// <summary>
    /// Typical speed category for HDD operations.
    /// Values: "Fast", "Medium", "Slow"
    /// </summary>
    public string HDDPerformance { get; init; } = "Medium";
    
    // Optimal Use Cases
    
    /// <summary>
    /// List of scenarios where this engine is recommended.
    /// Examples:
    /// - "Local SSD to SSD copies"
    /// - "Network share synchronization"
    /// - "Backup with locked files"
    /// - "Large file transfers"
    /// - "Many small files"
    /// </summary>
    public List<string> OptimalScenarios { get; init; } = new();
    
    /// <summary>
    /// List of scenarios where this engine should be avoided.
    /// Examples:
    /// - "Unreliable network connections"
    /// - "Locked system files"
    /// - "Cross-platform operations"
    /// </summary>
    public List<string> LimitedScenarios { get; init; } = new();
    
    /// <summary>
    /// Platform availability.
    /// Values: "Windows", "Linux", "macOS", "Cross-Platform"
    /// </summary>
    public string Platform { get; init; } = "Windows";
    
    /// <summary>
    /// Minimum Windows version required (if Windows-only).
    /// Examples: "Windows Vista", "Windows 7", "Windows 10"
    /// null if cross-platform or no minimum version.
    /// </summary>
    public string? MinimumWindowsVersion { get; init; }
}
