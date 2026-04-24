# Robocopy Module - Self-Contained File Copy Tool

## Overview

A complete, production-ready WPF wrapper for Windows Robocopy with real-time progress tracking, pause/resume
functionality, and comprehensive error handling.

## Features

- ✅ **Real-time progress tracking** - See bytes copied, files processed, transfer speed, and ETA
- ✅ **Pre-scan** - Calculates total size before copying for accurate progress
- ✅ **Pause/Resume** - True process suspension (not restart-based)
- ✅ **Error tracking** - Captures and displays all robocopy errors with friendly messages
- ✅ **Multiple presets** - Copy, Sync, Mirror, Backup, Custom
- ✅ **Full robocopy options** - Mirror mode, multithreading, backup mode, security, and more
- ✅ **Cancellation support** - Clean cancellation with partial progress preserved
- ✅ **Log file** - Full robocopy log saved for review

## Architecture

### Models (`Models/Robocopy/`)

- `RobocopyOptions.cs` - Configuration with validation
- `RobocopyProgress.cs` - Immutable progress data
- `RobocopyResult.cs` - Final results with statistics
- `RobocopyError.cs` - Individual error details
- `RobocopyJobState.cs` - Job state enum
- `RobocopyPreset.cs` - Common presets

### Services (`Services/Robocopy/`)

- `IRobocopyService.cs` - Main service interface
- `RobocopyService.cs` - Core implementation
- `RobocopyLogParser.cs` - Parses robocopy output
- `RobocopyCommandBuilder.cs` - Builds command arguments
- `ProcessSuspender.cs` - P/Invoke for pause/resume

### UI (`Views/Robocopy/`, `ViewModels/`, `Styles/`)

- `RobocopyWindow.xaml` - Main window
- `RobocopyViewModel.cs` - ViewModel with commands
- `RobocopyStyles.xaml` - Self-contained styles

## How to Copy to Another Project

### 1. Copy Files

```
Copy these folders:
- Models/Robocopy/         → Your project
- Services/Robocopy/       → Your project
- Views/Robocopy/          → Your project
- ViewModels/RobocopyViewModel.cs → Your project
- Styles/RobocopyStyles.xaml → Your project
```

### 2. Register Services (in App.xaml.cs or Startup.cs)

```csharp
services.AddSingleton<IRobocopyService, RobocopyService>();
services.AddSingleton<ProcessSuspender>();
services.AddSingleton<RobocopyCommandBuilder>();
services.AddSingleton<RobocopyLogParser>();
services.AddTransient<RobocopyViewModel>();
services.AddTransient<RobocopyWindow>();
```

### 3. Dependencies Required

- `CommunityToolkit.Mvvm` (8.2.2+)
- `Microsoft.Extensions.DependencyInjection`
- An `IFileSystemService` implementation for pre-scanning (or remove that feature)
- An `IDialogService` for folder browser (or use standard dialogs)

### 4. Optional: Remove Pre-Scan

If you don't have IFileSystemService, in `RobocopyService.cs`:

```csharp
// Comment out or remove PreScanSourceAsync() call
// Set fixed values or skip progress calculation
```

## Usage Example

### Open Window from Button

```csharp
[RelayCommand]
private void OpenRobocopy()
{
    var window = _serviceProvider.GetRequiredService<RobocopyWindow>();
    window.Show();
}
```

### Programmatic Usage

```csharp
var options = new RobocopyOptions
{
    SourcePath = @"C:\Source",
    DestinationPath = @"D:\Backup",
    CopySubdirectories = true,
    UseMultithreading = true,
    ThreadCount = 8
};

var progress = new Progress<RobocopyProgress>(p =>
{
    Console.WriteLine($"{p.PercentComplete:F1}% - {p.CurrentFile}");
});

var result = await _robocopyService.CopyAsync(
    options,
    progress,
    cancellationToken);

Console.WriteLine($"Copied {result.FilesCopied} files, {result.BytesCopied} bytes");
Console.WriteLine($"Exit Code: {result.ExitCode} - {result.ExitCodeMessage}");
```

## Configuration Options

### Presets

- **Copy** - Basic recursive copy
- **Sync** - Copy and skip older files
- **Mirror** - ⚠️ Copy and delete extra files at destination
- **Backup** - Copy with security and backup mode
- **Custom** - Full manual control

### Advanced Options

- Multi-threading (1-128 threads)
- Mirror mode (deletes at destination)
- Backup mode (requires admin for some files)
- Copy security/ACLs
- Exclude directories/files
- File size filters
- Move mode (delete after copy)
- Retry count and wait time

## Exit Codes

Robocopy uses bitwise exit codes (0-16):

- `0` - No changes, already up to date
- `1` - Files copied successfully
- `2` - Extra files detected
- `4` - Mismatches detected
- `8` - Failed copies (some errors)
- `16` - Fatal error

Codes can be combined (e.g., `3` = files copied + extra files).

## Error Handling

All robocopy errors are captured and parsed:

- Error code (e.g., 5 = Access Denied)
- Hex code (e.g., 0x00000005)
- File path that caused the error
- Friendly error message
- Timestamp

Common errors:

- `5` - Access denied
- `32` - File in use
- `123` - Invalid filename
- `206` - Path too long
- `1314` - Privilege not held

## Pause/Resume Implementation

Uses Windows kernel32.dll functions:

- `SuspendThread()` - Suspends all robocopy threads
- `ResumeThread()` - Resumes all threads

The process stays in memory while paused, preserving all state.

## Performance Notes

- Pre-scan uses parallel directory scanning (fast)
- Log file parsed every 500ms during copy (low overhead)
- Multi-threading enabled by default (8 threads)
- Progress calculation is approximate during copy, accurate at end

## Testing Checklist

- [ ] Small folder copy (< 100 MB)
- [ ] Large folder copy (> 1 GB)
- [ ] Pause/Resume during copy
- [ ] Cancel mid-copy
- [ ] Mirror mode with test data
- [ ] Error scenarios (locked files, permissions)
- [ ] Invalid paths
- [ ] Source = Destination validation

## Future Enhancements

- [ ] Copy presets save/load
- [ ] Scheduled copies
- [ ] Job history
- [ ] Bandwidth throttling
- [ ] Network path support validation
- [ ] Dry-run preview
- [ ] File filtering by date/attributes

## License

This module is part of DiskSpaceAnalyzer and can be freely used/modified.

## Notes

- Robocopy.exe must be present (Windows Vista+)
- Some options require admin privileges (backup mode, security)
- Mirror mode is DANGEROUS - it deletes files at destination
- Log files are created in temp folder and not auto-deleted
- Process suspension works on all Windows versions with kernel32.dll
