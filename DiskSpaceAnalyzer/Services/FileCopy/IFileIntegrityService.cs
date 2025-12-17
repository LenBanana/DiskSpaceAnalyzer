using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DiskSpaceAnalyzer.Models.FileCopy;

namespace DiskSpaceAnalyzer.Services.FileCopy;

/// <summary>
/// Service for verifying file integrity after copy operations.
/// Engine-agnostic - can be used by any copy engine (Robocopy, Native, etc.).
/// Supports multiple verification methods from quick metadata checks to cryptographic hashes.
/// </summary>
public interface IFileIntegrityService
{
    /// <summary>
    /// Start the verification process with the specified method.
    /// </summary>
    /// <param name="method">Verification method to use (Metadata, MD5, SHA256, XXHash64, Blake3).</param>
    /// <param name="sourcePath">Root source path for relative path calculation.</param>
    /// <param name="destinationPath">Root destination path for relative path calculation.</param>
    /// <param name="cancellationToken">Cancellation token to stop verification.</param>
    /// <remarks>
    /// This method prepares the service for verification but doesn't start processing.
    /// Files must be queued using QueueFile(), and actual verification happens asynchronously.
    /// </remarks>
    void Start(
        IntegrityCheckMethod method, 
        string sourcePath, 
        string destinationPath, 
        CancellationToken cancellationToken);
    
    /// <summary>
    /// Queue a file for verification.
    /// The file will be verified asynchronously in the background.
    /// </summary>
    /// <param name="fileInfo">Information about the copied file to verify.</param>
    /// <remarks>
    /// Files are verified in parallel up to the configured parallelism degree.
    /// If verification fails due to file locking, automatic retries will be attempted.
    /// </remarks>
    void QueueFile(FileCopyInfo fileInfo);
    
    /// <summary>
    /// Queue multiple files for verification in batch.
    /// More efficient than calling QueueFile() multiple times.
    /// </summary>
    /// <param name="files">Collection of files to verify.</param>
    void QueueFiles(IEnumerable<FileCopyInfo> files);
    
    /// <summary>
    /// Get the current verification progress.
    /// </summary>
    /// <returns>Progress snapshot including files verified, passed, failed, and speed.</returns>
    IntegrityProgress GetProgress();
    
    /// <summary>
    /// Get all verification results (both passed and failed).
    /// </summary>
    /// <returns>List of all completed verifications.</returns>
    List<IntegrityCheckResult> GetResults();
    
    /// <summary>
    /// Get only the failed verification results.
    /// </summary>
    /// <returns>List of files that failed integrity checks.</returns>
    List<IntegrityCheckResult> GetFailedResults();
    
    /// <summary>
    /// Wait for all queued verifications to complete.
    /// This method blocks until the verification queue is empty.
    /// </summary>
    /// <returns>Task that completes when all verifications are done.</returns>
    Task WaitForCompletionAsync();
    
    /// <summary>
    /// Stop verification and clear the queue.
    /// In-progress verifications will be cancelled gracefully.
    /// </summary>
    /// <returns>Task that completes when service has stopped.</returns>
    Task StopAsync();
    
    /// <summary>
    /// Event raised when a file is verified (both success and failure).
    /// Subscribe to this for real-time verification notifications.
    /// </summary>
    event EventHandler<IntegrityCheckResult>? FileVerified;
    
    /// <summary>
    /// Event raised when verification progress changes (every few files or bytes).
    /// Subscribe to this for UI progress updates.
    /// </summary>
    event EventHandler<IntegrityProgress>? ProgressChanged;
    
    /// <summary>
    /// Whether the service is currently active (started and not stopped).
    /// </summary>
    bool IsActive { get; }
    
    /// <summary>
    /// Whether all queued verifications have completed.
    /// </summary>
    bool IsComplete { get; }
}
