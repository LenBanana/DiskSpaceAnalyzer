using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DiskSpaceAnalyzer.Models.FileCopy;

namespace DiskSpaceAnalyzer.Services.FileCopy;

/// <summary>
///     Generic interface for file copy operations, abstracted from specific implementation engines.
///     This interface is designed to support multiple copy engines (Robocopy, Native C#, rsync, etc.)
///     while providing a consistent API for all copy operations.
/// </summary>
/// <remarks>
///     Design principles:
///     - Engine-agnostic: No assumptions about underlying implementation (process vs in-memory)
///     - Capability-based: Optional features exposed through GetCapabilities()
///     - Progress-driven: Real-time updates via IProgress pattern
///     - Cancellable: Full support for CancellationToken
///     - Validatable: Pre-flight validation before expensive operations
/// </remarks>
public interface IFileCopyService
{
    /// <summary>
    ///     Start a file copy operation asynchronously.
    /// </summary>
    /// <param name="options">Copy configuration options.</param>
    /// <param name="progress">Progress reporter for real-time updates. Can be null if progress not needed.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>Final result of the operation including statistics and any errors.</returns>
    /// <exception cref="ArgumentNullException">Thrown if options is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown if operation is already in progress.</exception>
    /// <exception cref="OperationCanceledException">Thrown if operation is cancelled.</exception>
    Task<FileCopyResult> CopyAsync(
        FileCopyOptions options,
        IProgress<FileCopyProgress>? progress,
        CancellationToken cancellationToken);

    /// <summary>
    ///     Pause the current copy operation.
    ///     Only supported if GetCapabilities().SupportsPauseResume is true.
    /// </summary>
    /// <exception cref="NotSupportedException">Thrown if engine doesn't support pause/resume.</exception>
    /// <exception cref="InvalidOperationException">Thrown if no operation is in progress.</exception>
    void Pause();

    /// <summary>
    ///     Resume a paused copy operation.
    ///     Only supported if GetCapabilities().SupportsPauseResume is true.
    /// </summary>
    /// <exception cref="NotSupportedException">Thrown if engine doesn't support pause/resume.</exception>
    /// <exception cref="InvalidOperationException">Thrown if operation is not paused.</exception>
    void Resume();

    /// <summary>
    ///     Cancel the current operation gracefully.
    ///     Prefer using CancellationToken in CopyAsync for most cases.
    ///     This method provides synchronous cancellation for UI scenarios.
    /// </summary>
    void Cancel();

    /// <summary>
    ///     Validate options before starting an operation.
    ///     Checks for common issues like missing paths, invalid configurations, insufficient permissions.
    /// </summary>
    /// <param name="options">Options to validate.</param>
    /// <returns>
    ///     Tuple containing:
    ///     - IsValid: true if options are valid
    ///     - ErrorMessage: null if valid, descriptive error message if invalid
    ///     - Warnings: List of non-fatal warnings (e.g., "destination has limited space")
    /// </returns>
    Task<(bool IsValid, string? ErrorMessage, List<string>? Warnings)> ValidateOptionsAsync(FileCopyOptions options);

    /// <summary>
    ///     Get a human-readable description of what the operation will do.
    ///     For process-based engines (robocopy), this might be a command line.
    ///     For native engines, this might be a summary like "Copy 1,234 files (5.2 GB) using 8 threads".
    ///     Useful for showing users what will happen before starting the operation.
    /// </summary>
    /// <param name="options">Options to describe.</param>
    /// <returns>Human-readable operation description.</returns>
    string GetOperationDescription(FileCopyOptions options);

    /// <summary>
    ///     Get the capabilities of this copy engine.
    ///     Used by UI and logic to determine what features are available.
    /// </summary>
    /// <returns>Capability descriptor for this engine.</returns>
    CopyEngineCapabilities GetCapabilities();

    /// <summary>
    ///     Get the engine type identifier.
    /// </summary>
    /// <returns>The type of this copy engine.</returns>
    CopyEngineType GetEngineType();

    /// <summary>
    ///     Check if this engine is available on the current system.
    ///     For example, robocopy checks if robocopy.exe exists.
    ///     Native engine would always return true.
    /// </summary>
    /// <returns>True if engine can be used on this system.</returns>
    bool IsAvailable();
}