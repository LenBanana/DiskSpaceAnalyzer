using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DiskSpaceAnalyzer.Models.FileCopy;
using DiskSpaceAnalyzer.Models.Robocopy;

namespace DiskSpaceAnalyzer.Services.Robocopy;

/// <summary>
///     Interface for robocopy operations.
///     Designed for testability and future extensibility.
/// </summary>
public interface IRobocopyService
{
    /// <summary>
    ///     Start a robocopy operation asynchronously.
    /// </summary>
    /// <param name="options">Robocopy configuration options.</param>
    /// <param name="progress">Progress reporter for real-time updates.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    /// <returns>Final result of the operation.</returns>
    Task<RobocopyResult> CopyAsync(
        RobocopyOptions options,
        IProgress<RobocopyProgress> progress,
        CancellationToken cancellationToken);

    /// <summary>
    ///     Pause the current robocopy operation by suspending the process.
    /// </summary>
    void Pause();

    /// <summary>
    ///     Resume a paused robocopy operation.
    /// </summary>
    void Resume();

    /// <summary>
    ///     Cancel the current operation.
    /// </summary>
    void Cancel();

    /// <summary>
    ///     Check if robocopy.exe is available on the system.
    /// </summary>
    bool IsRobocopyAvailable();

    /// <summary>
    ///     Get the full path to robocopy.exe.
    /// </summary>
    string GetRobocopyPath();

    /// <summary>
    ///     Validate options before starting (source exists, destination writable, etc.).
    /// </summary>
    (bool IsValid, string? ErrorMessage) ValidateOptions(RobocopyOptions options);

    /// <summary>
    ///     Get the current output lines from stdout.
    /// </summary>
    /// <param name="maxLines">Maximum number of recent lines to return.</param>
    string GetCurrentOutput(int maxLines = 250);

    /// <summary>
    ///     Build the full command line that will be executed for the given options.
    ///     Useful for previewing what command will run.
    /// </summary>
    /// <param name="options">The robocopy options to build command for.</param>
    /// <returns>Full command string including robocopy.exe path and arguments.</returns>
    string BuildCommandLine(RobocopyOptions options);

    /// <summary>
    ///     Get current verification results (including failures) from the integrity service.
    ///     Used to display failures while operation is in progress.
    /// </summary>
    /// <returns>List of all verification results collected so far, or null if not available.</returns>
    List<IntegrityCheckResult>? GetCurrentVerificationResults();
}