using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiskSpaceAnalyzer.Models;
using DiskSpaceAnalyzer.Services;

namespace DiskSpaceAnalyzer.ViewModels
{
    public partial class MainViewModel : BaseViewModel
    {
        private readonly IFileSystemService _fileSystemService;
        private readonly IDialogService _dialogService;
        private CancellationTokenSource? _cancellationTokenSource;

        [ObservableProperty]
        private string _selectedPath = string.Empty;

        [ObservableProperty]
        private ScanMode _selectedScanMode = ScanMode.TopLevel;

        [ObservableProperty]
        private bool _isScanning;

        [ObservableProperty]
        private string _scanProgress = string.Empty;

        [ObservableProperty]
        private int _progressPercentage;

        [ObservableProperty]
        private ScanResult? _scanResult;

        [ObservableProperty]
        private DirectoryItemViewModel? _selectedDirectory;

        [ObservableProperty]
        private SortMode _selectedSortMode = SortMode.Size;

        [ObservableProperty]
        private SortDirection _selectedSortDirection = SortDirection.Descending;

        public ObservableCollection<DirectoryItemViewModel> DirectoryItems { get; }
        public ICollectionView SortedItemsView { get; }
        public ObservableCollection<string> AvailableDrives { get; }
        public ObservableCollection<string> ScanErrors { get; }

        public MainViewModel(IFileSystemService fileSystemService, IDialogService dialogService)
        {
            _fileSystemService = fileSystemService;
            _dialogService = dialogService;
            DirectoryItems = [];
            AvailableDrives = [];
            ScanErrors = [];

            SortedItemsView = CollectionViewSource.GetDefaultView(DirectoryItems);
            SortedItemsView.SortDescriptions.Add(
                new SortDescription(nameof(DirectoryItemViewModel.Size),
                    ListSortDirection.Descending));
            SortedItemsView.SortDescriptions.Add(
                new SortDescription(nameof(DirectoryItemViewModel.DisplayName),
                    ListSortDirection.Ascending));
            
            LoadAvailableDrives();
        }

        private void LoadAvailableDrives()
        {
            AvailableDrives.Clear();
            foreach (var drive in _fileSystemService.GetDrives())
            {
                AvailableDrives.Add(drive);
            }
        }

        [RelayCommand]
        private void SelectFolder()
        {
            var selectedPath = _dialogService.SelectFolder("Select folder to analyze");
            if (!string.IsNullOrEmpty(selectedPath))
            {
                SelectedPath = selectedPath;
            }
        }

        [RelayCommand]
        private async Task StartScan()
        {
            if (string.IsNullOrEmpty(SelectedPath) || !_fileSystemService.DirectoryExists(SelectedPath))
            {
                _dialogService.ShowError("Invalid Path", "Please select a valid directory path.");
                return;
            }

            if (IsScanning)
            {
                return;
            }

            IsScanning = true;
            _cancellationTokenSource = new CancellationTokenSource();
            
            var progress = new Progress<ScanProgress>(UpdateProgress);
            DirectoryItems.Clear();
            ScanErrors.Clear();

            try
            {
                await Task.Run(async () =>
                {
                    ScanResult = await _fileSystemService.ScanDirectoryAsync(
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
            // 1) Plain text for the status bar
            ScanProgress = $"Scanning: {p.CurrentPath}";

            // 2) Live list / treemap update
            if (p.CompletedItem == null) return;

            // Do we already have the directory in the collection?
            var existing = DirectoryItems
                .FirstOrDefault(vm => vm.FullPath.Equals(p.CompletedItem.FullPath,
                    StringComparison.OrdinalIgnoreCase));

            if (existing == null)
            {
                // First time we see this directory – simply add it.
                DirectoryItems.Add(new DirectoryItemViewModel(p.CompletedItem));
            }
            else
            {
                // The size (or error info …) may have changed – update the VM.
                existing.DirectoryItem = p.CompletedItem;
            }

            // Keep the list sorted according to the current sort settings
            SortItems(DirectoryItems.ToList());
        }

        private void UpdateDirectoryItems()
        {
            if (ScanResult?.RootDirectory == null) return;
            
            var items = ScanResult.RootDirectory.Children
                .Select(item => new DirectoryItemViewModel(item))
                .ToList();

            SortItems(items);

            foreach (var item in items.Where(item => !DirectoryItems.Any(existing => existing.FullPath.Equals(item.FullPath, StringComparison.OrdinalIgnoreCase))))
            {
                DirectoryItems.Add(item);
            }
        }

        private void UpdateScanErrors()
        {
            ScanErrors.Clear();
            if (ScanResult?.Errors == null) return;
            foreach (var error in ScanResult.Errors)
            {
                ScanErrors.Add(error);
            }
        }

        private void SortItems(System.Collections.Generic.List<DirectoryItemViewModel> items)
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
            {
                SelectedSortDirection = SelectedSortDirection == SortDirection.Ascending 
                    ? SortDirection.Descending 
                    : SortDirection.Ascending;
            }
            else
            {
                SelectedSortMode = sortMode;
            }
            
            UpdateDirectoryItems();
        }

        public string ScanSummary 
        {
            get
            {
                if (ScanResult == null) return string.Empty;
                
                return $"Scanned {ScanResult.TotalDirectories:N0} directories and {ScanResult.TotalFiles:N0} files " +
                       $"({FormatBytes(ScanResult.TotalSize)}) in {ScanResult.ScanDuration.TotalSeconds:F1} seconds";
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

        partial void OnScanResultChanged(ScanResult? value)
        {
            OnPropertyChanged(nameof(ScanSummary));
        }
    }
}
