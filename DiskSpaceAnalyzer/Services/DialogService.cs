using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using DiskSpaceAnalyzer.Models;
using DiskSpaceAnalyzer.ViewModels;
using DiskSpaceAnalyzer.Views;
using Microsoft.Win32;
using DialogResult = DiskSpaceAnalyzer.Models.DialogResult;

namespace DiskSpaceAnalyzer.Services;

public class DialogService : IDialogService
{
    public string? SelectFolder(string title = "Select Folder")
    {
        var dialog = new OpenFolderDialog
        {
            Title = title
        };

        var result = dialog.ShowDialog();
        return result == true ? dialog.FolderName : null;
    }

    #region Synchronous Methods (Backward Compatibility)

    public void ShowError(string title, string message)
    {
        ShowDialog(title, message, DialogType.Error, DialogButton.OK, GetActiveWindow());
    }

    public void ShowInfo(string title, string message)
    {
        ShowDialog(title, message, DialogType.Information, DialogButton.OK, GetActiveWindow());
    }

    public void ShowSuccess(string title, string message)
    {
        ShowDialog(title, message, DialogType.Success, DialogButton.OK, GetActiveWindow());
    }

    public void ShowWarning(string title, string message)
    {
        ShowDialog(title, message, DialogType.Warning, DialogButton.OK, GetActiveWindow());
    }

    public bool ShowConfirmation(string title, string message)
    {
        var result = ShowDialog(title, message, DialogType.Confirmation, DialogButton.YesNo, GetActiveWindow());
        return result == DialogResult.Yes;
    }

    public bool ShowQuestion(string title, string message)
    {
        var result = ShowDialog(title, message, DialogType.Question, DialogButton.YesNo, GetActiveWindow());
        return result == DialogResult.Yes;
    }

    public string? ShowInput(string title, string message, string defaultValue = "")
    {
        var viewModel = new ModernDialogViewModel
        {
            Title = title,
            Message = message,
            DialogType = DialogType.Question,
            Buttons = DialogButton.OKCancel,
            ShowInput = true,
            InputText = defaultValue
        };

        var dialog = new ModernDialog(viewModel)
        {
            Owner = GetActiveWindow()
        };

        var result = dialog.ShowDialog();
        return result == true ? viewModel.InputText : null;
    }

    #endregion

    #region Async Methods (Preferred)

    public Task ShowErrorAsync(string title, string message, Window? owner = null)
    {
        return ShowDialogAsync(title, message, DialogType.Error, DialogButton.OK, owner);
    }

    public Task ShowInfoAsync(string title, string message, Window? owner = null)
    {
        return ShowDialogAsync(title, message, DialogType.Information, DialogButton.OK, owner);
    }

    public Task ShowSuccessAsync(string title, string message, Window? owner = null)
    {
        return ShowDialogAsync(title, message, DialogType.Success, DialogButton.OK, owner);
    }

    public Task ShowWarningAsync(string title, string message, Window? owner = null)
    {
        return ShowDialogAsync(title, message, DialogType.Warning, DialogButton.OK, owner);
    }

    public async Task<bool> ShowConfirmationAsync(string title, string message, Window? owner = null)
    {
        var result = await ShowDialogAsync(title, message, DialogType.Confirmation, DialogButton.YesNo, owner);
        return result == DialogResult.Yes;
    }

    public async Task<bool> ShowQuestionAsync(string title, string message, Window? owner = null)
    {
        var result = await ShowDialogAsync(title, message, DialogType.Question, DialogButton.YesNo, owner);
        return result == DialogResult.Yes;
    }

    public async Task<string?> ShowInputAsync(string title, string message, string defaultValue = "",
        Window? owner = null)
    {
        var viewModel = new ModernDialogViewModel
        {
            Title = title,
            Message = message,
            DialogType = DialogType.Question,
            Buttons = DialogButton.OKCancel,
            ShowInput = true,
            InputText = defaultValue
        };

        var dialog = new ModernDialog(viewModel)
        {
            Owner = owner ?? Application.Current.MainWindow
        };

        var result = await Task.Run(() => Application.Current.Dispatcher.Invoke(() => dialog.ShowDialog()));
        return result == true ? viewModel.InputText : null;
    }

    #endregion

    #region Private Helper Methods

    private DialogResult ShowDialog(string title, string message, DialogType type, DialogButton buttons,
        Window? owner = null)
    {
        var viewModel = new ModernDialogViewModel
        {
            Title = title,
            Message = message,
            DialogType = type,
            Buttons = buttons
        };

        var dialog = new ModernDialog(viewModel)
        {
            Owner = owner ?? GetActiveWindow()
        };

        dialog.ShowDialog();
        return viewModel.Result;
    }

    private Task<DialogResult> ShowDialogAsync(string title, string message, DialogType type, DialogButton buttons,
        Window? owner = null)
    {
        return Task.Run(() =>
        {
            return Application.Current.Dispatcher.Invoke(() =>
            {
                var viewModel = new ModernDialogViewModel
                {
                    Title = title,
                    Message = message,
                    DialogType = type,
                    Buttons = buttons
                };

                var dialog = new ModernDialog(viewModel)
                {
                    Owner = owner ?? GetActiveWindow()
                };

                dialog.ShowDialog();
                return viewModel.Result;
            });
        });
    }

    /// <summary>
    ///     Gets the currently active window or falls back to MainWindow
    /// </summary>
    private static Window? GetActiveWindow()
    {
        // Try to get the active window
        var activeWindow = Application.Current.Windows
            .OfType<Window>()
            .FirstOrDefault(w => w.IsActive);

        if (activeWindow != null)
            return activeWindow;

        // Fall back to any visible window
        var visibleWindow = Application.Current.Windows
            .OfType<Window>()
            .FirstOrDefault(w => w.IsVisible);

        return visibleWindow ?? Application.Current.MainWindow;
    }

    #endregion
}