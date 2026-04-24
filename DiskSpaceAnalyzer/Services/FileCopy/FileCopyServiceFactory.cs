using System;
using System.Collections.Generic;
using System.IO;
using DiskSpaceAnalyzer.Models.FileCopy;
using DiskSpaceAnalyzer.Services.Robocopy;

namespace DiskSpaceAnalyzer.Services.FileCopy;

/// <summary>
///     Factory for creating file copy service instances with intelligent engine selection.
///     Analyzes copy scenarios and recommends the optimal engine (Robocopy or Native C#)
///     based on paths, features required, and performance characteristics.
/// </summary>
/// <remarks>
///     Selection Strategy:
///     - Manual override (PreferredEngine != Auto) always respected
///     - Backup mode required → Robocopy (only option)
///     - Network paths → Robocopy (better retry logic)
///     - Security info required → Robocopy (better ACL support)
///     - Local operations → Native (2-4x faster)
///     - Robocopy unavailable → Native (graceful fallback)
/// </remarks>
public class FileCopyServiceFactory : IFileCopyServiceFactory
{
    private readonly IFileIntegrityService _integrityService;
    private readonly NativeFileCopyService _nativeService;
    private readonly IRobocopyService _robocopyService;

    /// <summary>
    ///     Initializes a new instance of the FileCopyServiceFactory.
    /// </summary>
    /// <param name="nativeService">Native C# copy engine instance.</param>
    /// <param name="robocopyService">Robocopy engine instance.</param>
    /// <param name="integrityService">Integrity verification service (used by both engines).</param>
    public FileCopyServiceFactory(
        NativeFileCopyService nativeService,
        IRobocopyService robocopyService,
        IFileIntegrityService integrityService)
    {
        _nativeService = nativeService ?? throw new ArgumentNullException(nameof(nativeService));
        _robocopyService = robocopyService ?? throw new ArgumentNullException(nameof(robocopyService));
        _integrityService = integrityService ?? throw new ArgumentNullException(nameof(integrityService));
    }

    /// <summary>
    ///     Creates a file copy service instance based on the provided options.
    ///     Automatically selects the optimal engine if options.PreferredEngine is Auto.
    /// </summary>
    /// <param name="options">Copy options that influence engine selection.</param>
    /// <returns>
    ///     An IFileCopyService implementation suitable for the scenario.
    ///     Never returns null - will always provide a working engine.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown if options is null.</exception>
    public IFileCopyService CreateService(FileCopyOptions options)
    {
        if (options == null)
            throw new ArgumentNullException(nameof(options));

        // Manual override - respect user's explicit choice
        if (options.PreferredEngine != CopyEngineType.Auto) return CreateServiceByType(options.PreferredEngine);

        // Auto-selection: analyze scenario and pick best engine
        var selectedEngine = SelectOptimalEngine(options);
        return CreateServiceByType(selectedEngine);
    }

    /// <summary>
    ///     Creates a specific copy engine by type.
    /// </summary>
    /// <param name="engineType">The type of engine to create.</param>
    /// <returns>
    ///     The requested engine implementation.
    ///     Returns Native if requested engine is unavailable.
    /// </returns>
    public IFileCopyService CreateService(CopyEngineType engineType)
    {
        return CreateServiceByType(engineType);
    }

    /// <summary>
    ///     Gets all copy engines that are currently available on this system.
    /// </summary>
    /// <returns>Array of available engine types.</returns>
    public CopyEngineType[] GetAvailableEngines()
    {
        var available = new List<CopyEngineType>();

        // Native is always available (pure .NET)
        available.Add(CopyEngineType.Native);

        // Check if robocopy.exe is available
        if (IsRobocopyAvailable()) available.Add(CopyEngineType.Robocopy);

        return available.ToArray();
    }

    /// <summary>
    ///     Recommends the best engine for a given scenario with detailed reasoning.
    ///     Useful for showing users why a particular engine was selected.
    /// </summary>
    /// <param name="options">Copy options to analyze.</param>
    /// <returns>
    ///     EngineRecommendation containing the recommended engine, reasoning,
    ///     confidence level, and alternatives.
    /// </returns>
    /// <exception cref="ArgumentNullException">Thrown if options is null.</exception>
    public EngineRecommendation RecommendEngine(FileCopyOptions options)
    {
        if (options == null)
            throw new ArgumentNullException(nameof(options));

        // Analyze the scenario
        var sourcePath = options.SourcePath;
        var destPath = options.DestinationPath;
        var sourceIsNetwork = CopyScenarioAnalyzer.IsNetworkPath(sourcePath);
        var destIsNetwork = CopyScenarioAnalyzer.IsNetworkPath(destPath);
        var robocopyAvailable = IsRobocopyAvailable();

        // Build scenario summary (used by all recommendations)
        var scenarioSummary = BuildScenarioSummary(sourcePath, destPath, sourceIsNetwork, destIsNetwork);

        // Decision logic with reasoning

        // 1. Backup mode - MUST use robocopy
        if (options.BackupMode)
        {
            if (robocopyAvailable)
                return new EngineRecommendation
                {
                    RecommendedEngine = CopyEngineType.Robocopy,
                    Reasoning =
                        "Backup mode required - only Robocopy supports copying locked files with SeBackupPrivilege (/B flag).",
                    Confidence = 1.0,
                    Alternatives = new Dictionary<CopyEngineType, string>
                    {
                        {
                            CopyEngineType.Native,
                            "Not supported - cannot copy locked files without Volume Shadow Copy integration."
                        }
                    },
                    ExpectedPerformance = sourceIsNetwork || destIsNetwork ? "Network-Limited" : "Good",
                    ScenarioSummary = scenarioSummary
                };

            return new EngineRecommendation
            {
                RecommendedEngine = CopyEngineType.Native,
                Reasoning =
                    "Backup mode requested but Robocopy unavailable. Native engine will be used but locked files cannot be copied.",
                Confidence = 0.3,
                Warnings =
                [
                    "Backup mode not supported by Native engine - locked files will be skipped.",
                    "Consider installing Robocopy or making Robocopy.exe available in PATH."
                ],
                ExpectedPerformance = "Fast (but incomplete)",
                ScenarioSummary = scenarioSummary
            };
        }

        // 2. Security info - strongly prefer robocopy
        if (options.CopySecurity)
            if (robocopyAvailable)
                return new EngineRecommendation
                {
                    RecommendedEngine = CopyEngineType.Robocopy,
                    Reasoning =
                        "Security information copying requested - Robocopy has comprehensive NTFS ACL and ownership support (/SEC, /COPYALL flags).",
                    Confidence = 0.9,
                    Alternatives = new Dictionary<CopyEngineType, string>
                    {
                        { CopyEngineType.Native, "Limited ACL support - may not preserve all security attributes." }
                    },
                    ExpectedPerformance = sourceIsNetwork || destIsNetwork ? "Network-Limited" : "Good",
                    ScenarioSummary = scenarioSummary
                };

        // 3. Network paths - prefer robocopy for reliability
        if (sourceIsNetwork || destIsNetwork)
        {
            if (robocopyAvailable)
                return new EngineRecommendation
                {
                    RecommendedEngine = CopyEngineType.Robocopy,
                    Reasoning =
                        "Network path detected - Robocopy provides superior reliability with automatic retry logic, restartable mode, and decades of network optimization.",
                    Confidence = 0.85,
                    Alternatives = new Dictionary<CopyEngineType, string>
                    {
                        {
                            CopyEngineType.Native,
                            "Will work but lacks specialized network retry logic. May fail on unreliable connections."
                        }
                    },
                    ExpectedPerformance = "Network-Limited (Reliable)",
                    ScenarioSummary = scenarioSummary
                };

            return new EngineRecommendation
            {
                RecommendedEngine = CopyEngineType.Native,
                Reasoning =
                    "Network path detected but Robocopy unavailable. Native engine will be used with basic retry logic.",
                Confidence = 0.6,
                Warnings =
                [
                    "Native engine lacks advanced network retry logic - may be less reliable on poor connections.",
                    "For best network performance, consider making Robocopy available."
                ],
                ExpectedPerformance = "Network-Limited",
                ScenarioSummary = scenarioSummary
            };
        }

        // 4. Local operations - prefer native for speed
        if (CopyScenarioAnalyzer.WouldBenefitFromNativeSpeed(sourcePath, destPath))
        {
            var alternatives = new Dictionary<CopyEngineType, string>();
            if (robocopyAvailable)
                alternatives.Add(CopyEngineType.Robocopy,
                    "More reliable for edge cases but 2-4x slower for local SSD operations.");

            return new EngineRecommendation
            {
                RecommendedEngine = CopyEngineType.Native,
                Reasoning =
                    "Local drive operation - Native C# engine provides 2-4x faster performance with direct async I/O, no process overhead, and real-time byte-level progress tracking.",
                Confidence = 0.85,
                Alternatives = alternatives,
                ExpectedPerformance = "Very Fast (Local SSD)",
                ScenarioSummary = scenarioSummary
            };
        }

        // 5. Default case - prefer robocopy if available, otherwise native
        if (robocopyAvailable)
            return new EngineRecommendation
            {
                RecommendedEngine = CopyEngineType.Robocopy,
                Reasoning =
                    "Default selection - Robocopy provides proven reliability for general-purpose file copying.",
                Confidence = 0.7,
                Alternatives = new Dictionary<CopyEngineType, string>
                {
                    {
                        CopyEngineType.Native,
                        "Would be faster for local operations but Robocopy chosen for reliability."
                    }
                },
                ExpectedPerformance = "Good",
                ScenarioSummary = scenarioSummary
            };

        return new EngineRecommendation
        {
            RecommendedEngine = CopyEngineType.Native,
            Reasoning = "Native C# engine selected - Robocopy unavailable on this system.",
            Confidence = 0.8,
            Alternatives = new Dictionary<CopyEngineType, string>(),
            ExpectedPerformance = "Fast",
            ScenarioSummary = scenarioSummary
        };
    }

    #region Private Helper Methods

    /// <summary>
    ///     Internal method to create a service by type with availability checking.
    /// </summary>
    private IFileCopyService CreateServiceByType(CopyEngineType engineType)
    {
        switch (engineType)
        {
            case CopyEngineType.Robocopy:
                if (IsRobocopyAvailable()) return _robocopyService as IFileCopyService ?? _nativeService;

                // Graceful fallback to Native if Robocopy requested but unavailable
                return _nativeService;

            case CopyEngineType.Native:
                return _nativeService;

            case CopyEngineType.Auto:
                // Should not reach here (handled in CreateService), but fallback to native
                return _nativeService;

            default:
                // Unknown engine type - fallback to native
                return _nativeService;
        }
    }

    /// <summary>
    ///     Selects the optimal engine based on scenario analysis.
    ///     Core auto-selection logic.
    /// </summary>
    private CopyEngineType SelectOptimalEngine(FileCopyOptions options)
    {
        var sourcePath = options.SourcePath;
        var destPath = options.DestinationPath;

        // 1. Feature requirements (Robocopy-only features)
        if (options.BackupMode && IsRobocopyAvailable()) return CopyEngineType.Robocopy;

        if (options.CopySecurity && IsRobocopyAvailable()) return CopyEngineType.Robocopy;

        // 2. Path analysis
        var sourceIsNetwork = CopyScenarioAnalyzer.IsNetworkPath(sourcePath);
        var destIsNetwork = CopyScenarioAnalyzer.IsNetworkPath(destPath);

        // Network paths - prefer robocopy for reliability
        if ((sourceIsNetwork || destIsNetwork) && IsRobocopyAvailable()) return CopyEngineType.Robocopy;

        // 3. Local operations - prefer native for speed
        if (CopyScenarioAnalyzer.WouldBenefitFromNativeSpeed(sourcePath, destPath)) return CopyEngineType.Native;

        // 4. Default: prefer robocopy if available (proven reliability)
        if (IsRobocopyAvailable()) return CopyEngineType.Robocopy;

        // 5. Fallback: native (always available)
        return CopyEngineType.Native;
    }

    /// <summary>
    ///     Checks if robocopy.exe is available on this system.
    /// </summary>
    private bool IsRobocopyAvailable()
    {
        try
        {
            // Use the existing method on RobocopyService
            // Cast to IRobocopyService to access the method
            var robocopyServiceInterface = _robocopyService;
            return robocopyServiceInterface?.IsRobocopyAvailable() ?? false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    ///     Builds a human-readable summary of the copy scenario.
    /// </summary>
    private static string BuildScenarioSummary(
        string sourcePath,
        string destPath,
        bool sourceIsNetwork,
        bool destIsNetwork)
    {
        var sourceType = GetPathTypeDescription(sourcePath, sourceIsNetwork);
        var destType = GetPathTypeDescription(destPath, destIsNetwork);

        if (sourceType == destType) return $"{sourceType} to {destType} copy";

        return $"{sourceType} to {destType} copy";
    }

    /// <summary>
    ///     Gets a user-friendly description of a path type.
    /// </summary>
    private static string GetPathTypeDescription(string path, bool isNetwork)
    {
        if (isNetwork) return "Network";

        var driveType = CopyScenarioAnalyzer.GetDriveType(path);
        if (driveType.HasValue)
            return driveType.Value switch
            {
                DriveType.Fixed => "Local",
                DriveType.Removable => "Removable",
                DriveType.Network => "Network",
                DriveType.CDRom => "CD/DVD",
                DriveType.Ram => "RAM Disk",
                _ => "Unknown"
            };

        return "Unknown";
    }

    #endregion
}