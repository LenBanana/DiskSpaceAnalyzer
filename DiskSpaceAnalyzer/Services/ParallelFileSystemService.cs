using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DiskSpaceAnalyzer.Models;

namespace DiskSpaceAnalyzer.Services;

public class ParallelFileSystemService : IFileSystemService
{
    // Conservative parallelism settings
    private static readonly int OptimalParallelism = Math.Max(2, Environment.ProcessorCount / 2);
    private static readonly int ParallelThreshold = 3; // Only parallelize if >= 3 subdirectories

    // Progress tracking
    private long _processedItems;

    public async Task<ScanResult> ScanDirectoryAsync(string path, ScanMode mode, IProgress<ScanProgress> progress,
        CancellationToken cancellationToken)
    {
        var startTime = DateTime.UtcNow;
        var errors = new ConcurrentBag<string>();
        var result = new ScanResult();

        // Reset progress tracking
        _processedItems = 0;

        try
        {
            var rootItem = await ScanDirectoryInternalAsync(path, mode, progress, cancellationToken, errors, 0);

            result.RootDirectory = rootItem;
            result.TotalSize = rootItem.Size;
            result.TotalFiles = CountFiles(rootItem);
            result.TotalDirectories = CountDirectories(rootItem);
        }
        catch (Exception ex)
        {
            errors.Add($"Failed to scan {path}: {ex.Message}");
            result.RootDirectory = new DirectoryItem
            {
                Name = Path.GetFileName(path) ?? path,
                FullPath = path,
                Error = ex.Message
            };
        }

        result.ScanDuration = DateTime.UtcNow - startTime;
        result.ErrorCount = errors.Count;
        foreach (var error in errors) result.Errors.Add(error);

        return result;
    }

    public IEnumerable<string> GetDrives()
    {
        return DriveInfo.GetDrives()
            .Where(d => d.IsReady)
            .Select(d => d.RootDirectory.FullName);
    }

    public bool DirectoryExists(string path)
    {
        return Directory.Exists(path);
    }

    public DirectoryItem GetDirectoryInfo(string path)
    {
        var info = new DirectoryInfo(path);
        return new DirectoryItem
        {
            Name = info.Name,
            FullPath = info.FullName,
            LastModified = info.LastWriteTime,
            IsDirectory = true,
            FileCount = info.GetFiles().Length
        };
    }

    private async Task<DirectoryItem> ScanDirectoryInternalAsync(
        string path,
        ScanMode mode,
        IProgress<ScanProgress> progress,
        CancellationToken cancellationToken,
        ConcurrentBag<string> errors,
        int depth)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var item = new DirectoryItem
        {
            Name = Path.GetFileName(path) ?? path,
            FullPath = path,
            IsDirectory = true
        };

        try
        {
            var directoryInfo = new DirectoryInfo(path);
            item.LastModified = directoryInfo.LastWriteTime;

            // Report progress
            Interlocked.Increment(ref _processedItems);
            if (_processedItems % 10 == 0 || depth == 0)
                progress?.Report(new ScanProgress
                {
                    CurrentPath = path,
                    ErrorCount = errors.Count
                });

            // Get files and directories separately to avoid collection modification issues
            var files = directoryInfo.GetFiles().ToArray();
            var directories = directoryInfo.GetDirectories().ToArray();

            item.FileCount = files.Length;
            item.DirectoryCount = directories.Length;

            // Calculate file sizes
            long totalSize = 0;
            foreach (var file in files)
                try
                {
                    totalSize += file.Length;
                }
                catch (Exception ex)
                {
                    errors.Add($"Cannot access file {file.FullName}: {ex.Message}");
                }

            if (directories.Length > 0)
            {
                long subdirectoriesSize;

                if (mode == ScanMode.Recursive)
                {
                    // Use parallel processing only for top levels with many directories
                    if (depth <= 1 && directories.Length >= ParallelThreshold)
                        subdirectoriesSize = await ProcessSubdirectoriesParallelAsync(
                            item, directories, mode, progress, cancellationToken, errors, depth);
                    else
                        subdirectoriesSize = await ProcessSubdirectoriesSequentialAsync(
                            item, directories, mode, progress, cancellationToken, errors, depth);
                }
                else
                {
                    // For top-level mode, calculate sizes in parallel
                    subdirectoriesSize = await CalculateTopLevelSizesAsync(
                        item, directories, progress, cancellationToken, errors);
                }

                totalSize += subdirectoriesSize;
            }

            item.Size = totalSize;

            // Calculate percentages
            if (totalSize > 0 && item.Children.Count > 0)
            {
                var totalSizeDouble = (double)totalSize;
                foreach (var child in item.Children) child.PercentageOfParent = child.Size / totalSizeDouble * 100.0;
            }

            // Report completion for root-level items
            if (depth == 0)
                progress?.Report(new ScanProgress
                {
                    CurrentPath = item.FullPath,
                    CompletedItem = item,
                    ErrorCount = errors.Count
                });
        }
        catch (UnauthorizedAccessException)
        {
            item.Error = "Access denied";
            errors.Add($"Access denied to directory: {path}");
        }
        catch (Exception ex)
        {
            item.Error = ex.Message;
            errors.Add($"Error scanning directory {path}: {ex.Message}");
        }

        return item;
    }

    private async Task<long> ProcessSubdirectoriesParallelAsync(
        DirectoryItem parentItem,
        DirectoryInfo[] directories,
        ScanMode mode,
        IProgress<ScanProgress> progress,
        CancellationToken cancellationToken,
        ConcurrentBag<string> errors,
        int depth)
    {
        var results = new ConcurrentBag<(DirectoryItem item, long size)>();

        await Parallel.ForEachAsync(
            directories,
            new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = OptimalParallelism
            },
            async (subDir, ct) =>
            {
                try
                {
                    var childItem = await ScanDirectoryInternalAsync(
                        subDir.FullName, mode, progress, ct, errors, depth + 1);

                    childItem.Parent = parentItem;
                    results.Add((childItem, childItem.Size));

                    // Report progress for top-level directories
                    if (depth == 0)
                        progress?.Report(new ScanProgress
                        {
                            CurrentPath = childItem.FullPath,
                            CompletedItem = childItem,
                            ErrorCount = errors.Count
                        });
                }
                catch (UnauthorizedAccessException)
                {
                    errors.Add($"Access denied to directory: {subDir.FullName}");
                    var errorItem = new DirectoryItem
                    {
                        Name = subDir.Name,
                        FullPath = subDir.FullName,
                        Error = "Access denied",
                        Parent = parentItem
                    };
                    results.Add((errorItem, 0));
                }
                catch (Exception ex)
                {
                    errors.Add($"Error scanning directory {subDir.FullName}: {ex.Message}");
                }
            });

        // Add results to parent and calculate total size
        long totalSize = 0;
        foreach (var (item, size) in results)
        {
            parentItem.Children.Add(item);
            totalSize += size;
        }

        return totalSize;
    }

    private async Task<long> ProcessSubdirectoriesSequentialAsync(
        DirectoryItem parentItem,
        DirectoryInfo[] directories,
        ScanMode mode,
        IProgress<ScanProgress> progress,
        CancellationToken cancellationToken,
        ConcurrentBag<string> errors,
        int depth)
    {
        long totalSize = 0;

        foreach (var subDir in directories)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var childItem = await ScanDirectoryInternalAsync(
                    subDir.FullName, mode, progress, cancellationToken, errors, depth + 1);

                childItem.Parent = parentItem;
                parentItem.Children.Add(childItem);
                totalSize += childItem.Size;

                // Report progress for top-level directories
                if (depth == 0)
                    progress?.Report(new ScanProgress
                    {
                        CurrentPath = childItem.FullPath,
                        CompletedItem = childItem,
                        ErrorCount = errors.Count
                    });
            }
            catch (UnauthorizedAccessException)
            {
                errors.Add($"Access denied to directory: {subDir.FullName}");
                var errorItem = new DirectoryItem
                {
                    Name = subDir.Name,
                    FullPath = subDir.FullName,
                    Error = "Access denied",
                    Parent = parentItem
                };
                parentItem.Children.Add(errorItem);
            }
            catch (Exception ex)
            {
                errors.Add($"Error scanning directory {subDir.FullName}: {ex.Message}");
            }
        }

        return totalSize;
    }

    private async Task<long> CalculateTopLevelSizesAsync(
        DirectoryItem parentItem,
        DirectoryInfo[] directories,
        IProgress<ScanProgress> progress,
        CancellationToken cancellationToken,
        ConcurrentBag<string> errors)
    {
        var results = new ConcurrentBag<(DirectoryItem item, long size)>();

        await Parallel.ForEachAsync(
            directories,
            new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = OptimalParallelism
            },
            async (subDir, ct) =>
            {
                try
                {
                    var size = await Task.Run(() =>
                        CalculateDirectorySize(subDir.FullName, ct, errors), ct);

                    var childItem = new DirectoryItem
                    {
                        Name = subDir.Name,
                        FullPath = subDir.FullName,
                        LastModified = subDir.LastWriteTime,
                        IsDirectory = true,
                        Parent = parentItem,
                        FileCount = GetFileCount(subDir),
                        Size = size
                    };

                    results.Add((childItem, size));
                }
                catch (Exception ex)
                {
                    errors.Add($"Error calculating size for {subDir.FullName}: {ex.Message}");
                }
            });

        long totalSize = 0;
        foreach (var (item, size) in results)
        {
            parentItem.Children.Add(item);
            totalSize += size;
        }

        return totalSize;
    }

    private static long CalculateDirectorySize(
        string path,
        CancellationToken cancellationToken,
        ConcurrentBag<string> errors)
    {
        long totalSize = 0;
        var stack = new Stack<DirectoryInfo>();
        stack.Push(new DirectoryInfo(path));

        while (stack.Count > 0 && !cancellationToken.IsCancellationRequested)
        {
            var currentDir = stack.Pop();

            try
            {
                // Get files and add their sizes
                foreach (var file in currentDir.GetFiles())
                    try
                    {
                        totalSize += file.Length;
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"Cannot access file {file.FullName}: {ex.Message}");
                    }

                // Add subdirectories to stack
                foreach (var subDir in currentDir.GetDirectories()) stack.Push(subDir);
            }
            catch (UnauthorizedAccessException)
            {
                errors.Add($"Access denied to directory: {currentDir.FullName}");
            }
            catch (Exception ex)
            {
                errors.Add($"Error accessing directory {currentDir.FullName}: {ex.Message}");
            }
        }

        return totalSize;
    }

    private static long GetFileCount(DirectoryInfo directory)
    {
        try
        {
            return directory.GetFiles("*", SearchOption.AllDirectories).LongCount();
        }
        catch
        {
            return 0;
        }
    }

    private static long CountFiles(DirectoryItem item)
    {
        var count = item.FileCount;
        foreach (var child in item.Children) count += CountFiles(child);
        return count;
    }

    private static long CountDirectories(DirectoryItem item)
    {
        var count = item.DirectoryCount;
        foreach (var child in item.Children) count += CountDirectories(child);
        return count;
    }
}