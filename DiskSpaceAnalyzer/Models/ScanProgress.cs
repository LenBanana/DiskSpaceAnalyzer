namespace DiskSpaceAnalyzer.Models;

public class ScanProgress
{
    public string CurrentPath { get; set; } = string.Empty;
    public long ProcessedItems { get; set; }
    public long TotalSize { get; set; }
    public int ErrorCount { get; set; }
    public DirectoryItem? CompletedItem { get; set; }
}