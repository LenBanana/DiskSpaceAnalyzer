using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskSpaceAnalyzer.Models;

namespace DiskSpaceAnalyzer.ViewModels;

public partial class FileItemViewModel : BaseViewModel
{
    [ObservableProperty] private FileItem _fileItem;

    public FileItemViewModel(FileItem fileItem, DirectoryItemViewModel parent)
    {
        _fileItem = fileItem;
        Parent = parent;
    }

    public static Action<string, string>? ErrorHandler { get; set; }

    public DirectoryItemViewModel Parent { get; }

    public string Name => FileItem.Name;
    public string FullPath => FileItem.FullPath;
    public long Size => FileItem.Size;
    public string FormattedSize => FormatBytes(FileItem.Size);
    public DateTime LastModified => FileItem.LastModified;
    public string Extension => FileItem.Extension;
    public double PercentageOfParent => FileItem.PercentageOfParent;

    public string FileTypeIcon
    {
        get
        {
            return Extension.ToLowerInvariant() switch
            {
                ".mp4" or ".avi" or ".mkv" or ".mov" or ".wmv" or ".flv" => "Video",
                ".mp3" or ".wav" or ".flac" or ".aac" or ".ogg" or ".m4a" => "Music",
                ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".svg" or ".webp" => "Image",
                ".pdf" => "FilePdfBox",
                ".doc" or ".docx" => "FileWord",
                ".xls" or ".xlsx" => "FileExcel",
                ".ppt" or ".pptx" => "FilePowerpoint",
                ".zip" or ".rar" or ".7z" or ".tar" or ".gz" => "FolderZip",
                ".exe" or ".msi" => "Application",
                ".txt" => "FileDocument",
                ".cs" or ".cpp" or ".h" or ".java" or ".py" or ".js" or ".html" or ".css" => "FileCode",
                _ => "File"
            };
        }
    }

    public string ExtensionBadgeColor
    {
        get
        {
            return Extension.ToLowerInvariant() switch
            {
                ".mp4" or ".avi" or ".mkv" or ".mov" or ".wmv" or ".flv" => "#FFB74DD5", // Purple for videos
                ".mp3" or ".wav" or ".flac" or ".aac" or ".ogg" or ".m4a" => "#FFEF5350", // Red for audio
                ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".svg" or ".webp" => "#FF29B6F6", // Blue for images
                ".pdf" => "#FFF44336", // Red for PDF
                ".doc" or ".docx" or ".txt" => "#FF42A5F5", // Blue for documents
                ".xls" or ".xlsx" => "#FF66BB6A", // Green for Excel
                ".ppt" or ".pptx" => "#FFFF9800", // Orange for PowerPoint
                ".zip" or ".rar" or ".7z" or ".tar" or ".gz" => "#FFFFCA28", // Yellow for archives
                ".exe" or ".msi" => "#FF78909C", // Gray for executables
                ".cs" or ".cpp" or ".h" or ".java" or ".py" or ".js" or ".html"
                    or ".css" => "#FF26A69A", // Teal for code
                _ => "#FF9E9E9E" // Gray for others
            };
        }
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

    [RelayCommand]
    private void OpenFile()
    {
        try
        {
            if (File.Exists(FullPath))
                Process.Start(new ProcessStartInfo
                {
                    FileName = FullPath,
                    UseShellExecute = true
                });
        }
        catch (Exception ex)
        {
            ErrorHandler?.Invoke("Cannot Open File", $"Failed to open file: {ex.Message}");
        }
    }

    [RelayCommand]
    private void ShowInExplorer()
    {
        try
        {
            if (File.Exists(FullPath)) Process.Start("explorer.exe", $"/select,\"{FullPath}\"");
        }
        catch (Exception ex)
        {
            ErrorHandler?.Invoke("Cannot Open Explorer", $"Failed to open Explorer: {ex.Message}");
        }
    }

    [RelayCommand]
    private void CopyPath()
    {
        try
        {
            Clipboard.SetText(FullPath);
        }
        catch (Exception ex)
        {
            ErrorHandler?.Invoke("Cannot Copy Path", $"Failed to copy path: {ex.Message}");
        }
    }

    partial void OnFileItemChanged(FileItem? oldValue, FileItem newValue)
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(FullPath));
        OnPropertyChanged(nameof(Size));
        OnPropertyChanged(nameof(FormattedSize));
        OnPropertyChanged(nameof(LastModified));
        OnPropertyChanged(nameof(Extension));
        OnPropertyChanged(nameof(PercentageOfParent));
        OnPropertyChanged(nameof(FileTypeIcon));
    }
}