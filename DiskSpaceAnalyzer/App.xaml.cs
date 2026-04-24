using System.Windows;
using DiskSpaceAnalyzer.Services;
using DiskSpaceAnalyzer.Services.FileCopy;
using DiskSpaceAnalyzer.Services.Mft;
using DiskSpaceAnalyzer.Services.Robocopy;
using DiskSpaceAnalyzer.ViewModels;
using DiskSpaceAnalyzer.Views;
using DiskSpaceAnalyzer.Views.Robocopy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace DiskSpaceAnalyzer;

public partial class App : Application
{
    private IHost? _host;

    // Carries the engine/path passed on the command line after a UAC self-relaunch
    // so the window can auto-select them once it constructs.
    public static bool LaunchedWithMftRequested { get; private set; }
    public static string? LaunchedWithPath { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        var (engineRequested, path) = MftElevationHelper.ParseLaunchArgs(e.Args);
        LaunchedWithMftRequested = engineRequested;
        LaunchedWithPath = path;

        _host = Host.CreateDefaultBuilder()
            .ConfigureServices((_, services) =>
            {
                // Core Services
                services.AddSingleton<FileSystemService>();
                services.AddSingleton<ParallelFileSystemService>();
                services.AddSingleton<MftFileSystemService>();
                services.AddSingleton<IFileSystemService, ParallelFileSystemService>();
                services.AddSingleton<IDialogService, DialogService>();

                // File Copy Services (generic infrastructure)
                services.AddSingleton<IFileIntegrityService, FileIntegrityService>();
                services.AddSingleton<IDirectoryScanService, DirectoryScanService>();
                services.AddSingleton<NativeFileCopyService>();
                services.AddSingleton<IFileCopyServiceFactory, FileCopyServiceFactory>();

                // Robocopy Module Services (completely modular)
                services.AddSingleton<IRobocopyService, RobocopyService>();
                services.AddSingleton<IExclusionPresetService, ExclusionPresetService>();
                services.AddSingleton<IGitIgnoreParserService, GitIgnoreParserService>();
                services.AddSingleton<ProcessSuspender>();
                services.AddSingleton<RobocopyCommandBuilder>();
                services.AddSingleton<RobocopyLogParser>();

                // ViewModels
                services.AddTransient<MainViewModel>();
                services.AddTransient<FileCopyViewModel>();

                // Views
                services.AddTransient<MainWindow>();
                services.AddTransient<RobocopyWindow>();
            })
            .Build();

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        mainWindow.Show();

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _host?.Dispose();
        base.OnExit(e);
    }
}