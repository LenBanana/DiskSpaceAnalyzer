using System.Collections.Generic;
using DiskSpaceAnalyzer.Models.FileCopy;

namespace DiskSpaceAnalyzer.Services.Robocopy;

/// <summary>
///     Describes the capabilities and characteristics of the Robocopy copy engine.
///     Used by the factory and UI to determine engine suitability for different scenarios.
/// </summary>
public static class RobocopyCapabilities
{
    /// <summary>
    ///     Singleton instance of robocopy's capabilities.
    /// </summary>
    public static readonly CopyEngineCapabilities Instance = new()
    {
        // Identity
        Name = "Robocopy",
        Description = "Windows Robust File Copy - Command-line file copying utility built into Windows",
        EngineType = CopyEngineType.Robocopy,

        // Feature support flags
        SupportsPauseResume = true, // Via process suspension
        SupportsMirrorMode = true, // /MIR flag
        SupportsNetworkOptimization = true, // Automatic retry and restartable mode
        SupportsBackupMode = true, // /B flag with SeBackupPrivilege
        SupportsSymbolicLinks = true, // /SL flag
        SupportsSecurityInfo = true, // /SEC and /COPYALL flags
        SupportsParallelCopy = true, // /MT:n flag (1-128 threads)
        SupportsByteProgressTracking = false, // Limited - only via text output parsing

        // Requirements
        RequiresExternalTool = true, // Depends on robocopy.exe
        Platform = "Windows",
        MinimumWindowsVersion = "Windows Vista", // Built-in since Vista

        // Performance characteristics
        LocalPerformance = "Medium - Process overhead and text parsing limit speed",
        NetworkPerformance = "Excellent - Designed for network copies with automatic retry",
        HDDPerformance = "Good - Multi-threaded helps with mechanical drive latency",

        // Optimal use cases
        OptimalScenarios =
        [
            "Network share synchronization",
            "Backup operations with locked files (using /B backup mode)",
            "Mirror mode with automatic deletion of extra files",
            "Unreliable network connections (automatic retry and resume)",
            "Complex filtering and exclusion patterns",
            "Copying NTFS security information and attributes",
            "Scenarios requiring detailed logging",
            "Compatibility with existing robocopy scripts"
        ],

        // Limited or suboptimal scenarios
        LimitedScenarios =
        [
            "Local SSD to SSD copies (native engine is faster)",
            "Systems without robocopy.exe (pre-Vista Windows)",
            "Real-time byte-level progress tracking needed",
            "Scenarios requiring embedded execution (no external process)",
            "Cross-platform requirements (robocopy is Windows-only)",
            "Very small file sets (process startup overhead)"
        ]
    };
}