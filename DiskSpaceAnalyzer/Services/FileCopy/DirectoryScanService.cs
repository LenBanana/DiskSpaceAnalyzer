using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DiskSpaceAnalyzer.Services.FileCopy;

/// <summary>
///     High-performance directory scanning service using lazy enumeration.
///     Shared by all copy engines for consistent, fast, non-blocking scanning.
///     Key Features:
///     - Lazy enumeration (doesn't load all files into memory)
///     - Progress reporting every N files
///     - Respects exclusion filters
///     - Handles access denied gracefully
///     - Supports cancellation
/// </summary>
public class DirectoryScanService : IDirectoryScanService
{
    private const int ProgressReportInterval = 100; // Report every 100 files

    public async Task<DirectoryScanResult> ScanAsync(
        DirectoryScanOptions options,
        IProgress<DirectoryScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (options == null)
            throw new ArgumentNullException(nameof(options));

        if (string.IsNullOrWhiteSpace(options.SourcePath))
            throw new ArgumentException("SourcePath is required", nameof(options));

        if (!Directory.Exists(options.SourcePath))
            throw new DirectoryNotFoundException($"Source directory not found: {options.SourcePath}");

        var stopwatch = Stopwatch.StartNew();
        var result = new DirectoryScanResult();
        var files = new List<ScannedFileInfo>();
        var errors = new List<ScanError>();
        long totalFiles = 0;
        long totalDirectories = 0;
        long totalBytes = 0;

        // Run scan on thread pool to avoid blocking
        await Task.Run(() =>
        {
            ScanDirectory(
                options.SourcePath,
                options.DestinationPath,
                options.SourcePath,
                "",
                options,
                files,
                errors,
                ref totalFiles,
                ref totalDirectories,
                ref totalBytes,
                progress,
                cancellationToken);
        }, cancellationToken);

        stopwatch.Stop();

        return new DirectoryScanResult
        {
            TotalFiles = totalFiles,
            TotalDirectories = totalDirectories,
            TotalBytes = totalBytes,
            Files = options.BuildFileList ? files : new List<ScannedFileInfo>(),
            Errors = errors,
            Duration = stopwatch.Elapsed
        };
    }

    public async Task<long> EstimateSizeAsync(
        string path,
        List<string>? excludeDirectories,
        List<string>? excludeFiles,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path))
            return 0;

        if (!Directory.Exists(path))
            return 0;

        long totalSize = 0;

        await Task.Run(() =>
        {
            EstimateSizeRecursive(
                path,
                path,
                excludeDirectories ?? new List<string>(),
                excludeFiles ?? new List<string>(),
                ref totalSize,
                cancellationToken);
        }, cancellationToken);

        return totalSize;
    }

    #region Private Scanning Methods

    /// <summary>
    ///     Recursively scan a directory using lazy enumeration.
    ///     This method is designed to be fast and non-blocking.
    /// </summary>
    private void ScanDirectory(
        string sourceRoot,
        string destRoot,
        string sourcePath,
        string relativePath,
        DirectoryScanOptions options,
        List<ScannedFileInfo> files,
        List<ScanError> errors,
        ref long totalFiles,
        ref long totalDirectories,
        ref long totalBytes,
        IProgress<DirectoryScanProgress>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var currentSourcePath = string.IsNullOrEmpty(relativePath)
            ? sourcePath
            : Path.Combine(sourcePath, relativePath);

        try
        {
            var dirInfo = new DirectoryInfo(currentSourcePath);

            // Scan files in current directory
            foreach (var file in dirInfo.EnumerateFiles())
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Check file exclusions
                    if (ShouldExcludeFile(file.Name, file.FullName, sourceRoot, options.ExcludeFiles))
                        continue;

                    // Apply size filters
                    if (options.MinFileSize.HasValue && file.Length < options.MinFileSize.Value)
                        continue;

                    if (options.MaxFileSize.HasValue && file.Length > options.MaxFileSize.Value)
                        continue;

                    // Apply date filters
                    if (options.OlderThan.HasValue && file.CreationTimeUtc > options.OlderThan.Value)
                        continue;

                    if (options.NewerThan.HasValue && file.CreationTimeUtc < options.NewerThan.Value)
                        continue;

                    // File passed all filters - count it
                    totalFiles++;
                    totalBytes += file.Length;

                    // Add to file list if requested
                    if (options.BuildFileList)
                    {
                        var fileRelativePath = string.IsNullOrEmpty(relativePath)
                            ? file.Name
                            : Path.Combine(relativePath, file.Name);

                        var destPath = string.IsNullOrEmpty(destRoot)
                            ? string.Empty
                            : Path.Combine(destRoot, fileRelativePath);

                        files.Add(new ScannedFileInfo
                        {
                            SourcePath = file.FullName,
                            DestinationPath = destPath,
                            RelativePath = fileRelativePath,
                            Size = file.Length,
                            LastModified = file.LastWriteTimeUtc,
                            Attributes = file.Attributes
                        });
                    }

                    // Report progress periodically
                    if (totalFiles % ProgressReportInterval == 0)
                        progress?.Report(new DirectoryScanProgress
                        {
                            FilesFound = totalFiles,
                            DirectoriesFound = totalDirectories,
                            BytesFound = totalBytes,
                            CurrentDirectory = currentSourcePath,
                            PercentComplete = 0 // Unknown total
                        });
                }
                catch (UnauthorizedAccessException)
                {
                    errors.Add(new ScanError
                    {
                        Path = file.FullName,
                        Message = "Access denied",
                        Timestamp = DateTime.Now
                    });
                }
                catch (Exception ex)
                {
                    errors.Add(new ScanError
                    {
                        Path = file.FullName,
                        Message = ex.Message,
                        Timestamp = DateTime.Now
                    });
                }

            // Scan subdirectories if requested
            if (options.IncludeSubdirectories)
                foreach (var subDir in dirInfo.EnumerateDirectories())
                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        // Check directory exclusions
                        if (ShouldExcludeDirectory(subDir.Name, subDir.FullName, sourceRoot,
                                options.ExcludeDirectories))
                            continue;

                        totalDirectories++;

                        // Recurse into subdirectory
                        var newRelativePath = string.IsNullOrEmpty(relativePath)
                            ? subDir.Name
                            : Path.Combine(relativePath, subDir.Name);

                        ScanDirectory(
                            sourceRoot,
                            destRoot,
                            sourcePath,
                            newRelativePath,
                            options,
                            files,
                            errors,
                            ref totalFiles,
                            ref totalDirectories,
                            ref totalBytes,
                            progress,
                            cancellationToken);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        errors.Add(new ScanError
                        {
                            Path = subDir.FullName,
                            Message = "Access denied",
                            Timestamp = DateTime.Now
                        });
                    }
                    catch (Exception ex)
                    {
                        errors.Add(new ScanError
                        {
                            Path = subDir.FullName,
                            Message = ex.Message,
                            Timestamp = DateTime.Now
                        });
                    }
        }
        catch (UnauthorizedAccessException)
        {
            errors.Add(new ScanError
            {
                Path = currentSourcePath,
                Message = "Access denied",
                Timestamp = DateTime.Now
            });
        }
        catch (Exception ex)
        {
            errors.Add(new ScanError
            {
                Path = currentSourcePath,
                Message = ex.Message,
                Timestamp = DateTime.Now
            });
        }
    }

    /// <summary>
    ///     Quick recursive size estimation without building file list.
    /// </summary>
    private void EstimateSizeRecursive(
        string sourceRoot,
        string currentPath,
        List<string> excludeDirectories,
        List<string> excludeFiles,
        ref long totalSize,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var dirInfo = new DirectoryInfo(currentPath);

            // Add file sizes
            foreach (var file in dirInfo.EnumerateFiles())
                try
                {
                    if (!ShouldExcludeFile(file.Name, file.FullName, sourceRoot, excludeFiles))
                        totalSize += file.Length;
                }
                catch
                {
                    // Ignore individual file errors
                }

            // Recurse into subdirectories
            foreach (var subDir in dirInfo.EnumerateDirectories())
                try
                {
                    if (!ShouldExcludeDirectory(subDir.Name, subDir.FullName, sourceRoot, excludeDirectories))
                        EstimateSizeRecursive(
                            sourceRoot,
                            subDir.FullName,
                            excludeDirectories,
                            excludeFiles,
                            ref totalSize,
                            cancellationToken);
                }
                catch
                {
                    // Ignore subdirectory errors
                }
        }
        catch
        {
            // Ignore directory errors
        }
    }

    #endregion

    #region Filtering Logic

    /// <summary>
    ///     Check if a directory should be excluded based on patterns.
    ///     Supports name matching, wildcards, and path-based exclusions.
    /// </summary>
    private bool ShouldExcludeDirectory(
        string directoryName,
        string fullPath,
        string sourceRoot,
        List<string> exclusions)
    {
        if (exclusions == null || exclusions.Count == 0)
            return false;

        foreach (var pattern in exclusions)
            // Case 1: Simple name match (e.g., "node_modules")
            if (!pattern.Contains("\\") && !pattern.Contains("*") && !pattern.Contains("?"))
            {
                if (directoryName.Equals(pattern, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            // Case 2: Wildcard pattern (e.g., "*cache", "temp*")
            else if (!pattern.Contains("\\") && (pattern.Contains("*") || pattern.Contains("?")))
            {
                if (MatchesWildcard(directoryName, pattern))
                    return true;
            }
            // Case 3: Path-based exclusion (e.g., "src\\bin")
            else if (pattern.Contains("\\"))
            {
                var relativePath = GetRelativePath(sourceRoot, fullPath);

                if (relativePath.Equals(pattern, StringComparison.OrdinalIgnoreCase))
                    return true;

                if (pattern.Contains("*") || pattern.Contains("?"))
                    if (MatchesWildcard(relativePath, pattern))
                        return true;
            }

        return false;
    }

    /// <summary>
    ///     Check if a file should be excluded based on patterns.
    ///     Supports name matching, wildcards, and path-based exclusions.
    /// </summary>
    private bool ShouldExcludeFile(
        string fileName,
        string fullPath,
        string sourceRoot,
        List<string> exclusions)
    {
        if (exclusions == null || exclusions.Count == 0)
            return false;

        foreach (var pattern in exclusions)
            // Case 1: Simple name or wildcard (e.g., "*.log", "Thumbs.db")
            if (!pattern.Contains("\\"))
            {
                if (MatchesWildcard(fileName, pattern))
                    return true;
            }
            // Case 2: Path-based exclusion (e.g., "logs\\*.txt")
            else
            {
                var relativePath = GetRelativePath(sourceRoot, fullPath);

                if (relativePath.Equals(pattern, StringComparison.OrdinalIgnoreCase))
                    return true;

                if (pattern.Contains("*") || pattern.Contains("?"))
                    if (MatchesWildcard(relativePath, pattern))
                        return true;
            }

        return false;
    }

    /// <summary>
    ///     Simple wildcard matching supporting * (any characters) and ? (single character).
    /// </summary>
    private bool MatchesWildcard(string text, string pattern)
    {
        if (string.IsNullOrEmpty(pattern))
            return string.IsNullOrEmpty(text);

        // Convert wildcard pattern to regex-like matching
        var textIndex = 0;
        var patternIndex = 0;
        var lastWildcardIndex = -1;
        var lastTextIndex = -1;

        while (textIndex < text.Length)
            if (patternIndex < pattern.Length &&
                (pattern[patternIndex] == '?' ||
                 char.ToLowerInvariant(pattern[patternIndex]) == char.ToLowerInvariant(text[textIndex])))
            {
                // Match single character
                textIndex++;
                patternIndex++;
            }
            else if (patternIndex < pattern.Length && pattern[patternIndex] == '*')
            {
                // Remember wildcard position
                lastWildcardIndex = patternIndex;
                lastTextIndex = textIndex;
                patternIndex++;
            }
            else if (lastWildcardIndex != -1)
            {
                // Backtrack to last wildcard
                patternIndex = lastWildcardIndex + 1;
                lastTextIndex++;
                textIndex = lastTextIndex;
            }
            else
            {
                return false;
            }

        // Check remaining pattern characters (should be only *)
        while (patternIndex < pattern.Length && pattern[patternIndex] == '*') patternIndex++;

        return patternIndex == pattern.Length;
    }

    /// <summary>
    ///     Get relative path from base to target.
    /// </summary>
    private string GetRelativePath(string basePath, string targetPath)
    {
        if (string.IsNullOrEmpty(basePath) || string.IsNullOrEmpty(targetPath))
            return targetPath;

        var baseUri = new Uri(AppendDirectorySeparator(basePath));
        var targetUri = new Uri(targetPath);

        if (baseUri.Scheme != targetUri.Scheme)
            return targetPath;

        var relativeUri = baseUri.MakeRelativeUri(targetUri);
        var relativePath = Uri.UnescapeDataString(relativeUri.ToString());

        return relativePath.Replace('/', Path.DirectorySeparatorChar);
    }

    private string AppendDirectorySeparator(string path)
    {
        if (!path.EndsWith(Path.DirectorySeparatorChar.ToString()))
            return path + Path.DirectorySeparatorChar;
        return path;
    }

    #endregion
}