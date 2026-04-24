using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskSpaceAnalyzer.Models;

namespace DiskSpaceAnalyzer.ViewModels;

public partial class DirectoryItemViewModel : BaseViewModel
{
    [ObservableProperty] private DirectoryItem _directoryItem;

    [ObservableProperty] private bool _isExpanded;

    [ObservableProperty] private bool _isSelected;

    [ObservableProperty] private bool _isFilesExpanded;

    [ObservableProperty] private bool _showAllFiles;

    [ObservableProperty] private string _currentSortMode = "Size";

    // Cached computed values
    private long? _cachedTotalFilesSize;
    private long? _cachedSubdirectoriesSize;

    public DirectoryItemViewModel(DirectoryItem directoryItem, DirectoryItemViewModel? parent = null)
    {
        _directoryItem = directoryItem;
        Parent = parent;
        Children = [];
        Files = [];
        TopFiles = [];
        DisplayedFiles = [];

        LoadChildren();
        LoadFiles();
    }

    public ObservableCollection<DirectoryItemViewModel> Children { get; }
    public ObservableCollection<FileItemViewModel> Files { get; }
    public ObservableCollection<FileItemViewModel> TopFiles { get; }
    public ObservableCollection<FileItemViewModel> DisplayedFiles { get; }
    public DirectoryItemViewModel? Parent { get; }

    public string DisplayName => DirectoryItem.Name;
    public string FullPath => DirectoryItem.FullPath;
    public long Size => DirectoryItem.Size;
    public string FormattedSize => FormatBytes(DirectoryItem.Size);
    public long FileCount => DirectoryItem.FileCount;
    public long DirectoryCount => DirectoryItem.DirectoryCount;
    public double PercentageOfParent => DirectoryItem.PercentageOfParent;
    public bool HasError => DirectoryItem.HasError;
    public string Error => DirectoryItem.Error;
    public bool HasFiles => DirectoryItem.Files.Count > 0;
    public bool FilesNotTracked => DirectoryItem.FileCount > 0 && DirectoryItem.Files.Count == 0;
    public int TotalFileCount => DirectoryItem.Files.Count;
    public bool HasMoreFiles => TotalFileCount > 10;
    
    public string FileCountText
    {
        get
        {
            if (TotalFileCount == 0) return "No files";
            if (ShowAllFiles) return $"All {TotalFileCount:N0} files";
            return $"Top {Math.Min(10, TotalFileCount)} of {TotalFileCount:N0} files";
        }
    }
    
    public long TotalFilesSize
    {
        get
        {
            if (_cachedTotalFilesSize == null)
            {
                _cachedTotalFilesSize = DirectoryItem.Files.Sum(f => f.Size);
            }
            return _cachedTotalFilesSize.Value;
        }
    }
    
    public string FormattedFilesSize => FormatBytes(TotalFilesSize);
    
    public long SubdirectoriesSize
    {
        get
        {
            if (_cachedSubdirectoriesSize == null)
            {
                _cachedSubdirectoriesSize = Size - TotalFilesSize;
            }
            return _cachedSubdirectoriesSize.Value;
        }
    }
    
    public string FormattedSubdirectoriesSize => FormatBytes(SubdirectoriesSize);

    private void LoadChildren()
    {
        Children.Clear();
        foreach (var child in DirectoryItem.Children) Children.Add(new DirectoryItemViewModel(child, this));
    }

    private void LoadFiles()
    {
        Files.Clear();
        TopFiles.Clear();
        DisplayedFiles.Clear();
        
        // Invalidate cached values
        _cachedTotalFilesSize = null;
        _cachedSubdirectoriesSize = null;
        
        foreach (var file in DirectoryItem.Files)
        {
            Files.Add(new FileItemViewModel(file, this));
        }

        // Load top 10 files by size for quick display
        var topFiles = DirectoryItem.Files
            .OrderByDescending(f => f.Size)
            .Take(10)
            .ToList();

        foreach (var file in topFiles)
        {
            var fileVm = new FileItemViewModel(file, this);
            TopFiles.Add(fileVm);
            DisplayedFiles.Add(fileVm);
        }
    }

    [RelayCommand]
    private void ToggleShowAllFiles()
    {
        ShowAllFiles = !ShowAllFiles;
        UpdateDisplayedFiles();
        OnPropertyChanged(nameof(FileCountText));
    }

    private void UpdateDisplayedFiles()
    {
        DisplayedFiles.Clear();
        var filesToShow = ShowAllFiles ? Files : TopFiles;
        foreach (var file in filesToShow)
        {
            DisplayedFiles.Add(file);
        }
    }

    [RelayCommand]
    private void SortFilesBy(string sortMode)
    {
        CurrentSortMode = sortMode;
        
        var sorted = sortMode switch
        {
            "Size" => Files.OrderByDescending(f => f.Size).ToList(),
            "Name" => Files.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList(),
            "Date" => Files.OrderByDescending(f => f.LastModified).ToList(),
            _ => Files.ToList()
        };

        Files.Clear();
        foreach (var file in sorted)
        {
            Files.Add(file);
        }

        UpdateDisplayedFiles();
    }

    private static string FormatBytes(long bytes)
    {
        const long kb = 1024;
        const long mb = kb * 1024;
        const long gb = mb * 1024;
        const long tb = gb * 1024;

        return bytes switch
        {
            >= tb => $"{bytes / (double)tb:F2} TB",
            >= gb => $"{bytes / (double)gb:F2} GB",
            >= mb => $"{bytes / (double)mb:F2} MB",
            >= kb => $"{bytes / (double)kb:F2} KB",
            _ => $"{bytes} B"
        };
    }

    partial void OnDirectoryItemChanged(DirectoryItem? oldValue, DirectoryItem newValue)
    {
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(FullPath));
        OnPropertyChanged(nameof(Size));
        OnPropertyChanged(nameof(FormattedSize));
        OnPropertyChanged(nameof(FileCount));
        OnPropertyChanged(nameof(DirectoryCount));
        OnPropertyChanged(nameof(PercentageOfParent));
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(Error));
        OnPropertyChanged(nameof(HasFiles));
        LoadFiles();
    }
}