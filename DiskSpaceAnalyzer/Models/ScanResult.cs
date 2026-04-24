using System;
using System.Collections.Generic;

namespace DiskSpaceAnalyzer.Models;

public class ScanResult
{
    public DirectoryItem RootDirectory { get; set; } = new();
    public TimeSpan ScanDuration { get; set; }
    public long TotalFiles { get; set; }
    public long TotalDirectories { get; set; }
    public long TotalSize { get; set; }
    public int ErrorCount { get; set; }
    public List<string> Errors { get; set; } = new();
}