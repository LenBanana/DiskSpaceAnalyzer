using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DiskSpaceAnalyzer.Models;

namespace DiskSpaceAnalyzer.Services;

public class FileSystemService : IFileSystemService
{
    private const int MaxFilesPerDirectory = 10_000;

    public bool TrackIndividualFiles { get; set; } = true;

    // Sequential scan; these fields are only ever written from one async
    // continuation at a time, so no Interlocked is required.
    private long _totalFiles;
    private long _totalDirectories;

    public async Task<ScanResult> ScanDirectoryAsync(string path, ScanMode mode, IProgress<ScanProgress> progress,
        CancellationToken cancellationToken)
    {
        var startTime = DateTime.UtcNow;
        var result = new ScanResult();
        var errors = new List<string>();

        _totalFiles = 0;
        _totalDirectories = 0;

        try
        {
            var rootItem = await ScanDirectoryInternalAsync(path, mode, progress, cancellationToken, errors);
            result.RootDirectory = rootItem;
            result.TotalSize = rootItem.Size;
            result.TotalFiles = _totalFiles;
            result.TotalDirectories = _totalDirectories;
        }
        catch (OperationCanceledException)
        {
            throw; // Propagate cancellation to the caller; don't treat it as an error.
        }
        catch (Exception ex)
        {
            errors.Add($"Failed to scan {path}: {ex.Message}");
            result.RootDirectory = new DirectoryItem
            {
                Name = Path.GetFileName(path),
                FullPath = path,
                Error = ex.Message
            };
        }

        result.ScanDuration = DateTime.UtcNow - startTime;
        result.ErrorCount = errors.Count;
        result.Errors.AddRange(errors);

        return result;
    }

    public IEnumerable<string> GetDrives() =>
        DriveInfo.GetDrives()
            .Where(d => d.IsReady)
            .Select(d => d.RootDirectory.FullName);

    public bool DirectoryExists(string path) => Directory.Exists(path);

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

    private async Task<DirectoryItem> ScanDirectoryInternalAsync(string path, ScanMode mode,
        IProgress<ScanProgress> progress, CancellationToken cancellationToken, List<string> errors)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var item = new DirectoryItem
        {
            Name = Path.GetFileName(path) ?? path,
            FullPath = path,
            IsDirectory = true
        };

        progress.Report(new ScanProgress { CurrentPath = path, ErrorCount = errors.Count });

        try
        {
            var dirInfo = new DirectoryInfo(path);
            item.LastModified = dirInfo.LastWriteTime;

            var files = new List<FileInfo>();
            var subdirs = new List<DirectoryInfo>();

            // Single pass: one OS directory-read instead of separate
            // GetFiles() + GetDirectories() (two reads of the same inode).
            try
            {
                foreach (var entry in dirInfo.EnumerateFileSystemInfos())
                {
                    if (entry is FileInfo fi) files.Add(fi);
                    else if (entry is DirectoryInfo di) subdirs.Add(di);
                }
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

            item.FileCount = files.Count;
            item.DirectoryCount = subdirs.Count;
            _totalDirectories++;
            _totalFiles += files.Count;

            long totalSize = 0;

            if (TrackIndividualFiles && files.Count > 0 && files.Count <= MaxFilesPerDirectory)
            {
                foreach (var file in files)
                {
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
                        errors.Add($"Cannot access file {file.FullName}: {ex.Message}");
                    }
                }
            }
            else
            {
                foreach (var file in files)
                {
                    try { totalSize += file.Length; }
                    catch (Exception ex) { errors.Add($"Cannot access file {file.FullName}: {ex.Message}"); }
                }
            }

            foreach (var subDir in subdirs)
            {
                cancellationToken.ThrowIfCancellationRequested();

                progress.Report(new ScanProgress
                {
                    CurrentPath = subDir.FullName,
                    ProcessedItems = item.Children.Count,
                    ErrorCount = errors.Count
                });

                try
                {
                    DirectoryItem childItem;

                    if (mode == ScanMode.Recursive)
                    {
                        childItem = await ScanDirectoryInternalAsync(
                            subDir.FullName, mode, progress, cancellationToken, errors);
                    }
                    else
                    {
                        // Top-level mode: one combined walk for size + file count,
                        // replacing the previous separate CalculateDirectorySize +
                        // subDir.GetFiles().Length (two redundant traversals).
                        var (size, fileCount) = await Task.Run(
                            () => CalculateDirectorySizeAndCount(subDir.FullName, cancellationToken, errors),
                            cancellationToken);

                        _totalDirectories++;
                        _totalFiles += fileCount;

                        childItem = new DirectoryItem
                        {
                            Name = subDir.Name,
                            FullPath = subDir.FullName,
                            LastModified = subDir.LastWriteTime,
                            IsDirectory = true,
                            Parent = item,
                            FileCount = fileCount,
                            Size = size
                        };
                    }

                    childItem.Parent = item;
                    item.Children.Add(childItem);
                    totalSize += childItem.Size;

                    if (item.Parent == null) // root level
                        progress.Report(new ScanProgress
                        {
                            CurrentPath = childItem.FullPath,
                            CompletedItem = childItem,
                            ErrorCount = errors.Count
                        });
                }
                catch (UnauthorizedAccessException)
                {
                    errors.Add($"Access denied to directory: {subDir.FullName}");
                    item.Children.Add(new DirectoryItem
                    {
                        Name = subDir.Name,
                        FullPath = subDir.FullName,
                        Error = "Access denied",
                        Parent = item
                    });
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    errors.Add($"Error scanning directory {subDir.FullName}: {ex.Message}");
                }
            }

            item.Size = totalSize;

            // Percentages relative to the FULL directory size (files + subdirectories
            // combined), consistent with how ParallelFileSystemService calculates them.
            if (totalSize > 0)
            {
                var totalDouble = (double)totalSize;
                foreach (var child in item.Children)
                    child.PercentageOfParent = child.Size / totalDouble * 100.0;
                foreach (var file in item.Files)
                    file.PercentageOfParent = file.Size / totalDouble * 100.0;
            }
        }
        catch (UnauthorizedAccessException)
        {
            item.Error = "Access denied";
            errors.Add($"Access denied to directory: {path}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            item.Error = ex.Message;
            errors.Add($"Error scanning directory {path}: {ex.Message}");
        }

        return item;
    }

    // Iterative (stack-based) walk that returns total size and total file count in
    // a single pass.  Replaces the recursive CalculateDirectorySize (which had no
    // stack-depth limit) and the separate subDir.GetFiles(AllDirectories) call.
    private static (long Size, long FileCount) CalculateDirectorySizeAndCount(
        string path,
        CancellationToken cancellationToken,
        List<string> errors)
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
                {
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
                        errors.Add($"Cannot access entry {entry.FullName}: {ex.Message}");
                    }
                }
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

        return (totalSize, fileCount);
    }
}