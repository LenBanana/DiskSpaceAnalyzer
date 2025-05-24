using System;
using System.Collections.ObjectModel;

namespace DiskSpaceAnalyzer.Models
{
    public class ScanResult
    {
        public DirectoryItem RootDirectory { get; set; } = new();
        public TimeSpan ScanDuration { get; set; }
        public long TotalFiles { get; set; }
        public long TotalDirectories { get; set; }
        public long TotalSize { get; set; }
        public int ErrorCount { get; set; }
        public ObservableCollection<string> Errors { get; set; } = new();
    }
}
