using System;
using System.Collections.ObjectModel;

namespace DiskSpaceAnalyzer.Models
{
    public class DirectoryItem
    {
        public string Name { get; set; } = string.Empty;
        public string FullPath { get; set; } = string.Empty;
        public long Size { get; set; }
        public long FileCount { get; set; }
        public long DirectoryCount { get; set; }
        public DateTime LastModified { get; set; }
        public bool IsDirectory { get; set; } = true;
        public ObservableCollection<DirectoryItem> Children { get; set; } = new();
        public DirectoryItem? Parent { get; set; }
        public bool IsExpanded { get; set; }
        public bool IsSelected { get; set; }
        public double PercentageOfParent { get; set; }
        public string Error { get; set; } = string.Empty;
        public bool HasError => !string.IsNullOrEmpty(Error);
    }
}
