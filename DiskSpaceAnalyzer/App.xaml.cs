using System.Windows;
using DiskSpaceAnalyzer.Services;
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

    protected override void OnStartup(StartupEventArgs e)
    {
        _host = Host.CreateDefaultBuilder()
            .ConfigureServices((_, services) =>
            {
                // Core Services
                services.AddSingleton<FileSystemService>();
                services.AddSingleton<ParallelFileSystemService>();
                services.AddSingleton<IFileSystemService, ParallelFileSystemService>();
                services.AddSingleton<IDialogService, DialogService>();
                
                // Robocopy Module Services (completely modular)
                services.AddSingleton<IRobocopyService, RobocopyService>();
                services.AddSingleton<ProcessSuspender>();
                services.AddSingleton<RobocopyCommandBuilder>();
                services.AddSingleton<RobocopyLogParser>();

                // ViewModels
                services.AddTransient<MainViewModel>();
                services.AddTransient<RobocopyViewModel>();

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