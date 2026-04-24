using System.Collections.Generic;
using DiskSpaceAnalyzer.Models.FileCopy;

namespace DiskSpaceAnalyzer.Services.FileCopy;

/// <summary>
///     Represents a copy engine recommendation with reasoning and alternatives.
///     Returned by factory's recommendation method to help users understand
///     why a particular engine was selected.
/// </summary>
public class EngineRecommendation
{
    /// <summary>
    ///     The recommended copy engine for this scenario.
    /// </summary>
    public CopyEngineType RecommendedEngine { get; init; }

    /// <summary>
    ///     Human-readable explanation of why this engine was recommended.
    ///     Example: "Robocopy recommended for network path reliability and automatic retry logic."
    /// </summary>
    public string Reasoning { get; init; } = string.Empty;

    /// <summary>
    ///     Confidence level in the recommendation (0.0 to 1.0).
    ///     1.0 = Strongly recommended (e.g., backup mode requires robocopy)
    ///     0.8 = Recommended (e.g., network path benefits from robocopy)
    ///     0.5 = Neutral (e.g., both engines would work equally well)
    /// </summary>
    public double Confidence { get; init; }

    /// <summary>
    ///     Alternative engines that could also work for this scenario,
    ///     with brief notes on trade-offs.
    ///     Example: { Native: "Would be 2-3x faster but lacks network retry logic" }
    /// </summary>
    public Dictionary<CopyEngineType, string> Alternatives { get; init; } = new();

    /// <summary>
    ///     Any warnings about the recommended engine for this scenario.
    ///     Example: "Native engine does not support backup mode for locked files."
    /// </summary>
    public List<string> Warnings { get; init; } = new();

    /// <summary>
    ///     Performance expectation for the recommended engine.
    ///     Example: "Fast", "Very Fast", "Medium", "Network-Limited"
    /// </summary>
    public string ExpectedPerformance { get; init; } = "Good";

    /// <summary>
    ///     Brief summary of the scenario that was analyzed.
    ///     Example: "Local SSD to SSD copy"
    /// </summary>
    public string ScenarioSummary { get; init; } = string.Empty;
}