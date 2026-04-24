using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DiskSpaceAnalyzer.Services.FileCopy;

/// <summary>
///     Service for efficient directory scanning with filtering and progress reporting.
///     Shared by all copy engines to ensure consistent behavior and performance.
/// </summary>
public interface IDirectoryScanService
{
    /// <summary>
    ///     Scans a directory tree and returns metadata about files matching the criteria.
    ///     Uses lazy enumeration and reports progress periodically.
    /// </summary>
    /// <param name="options">Scan configuration including filters and exclusions</param>
    /// <param name="progress">Progress callback for UI updates</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Scan results with file list and statistics</returns>
    Task<DirectoryScanResult> ScanAsync(
        DirectoryScanOptions options,
        IProgress<DirectoryScanProgress>? progress,
        CancellationToken cancellationToken);

    /// <summary>
    ///     Quick estimate of directory size without building full file list.
    ///     Useful for disk space checks. Respects exclusion filters.
    /// </summary>
    /// <param name="path">Directory to estimate</param>
    /// <param name="excludeDirectories">Directory patterns to skip</param>
    /// <param name="excludeFiles">File patterns to skip</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Estimated size in bytes</returns>
    Task<long> EstimateSizeAsync(
        string path,
        List<string>? excludeDirectories,
        List<string>? excludeFiles,
        CancellationToken cancellationToken);
}

/// <summary>
///     Configuration options for directory scanning.
/// </summary>
public class DirectoryScanOptions
{
    /// <summary>Source directory to scan</summary>
    public string SourcePath { get; set; } = string.Empty;

    /// <summary>Destination path (for relative path calculations)</summary>
    public string DestinationPath { get; set; } = string.Empty;

    /// <summary>Include subdirectories in scan</summary>
    public bool IncludeSubdirectories { get; set; } = true;

    /// <summary>Directory name patterns to exclude</summary>
    public List<string> ExcludeDirectories { get; set; } = new();

    /// <summary>File name patterns to exclude</summary>
    public List<string> ExcludeFiles { get; set; } = new();

    /// <summary>Minimum file size filter (bytes)</summary>
    public long? MinFileSize { get; set; }

    /// <summary>Maximum file size filter (bytes)</summary>
    public long? MaxFileSize { get; set; }

    /// <summary>Only scan older files (created before this date)</summary>
    public DateTime? OlderThan { get; set; }

    /// <summary>Only scan newer files (created after this date)</summary>
    public DateTime? NewerThan { get; set; }

    /// <summary>Build full file list (true) or just count/size (false)</summary>
    public bool BuildFileList { get; set; } = true;
}

/// <summary>
///     Progress information during directory scanning.
/// </summary>
public class DirectoryScanProgress
{
    /// <summary>Number of files found so far</summary>
    public long FilesFound { get; init; }

    /// <summary>Number of directories found so far</summary>
    public long DirectoriesFound { get; init; }

    /// <summary>Total bytes found so far</summary>
    public long BytesFound { get; init; }

    /// <summary>Current directory being scanned</summary>
    public string CurrentDirectory { get; init; } = string.Empty;

    /// <summary>Estimated completion percentage (0-100)</summary>
    public double PercentComplete { get; init; }
}

/// <summary>
///     Results of a directory scan operation.
/// </summary>
public class DirectoryScanResult
{
    /// <summary>Total files found</summary>
    public long TotalFiles { get; init; }

    /// <summary>Total directories found</summary>
    public long TotalDirectories { get; init; }

    /// <summary>Total bytes found</summary>
    public long TotalBytes { get; init; }

    /// <summary>List of files to process (if BuildFileList was true)</summary>
    public List<ScannedFileInfo> Files { get; init; } = new();

    /// <summary>Errors encountered during scanning</summary>
    public List<ScanError> Errors { get; init; } = new();

    /// <summary>Time taken to scan</summary>
    public TimeSpan Duration { get; init; }
}

/// <summary>
///     Information about a scanned file.
/// </summary>
public class ScannedFileInfo
{
    /// <summary>Full source path</summary>
    public string SourcePath { get; set; } = string.Empty;

    /// <summary>Full destination path</summary>
    public string DestinationPath { get; set; } = string.Empty;

    /// <summary>Relative path from source root</summary>
    public string RelativePath { get; set; } = string.Empty;

    /// <summary>File size in bytes</summary>
    public long Size { get; set; }

    /// <summary>Last write time (UTC)</summary>
    public DateTime LastModified { get; set; }

    /// <summary>File attributes</summary>
    public FileAttributes Attributes { get; set; }
}

/// <summary>
///     Error encountered during scanning.
/// </summary>
public class ScanError
{
    /// <summary>Path that caused the error</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>Error message</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>When the error occurred</summary>
    public DateTime Timestamp { get; set; }
}