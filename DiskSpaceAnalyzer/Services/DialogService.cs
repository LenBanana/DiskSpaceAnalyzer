using System.Windows;
using Microsoft.Win32;

namespace DiskSpaceAnalyzer.Services;

public class DialogService : IDialogService
{
    public string? SelectFolder(string title = "Select Folder")
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select Folder"
        };

        var result = dialog.ShowDialog();
        return result == true ? dialog.FolderName : null;
    }

    public void ShowError(string title, string message)
    {
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    public void ShowInfo(string title, string message)
    {
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
    }
}