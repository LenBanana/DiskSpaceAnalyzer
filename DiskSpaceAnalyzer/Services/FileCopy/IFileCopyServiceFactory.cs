using DiskSpaceAnalyzer.Models.FileCopy;

namespace DiskSpaceAnalyzer.Services.FileCopy;

/// <summary>
///     Factory interface for creating file copy service instances.
///     Handles engine selection logic and instantiation.
/// </summary>
/// <remarks>
///     Implementation will be created in Session 4.
///     This interface placeholder is here to establish the pattern for future use.
/// </remarks>
public interface IFileCopyServiceFactory
{
    /// <summary>
    ///     Create a file copy service instance based on provided options.
    ///     If options.PreferredEngine is Auto, the factory will analyze the scenario
    ///     and select the optimal engine automatically.
    /// </summary>
    /// <param name="options">Copy options that may influence engine selection.</param>
    /// <returns>
    ///     An IFileCopyService implementation suitable for the scenario.
    ///     Returns null if no suitable engine is available.
    /// </returns>
    IFileCopyService? CreateService(FileCopyOptions options);

    /// <summary>
    ///     Create a specific copy engine by type.
    /// </summary>
    /// <param name="engineType">The type of engine to create.</param>
    /// <returns>
    ///     The requested engine implementation.
    ///     Returns null if the engine is not available on this system.
    /// </returns>
    IFileCopyService? CreateService(CopyEngineType engineType);

    /// <summary>
    ///     Get all available copy engines on this system.
    /// </summary>
    /// <returns>List of engine types that are currently available.</returns>
    CopyEngineType[] GetAvailableEngines();

    /// <summary>
    ///     Recommend the best engine for a given scenario.
    ///     Analyzes paths, file counts, sizes, and returns a detailed recommendation
    ///     with reasoning, confidence level, and alternatives.
    /// </summary>
    /// <param name="options">Copy options to analyze.</param>
    /// <returns>
    ///     EngineRecommendation containing:
    ///     - RecommendedEngine: The optimal engine type for this scenario
    ///     - Reasoning: Human-readable explanation of why this engine was chosen
    ///     - Confidence: How confident the recommendation is (0.0 to 1.0)
    ///     - Alternatives: Other engines that could work with trade-offs
    ///     - Warnings: Any concerns about the recommended engine
    /// </returns>
    EngineRecommendation RecommendEngine(FileCopyOptions options);
}