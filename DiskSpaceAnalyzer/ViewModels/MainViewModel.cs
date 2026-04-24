using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskSpaceAnalyzer.Models;
using DiskSpaceAnalyzer.Services;
using DiskSpaceAnalyzer.Views.Robocopy;
using Microsoft.Extensions.DependencyInjection;

namespace DiskSpaceAnalyzer.ViewModels;

public partial class MainViewModel : BaseViewModel
{
    private readonly IDialogService _dialogService;
    private readonly FileSystemService _fileSystemService;
    private readonly ParallelFileSystemService _parallelFileSystemService;
    private readonly IServiceProvider _serviceProvider;
    private CancellationTokenSource? _cancellationTokenSource;

    private IFileSystemService _currentFileSystemService;

    [ObservableProperty] private bool _isScanning;
    private string _lastSelectedPath = string.Empty;

    [ObservableProperty] private int _progressPercentage;

    [ObservableProperty] private string _scanProgress = string.Empty;

    [ObservableProperty] private ScanResult? _scanResult;

    [ObservableProperty] private DirectoryItemViewModel? _selectedDirectory;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenCurrentDirectoryCommand))]
    [NotifyCanExecuteChangedFor(nameof(StartScanCommand))]
    private string _selectedPath = string.Empty;

    [ObservableProperty] private ScanMode _selectedScanMode = ScanMode.Recursive;

    [ObservableProperty] private SortDirection _selectedSortDirection = SortDirection.Descending;

    [ObservableProperty] private SortMode _selectedSortMode = SortMode.Size;

    [ObservableProperty] private bool _useParallelProcessing = true;

    [ObservableProperty] private bool _trackIndividualFiles = true;

    public MainViewModel(FileSystemService fileSystemService, ParallelFileSystemService parallelFileSystemService,
        IDialogService dialogService, IServiceProvider serviceProvider)
    {
        _fileSystemService = fileSystemService;
        _parallelFileSystemService = parallelFileSystemService;
        _currentFileSystemService = parallelFileSystemService;
        _dialogService = dialogService;
        _serviceProvider = serviceProvider;
        DirectoryItems = [];
        SelectedItems = [];
        AvailableDrives = [];
        ScanErrors = [];

        // Wire up error handler for FileItemViewModel
        FileItemViewModel.ErrorHandler = (title, message) => _dialogService.ShowError(title, message);

        LoadAvailableDrives();
    }
    
    [RelayCommand]
    private void OpenRobocopy()
    {
        var window = _serviceProvider.GetRequiredService<RobocopyWindow>();
        window.Show();
    }

    public ObservableCollection<DirectoryItemViewModel> DirectoryItems { get; }
    public ObservableCollection<DirectoryItemViewModel> SelectedItems { get; }
    private ObservableCollection<string> AvailableDrives { get; }
    private ObservableCollection<string> ScanErrors { get; }

    public string ScanSummary
    {
        get
        {
            if (ScanResult == null) return string.Empty;

            return $"Scanned {ScanResult.TotalDirectories:N0} directories and {ScanResult.TotalFiles:N0} files " +
                   $"({FormatBytes(ScanResult.TotalSize)}) in {ScanResult.ScanDuration.TotalSeconds:F1} seconds";
        }
    }

    private void LoadAvailableDrives()
    {
        AvailableDrives.Clear();
        foreach (var drive in _currentFileSystemService.GetDrives()) AvailableDrives.Add(drive);
    }

    [RelayCommand]
    private void SelectFolder()
    {
        var selectedPath = _dialogService.SelectFolder("Select folder to analyze");
        if (string.IsNullOrEmpty(selectedPath)) return;
        SelectedPath = selectedPath;
        _lastSelectedPath = selectedPath;
    }

    [RelayCommand(CanExecute = nameof(IsValidDirectory))]
    private void OpenCurrentDirectory()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = SelectedPath,
            UseShellExecute = true
        });
    }

    private bool IsValidDirectory()
    {
        return !string.IsNullOrEmpty(SelectedPath) && _currentFileSystemService.DirectoryExists(SelectedPath);
    }

    [RelayCommand(CanExecute = nameof(IsValidDirectory))]
    private async Task StartScan()
    {
        if (string.IsNullOrEmpty(SelectedPath) || !_currentFileSystemService.DirectoryExists(SelectedPath))
        {
            _dialogService.ShowError("Invalid Path", "Please select a valid directory path.");
            return;
        }

        if (IsScanning) return;

        IsScanning = true;
        _cancellationTokenSource = new CancellationTokenSource();

        // Update file tracking setting on services
        _fileSystemService.TrackIndividualFiles = TrackIndividualFiles;
        _parallelFileSystemService.TrackIndividualFiles = TrackIndividualFiles;

        var progress = new Progress<ScanProgress>(UpdateProgress);
        DirectoryItems.Clear();
        ScanErrors.Clear();

        try
        {
            await Task.Run(async () =>
            {
                ScanResult = await _currentFileSystemService.ScanDirectoryAsync(
                    SelectedPath,
                    SelectedScanMode,
                    progress,
                    _cancellationTokenSource.Token);
            });

            UpdateDirectoryItems();
            UpdateScanErrors();
        }
        catch (OperationCanceledException)
        {
            ScanProgress = "Scan cancelled";
        }
        catch (Exception ex)
        {
            _dialogService.ShowError("Scan Error", $"An error occurred during scanning: {ex.Message}");
        }
        finally
        {
            IsScanning = false;
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
        }
    }

    [RelayCommand]
    private void CancelScan()
    {
        _cancellationTokenSource?.Cancel();
    }

    [RelayCommand]
    private void SelectDrive(string drive)
    {
        SelectedPath = drive;
    }

    private void UpdateProgress(ScanProgress p)
    {
        ScanProgress = $"Scanning: {p.CurrentPath}";
    }

    private void UpdateDirectoryItems()
    {
        if (ScanResult?.RootDirectory == null) return;

        DirectoryItems.Clear();
        SelectedItems.Clear();
        var items = ScanResult.RootDirectory.Children
            .Select(item => new DirectoryItemViewModel(item))
            .ToList();

        SortItems(items);

        foreach (var item in items)
        {
            DirectoryItems.Add(item);
            SelectedItems.Add(item);
        }
    }

    public void UpdateSelectedDirectory(DirectoryItemViewModel? directory)
    {
        List<DirectoryItemViewModel> items;
        SelectedDirectory = directory;
        SelectedItems.Clear();
        if (directory == null)
        {
            SelectedPath = _lastSelectedPath;
            items = DirectoryItems.ToList();
            SortItems(items);
            foreach (var item in items) SelectedItems.Add(item);

            return;
        }

        SelectedPath = directory.FullPath;

        items = directory.Children.ToList();
        SortItems(items);
        foreach (var child in items) SelectedItems.Add(child);
    }

    private void UpdateScanErrors()
    {
        ScanErrors.Clear();
        if (ScanResult?.Errors == null) return;
        foreach (var error in ScanResult.Errors) ScanErrors.Add(error);
    }

    private void SortItems(List<DirectoryItemViewModel> items)
    {
        switch (SelectedSortMode)
        {
            case SortMode.Name:
                items.Sort((a, b) => SelectedSortDirection == SortDirection.Ascending
                    ? string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase)
                    : string.Compare(b.DisplayName, a.DisplayName, StringComparison.OrdinalIgnoreCase));
                break;
            case SortMode.Size:
                items.Sort((a, b) => SelectedSortDirection == SortDirection.Ascending
                    ? a.Size.CompareTo(b.Size)
                    : b.Size.CompareTo(a.Size));
                break;
            case SortMode.LastModified:
                items.Sort((a, b) => SelectedSortDirection == SortDirection.Ascending
                    ? a.DirectoryItem.LastModified.CompareTo(b.DirectoryItem.LastModified)
                    : b.DirectoryItem.LastModified.CompareTo(a.DirectoryItem.LastModified));
                break;
            case SortMode.FileCount:
                items.Sort((a, b) => SelectedSortDirection == SortDirection.Ascending
                    ? a.FileCount.CompareTo(b.FileCount)
                    : b.FileCount.CompareTo(a.FileCount));
                break;
        }
    }

    [RelayCommand]
    private void ChangeSortMode(SortMode sortMode)
    {
        if (SelectedSortMode == sortMode)
            SelectedSortDirection = SelectedSortDirection == SortDirection.Ascending
                ? SortDirection.Descending
                : SortDirection.Ascending;
        else
            SelectedSortMode = sortMode;

        var items = SelectedItems.ToList();
        SortItems(items);

        SelectedItems.Clear();
        foreach (var item in items) SelectedItems.Add(item);
    }

    partial void OnUseParallelProcessingChanged(bool value)
    {
        _currentFileSystemService = value
            ? _parallelFileSystemService
            : _fileSystemService;
        
        // Sync file tracking setting
        _currentFileSystemService.TrackIndividualFiles = TrackIndividualFiles;
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

    partial void OnScanResultChanged(ScanResult? value)
    {
        OnPropertyChanged(nameof(ScanSummary));
    }
}