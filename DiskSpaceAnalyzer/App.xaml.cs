using System.Windows;
using DiskSpaceAnalyzer.Services;
using DiskSpaceAnalyzer.ViewModels;
using DiskSpaceAnalyzer.Views;
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
                // Services
                services.AddSingleton<ParallelFileSystemService>();
                services.AddSingleton<FileSystemService>();
                services.AddSingleton<IDialogService, DialogService>();

                // ViewModels
                services.AddTransient<MainViewModel>();

                // Views
                services.AddTransient<MainWindow>();
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