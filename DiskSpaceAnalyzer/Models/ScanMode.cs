namespace DiskSpaceAnalyzer.Models;

public enum ScanMode
{
    TopLevel,
    Recursive
}

public enum SortMode
{
    Name,
    Size,
    LastModified,
    FileCount
}

public enum SortDirection
{
    Ascending,
    Descending
}