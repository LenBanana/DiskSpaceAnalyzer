namespace DiskSpaceAnalyzer.Services;

public interface IDialogService
{
    string? SelectFolder(string title = "Select Folder");
    void ShowError(string title, string message);
    void ShowInfo(string title, string message);
}