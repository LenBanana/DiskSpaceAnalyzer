using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DiskSpaceAnalyzer.Models;

namespace DiskSpaceAnalyzer.Services;

public interface IFileSystemService
{
    Task<ScanResult> ScanDirectoryAsync(string path, ScanMode mode, IProgress<ScanProgress> progress,
        CancellationToken cancellationToken);

    IEnumerable<string> GetDrives();
    bool DirectoryExists(string path);
    DirectoryItem GetDirectoryInfo(string path);
}