using System.Threading.Tasks;
using System.Windows;

namespace DiskSpaceAnalyzer.Services;

public interface IDialogService
{
    string? SelectFolder(string title = "Select Folder");
    
    // Synchronous methods (for backward compatibility)
    void ShowError(string title, string message);
    void ShowInfo(string title, string message);
    void ShowSuccess(string title, string message);
    void ShowWarning(string title, string message);
    bool ShowConfirmation(string title, string message);
    bool ShowQuestion(string title, string message);
    string? ShowInput(string title, string message, string defaultValue = "");
    
    // Async methods (preferred)
    Task ShowErrorAsync(string title, string message, Window? owner = null);
    Task ShowInfoAsync(string title, string message, Window? owner = null);
    Task ShowSuccessAsync(string title, string message, Window? owner = null);
    Task ShowWarningAsync(string title, string message, Window? owner = null);
    Task<bool> ShowConfirmationAsync(string title, string message, Window? owner = null);
    Task<bool> ShowQuestionAsync(string title, string message, Window? owner = null);
    Task<string?> ShowInputAsync(string title, string message, string defaultValue = "", Window? owner = null);
}