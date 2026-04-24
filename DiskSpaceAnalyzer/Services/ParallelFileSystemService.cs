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
    private const int MaxFilesPerDirectory = 10_000;

    // Bound I/O concurrency to avoid saturating the file-system driver queue.
    // Only the directory-listing phase is gated; recursion proceeds freely so
    // every level of the tree is explored concurrently.
    private static readonly int OptimalParallelism = Math.Max(2, Environment.ProcessorCount / 2);

    public bool TrackIndividualFiles { get; set; } = true;

    public async Task<ScanResult> ScanDirectoryAsync(string path, ScanMode mode, IProgress<ScanProgress> progress,
        CancellationToken cancellationToken)
    {
        var startTime = DateTime.UtcNow;
        var result = new ScanResult();

        using var ctx = new ScanContext(OptimalParallelism, TrackIndividualFiles);

        try
        {
            var rootItem = await ScanDirectoryInternalAsync(path, mode, progress, cancellationToken, ctx, 0);

            result.RootDirectory = rootItem;
            result.TotalSize = rootItem.Size;
            result.TotalFiles = ctx.TotalFiles;
            result.TotalDirectories = ctx.TotalDirectories;
        }
        catch (OperationCanceledException)
        {
            throw; // Propagate cancellation to the caller; don't treat it as an error.
        }
        catch (Exception ex)
        {
            ctx.AddError($"Failed to scan {path}: {ex.Message}");
            result.RootDirectory = new DirectoryItem
            {
                Name = Path.GetFileName(path) ?? path,
                FullPath = path,
                Error = ex.Message
            };
        }

        result.ScanDuration = DateTime.UtcNow - startTime;
        while (ctx.Errors.TryDequeue(out var error))
            result.Errors.Add(error);
        result.ErrorCount = result.Errors.Count;

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

    // Static: uses only ScanContext state and constants, no instance fields.
    private static async Task<DirectoryItem> ScanDirectoryInternalAsync(
        string path,
        ScanMode mode,
        IProgress<ScanProgress>? progress,
        CancellationToken cancellationToken,
        ScanContext ctx,
        int depth)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var item = new DirectoryItem
        {
            Name = Path.GetFileName(path) ?? path,
            FullPath = path,
            IsDirectory = true
        };

        var files = new List<FileInfo>();
        var subdirs = new List<DirectoryInfo>();

        // ----------------------------------------------------------------
        // Phase 1 – Enumerate this directory.
        // The semaphore is acquired for the listing I/O only, then released
        // BEFORE any await that recurses into children.  This ensures a
        // parent never holds a slot while waiting for its children, which
        // would deadlock as the tree grows deeper than OptimalParallelism.
        // ----------------------------------------------------------------
        await ctx.Semaphore.WaitAsync(cancellationToken);
        try
        {
            var dirInfo = new DirectoryInfo(path);
            item.LastModified = dirInfo.LastWriteTime;

            var n = ctx.IncrementProcessed();
            if (n % 10 == 0 || depth == 0)
                progress?.Report(new ScanProgress { CurrentPath = path, ErrorCount = ctx.ErrorCount });

            try
            {
                // Single pass: one OS directory-read instead of separate
                // GetFiles() + GetDirectories() (two reads of the same inode).
                foreach (var entry in dirInfo.EnumerateFileSystemInfos())
                    if (entry is FileInfo fi) files.Add(fi);
                    else if (entry is DirectoryInfo di) subdirs.Add(di);
            }
            catch (UnauthorizedAccessException)
            {
                item.Error = "Access denied";
                ctx.AddError($"Access denied to directory: {path}");
            }
            catch (Exception ex)
            {
                item.Error = ex.Message;
                ctx.AddError($"Error scanning directory {path}: {ex.Message}");
            }
        }
        finally
        {
            ctx.Semaphore.Release(); // Must be released before any child tasks are awaited.
        }

        item.FileCount = files.Count;
        item.DirectoryCount = subdirs.Count;
        ctx.IncrementDirectories();
        ctx.AddFiles(files.Count);

        // ----------------------------------------------------------------
        // Phase 2 – Accumulate file sizes.
        // FileInfo.Length is populated by EnumerateFileSystemInfos (Windows
        // returns sizes in the directory listing), so no additional I/O here.
        // ----------------------------------------------------------------
        long totalSize = 0;

        if (ctx.TrackFiles && files.Count > 0 && files.Count <= MaxFilesPerDirectory)
            foreach (var file in files)
                try
                {
                    var fileSize = file.Length;
                    totalSize += fileSize;
                    item.Files.Add(new FileItem
                    {
                        Name = file.Name,
                        FullPath = file.FullName,
                        Size = fileSize,
                        LastModified = file.LastWriteTime,
                        Extension = file.Extension
                    });
                }
                catch (Exception ex)
                {
                    ctx.AddError($"Cannot access file {file.FullName}: {ex.Message}");
                }
        else
            foreach (var file in files)
                try
                {
                    totalSize += file.Length;
                }
                catch (Exception ex)
                {
                    ctx.AddError($"Cannot access file {file.FullName}: {ex.Message}");
                }

        // ----------------------------------------------------------------
        // Phase 3 – Process subdirectories.
        // The semaphore is already released above, so each child task can
        // acquire its own I/O slot independently.  Task.WhenAll enables
        // full-tree concurrency (not just the top two levels), globally
        // bounded by the semaphore.
        // ----------------------------------------------------------------
        if (subdirs.Count > 0)
        {
            if (mode == ScanMode.Recursive)
            {
                var childTasks = subdirs.Select(subDir =>
                    ScanDirectoryInternalAsync(subDir.FullName, mode, progress, cancellationToken, ctx, depth + 1));

                var children = await Task.WhenAll(childTasks);

                foreach (var child in children)
                {
                    child.Parent = item;
                    item.Children.Add(child);
                    totalSize += child.Size;
                }
            }
            else
            {
                // Top-level mode: compute each subdirectory's total size without
                // building the recursive tree structure.
                totalSize += await CalculateTopLevelSizesAsync(item, subdirs, cancellationToken, ctx);
            }
        }

        item.Size = totalSize;

        // Percentages relative to the FULL directory size (files + subdirectories
        // combined), so a file's share is directly comparable to a sibling
        // subdirectory's share.  Previously, file percentages were calculated
        // against the files-only subtotal, making them appear inflated.
        if (totalSize > 0)
        {
            var totalDouble = (double)totalSize;
            foreach (var child in item.Children)
                child.PercentageOfParent = child.Size / totalDouble * 100.0;
            foreach (var file in item.Files)
                file.PercentageOfParent = file.Size / totalDouble * 100.0;
        }

        if (depth == 0)
            progress?.Report(new ScanProgress
            {
                CurrentPath = item.FullPath,
                CompletedItem = item,
                ErrorCount = ctx.ErrorCount
            });

        return item;
    }

    private static async Task<long> CalculateTopLevelSizesAsync(
        DirectoryItem parentItem,
        List<DirectoryInfo> directories,
        CancellationToken cancellationToken,
        ScanContext ctx)
    {
        var results = new ConcurrentBag<(DirectoryItem Item, long Size)>();

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
                    // CalculateDirectorySizeAndCount is CPU/I/O bound; run on the
                    // thread pool to keep the UI thread free.
                    var (size, fileCount) = await Task.Run(
                        () => CalculateDirectorySizeAndCount(subDir.FullName, ct, ctx), ct);

                    var childItem = new DirectoryItem
                    {
                        Name = subDir.Name,
                        FullPath = subDir.FullName,
                        LastModified = subDir.LastWriteTime,
                        IsDirectory = true,
                        Parent = parentItem,
                        FileCount = fileCount,
                        Size = size
                    };

                    ctx.AddFiles(fileCount);
                    ctx.IncrementDirectories();
                    results.Add((childItem, size));
                }
                catch (Exception ex)
                {
                    ctx.AddError($"Error calculating size for {subDir.FullName}: {ex.Message}");
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

    // Replaces the previous separate CalculateDirectorySize + GetFileCount pair,
    // which caused a double traversal of every subdirectory in top-level mode.
    // Now a single iterative walk (stack-based, no recursion depth limit) returns
    // both the total size and the total recursive file count.
    private static (long Size, long FileCount) CalculateDirectorySizeAndCount(
        string path,
        CancellationToken cancellationToken,
        ScanContext ctx)
    {
        long totalSize = 0;
        long fileCount = 0;
        var stack = new Stack<DirectoryInfo>();
        stack.Push(new DirectoryInfo(path));

        while (stack.Count > 0 && !cancellationToken.IsCancellationRequested)
        {
            var currentDir = stack.Pop();

            try
            {
                // Single-pass enumeration: one directory read for both files and subdirs.
                foreach (var entry in currentDir.EnumerateFileSystemInfos())
                    try
                    {
                        if (entry is FileInfo fi)
                        {
                            totalSize += fi.Length;
                            fileCount++;
                        }
                        else if (entry is DirectoryInfo di)
                        {
                            stack.Push(di);
                        }
                    }
                    catch (Exception ex)
                    {
                        ctx.AddError($"Cannot access entry {entry.FullName}: {ex.Message}");
                    }
            }
            catch (UnauthorizedAccessException)
            {
                ctx.AddError($"Access denied to directory: {currentDir.FullName}");
            }
            catch (Exception ex)
            {
                ctx.AddError($"Error accessing directory {currentDir.FullName}: {ex.Message}");
            }
        }

        return (totalSize, fileCount);
    }

    // -------------------------------------------------------------------------
    // Per-scan context: groups all mutable scan state so the service is
    // re-entrant and state never leaks between sequential or concurrent calls.
    // -------------------------------------------------------------------------
    private sealed class ScanContext : IDisposable
    {
        public readonly ConcurrentQueue<string> Errors = new();

        // The semaphore gates directory-listing I/O globally across all
        // recursive levels.  It is released before any child tasks are launched
        // so parents never hold a slot while waiting for children (no deadlock).
        public readonly SemaphoreSlim Semaphore;

        public readonly bool TrackFiles;
        private int _errorCount;

        private long _processedItems;
        private long _totalDirectories;
        private long _totalFiles;

        public ScanContext(int parallelism, bool trackFiles)
        {
            Semaphore = new SemaphoreSlim(parallelism, parallelism);
            TrackFiles = trackFiles;
        }

        public long TotalFiles => Interlocked.Read(ref _totalFiles);
        public long TotalDirectories => Interlocked.Read(ref _totalDirectories);

        // Volatile read is sufficient: used only for approximate progress display.
        public int ErrorCount => Volatile.Read(ref _errorCount);

        public void Dispose()
        {
            Semaphore.Dispose();
        }

        public long IncrementProcessed()
        {
            return Interlocked.Increment(ref _processedItems);
        }

        public void AddFiles(long count)
        {
            Interlocked.Add(ref _totalFiles, count);
        }

        public void IncrementDirectories()
        {
            Interlocked.Increment(ref _totalDirectories);
        }

        public void AddError(string message)
        {
            Errors.Enqueue(message);
            Interlocked.Increment(ref _errorCount);
        }
    }
}