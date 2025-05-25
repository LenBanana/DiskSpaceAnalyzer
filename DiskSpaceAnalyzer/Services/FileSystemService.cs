using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DiskSpaceAnalyzer.Models;

namespace DiskSpaceAnalyzer.Services
{
    public class FileSystemService : IFileSystemService
    {
        public async Task<ScanResult> ScanDirectoryAsync(string path, ScanMode mode, IProgress<ScanProgress> progress, CancellationToken cancellationToken)
        {
            var startTime = DateTime.Now;
            var result = new ScanResult();
            var errors = new List<string>();

            try
            {
                var rootItem = await ScanDirectoryInternalAsync(path, mode, progress, cancellationToken, errors);
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
                    Name = Path.GetFileName(path),
                    FullPath = path,
                    Error = ex.Message
                };
            }

            result.ScanDuration = DateTime.Now - startTime;
            result.ErrorCount = errors.Count;
            foreach (var error in errors)
            {
                result.Errors.Add(error);
            }

            return result;
        }

        private async Task<DirectoryItem> ScanDirectoryInternalAsync(string path, ScanMode mode, IProgress<ScanProgress> progress, CancellationToken cancellationToken, List<string> errors)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var directoryInfo = new DirectoryInfo(path);
            var item = GetDirectoryInfo(path);

            progress.Report(new ScanProgress 
            { 
                CurrentPath = path, 
                ProcessedItems = 0, 
                ErrorCount = errors.Count 
            });

            try
            {
                var subdirectories = directoryInfo.GetDirectories();
                var files = directoryInfo.GetFiles();

                item.FileCount = files.Length;
                item.DirectoryCount = subdirectories.Length;

                // Calculate size of direct files
                long totalSize = 0;
                foreach (var file in files)
                {
                    try
                    {
                        totalSize += file.Length;
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"Cannot access file {file.FullName}: {ex.Message}");
                    }
                }

                // Process subdirectories
                foreach (var subDir in subdirectories)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Report progress for each subdirectory
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
                            childItem = await ScanDirectoryInternalAsync(subDir.FullName, mode, progress, cancellationToken, errors);
                        }
                        else
                        {
                            // For top-level mode, calculate size but don't recurse into structure
                            childItem = new DirectoryItem
                            {
                                Name = subDir.Name,
                                FullPath = subDir.FullName,
                                LastModified = subDir.LastWriteTime,
                                IsDirectory = true,
                                Parent = item,
                                FileCount = subDir.GetFiles().Length,
                                // Calculate total size including subdirectories
                                Size = await CalculateDirectorySizeAsync(subDir.FullName, progress, cancellationToken, errors)
                            };
                        }

                        childItem.Parent = item;
                        item.Children.Add(childItem);
                        totalSize += childItem.Size;

                        if (item.Parent == null)          // item == Root
                        {
                            progress.Report(new ScanProgress
                            {
                                CurrentPath    = childItem.FullPath,
                                CompletedItem  = childItem,
                                ErrorCount     = errors.Count
                            });
                        }
                    }
                    catch (UnauthorizedAccessException)
                    {
                        errors.Add($"Access denied to directory: {subDir.FullName}");
                        var errorItem = new DirectoryItem
                        {
                            Name = subDir.Name,
                            FullPath = subDir.FullName,
                            Error = "Access denied",
                            Parent = item
                        };
                        item.Children.Add(errorItem);
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"Error scanning directory {subDir.FullName}: {ex.Message}");
                    }
                }

                item.Size = totalSize;

                // Calculate percentages
                if (totalSize > 0)
                {
                    foreach (var child in item.Children)
                    {
                        child.PercentageOfParent = (double)child.Size / totalSize * 100;
                    }
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

            return item;
        }

        private async Task<long> CalculateDirectorySizeAsync(string path, IProgress<ScanProgress> progress, CancellationToken cancellationToken, List<string> errors)
        {
            return await Task.Run(() =>
            {
                try
                {
                    return CalculateDirectorySize(path, progress, cancellationToken, errors);
                }
                catch
                {
                    return 0;
                }
            }, cancellationToken);
        }

        private long CalculateDirectorySize(string path, IProgress<ScanProgress> progress, CancellationToken cancellationToken, List<string> errors)
        {
            long size = 0;

            try
            {
                progress.Report(new ScanProgress
                {
                    CurrentPath = path,
                    ErrorCount = errors.Count
                });
                var directory = new DirectoryInfo(path);

                // Add file sizes
                foreach (var file in directory.GetFiles())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        size += file.Length;
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"Cannot access file {file.FullName}: {ex.Message}");
                    }
                }

                // Add subdirectory sizes
                foreach (var subDir in directory.GetDirectories())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        size += CalculateDirectorySize(subDir.FullName, progress, cancellationToken, errors);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        errors.Add($"Access denied to directory: {subDir.FullName}");
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"Error calculating size for {subDir.FullName}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                errors.Add($"Error accessing directory {path}: {ex.Message}");
            }

            return size;
        }

        private static long CountFiles(DirectoryItem item)
        {
            return item.FileCount + item.Children.Sum(CountFiles);
        }

        private static long CountDirectories(DirectoryItem item)
        {
            return item.DirectoryCount + item.Children.Sum(CountDirectories);
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
                FileCount = info.GetFiles().Length,
            };
        }
    }
}