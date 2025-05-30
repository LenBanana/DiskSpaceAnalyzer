using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using DiskSpaceAnalyzer.Models;

namespace DiskSpaceAnalyzer.ViewModels;

public partial class DirectoryItemViewModel : BaseViewModel
{
    [ObservableProperty] private DirectoryItem _directoryItem;

    [ObservableProperty] private bool _isExpanded;

    [ObservableProperty] private bool _isSelected;

    public DirectoryItemViewModel(DirectoryItem directoryItem, DirectoryItemViewModel? parent = null)
    {
        _directoryItem = directoryItem;
        Parent = parent;
        Children = [];

        LoadChildren();
    }

    public ObservableCollection<DirectoryItemViewModel> Children { get; }
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

    private void LoadChildren()
    {
        Children.Clear();
        foreach (var child in DirectoryItem.Children) Children.Add(new DirectoryItemViewModel(child, this));
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
    }
}