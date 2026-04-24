using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using DiskSpaceAnalyzer.Models;

namespace DiskSpaceAnalyzer.ViewModels;

public partial class ModernDialogViewModel : ObservableObject
{
    [ObservableProperty] private DialogButton _buttons = DialogButton.OK;
    [ObservableProperty] private DialogType _dialogType = DialogType.Information;
    [ObservableProperty] private string _inputPlaceholder = string.Empty;
    [ObservableProperty] private string _inputText = string.Empty;
    [ObservableProperty] private string _message = string.Empty;
    [ObservableProperty] private bool _showInput;
    [ObservableProperty] private string _title = string.Empty;

    public DialogResult Result { get; set; } = DialogResult.None;

    // Button visibility
    public bool ShowOkButton => Buttons is DialogButton.OK or DialogButton.OKCancel;
    public bool ShowCancelButton => Buttons is DialogButton.OKCancel or DialogButton.YesNoCancel;
    public bool ShowYesButton => Buttons is DialogButton.YesNo or DialogButton.YesNoCancel;
    public bool ShowNoButton => Buttons is DialogButton.YesNo or DialogButton.YesNoCancel;

    // Icon properties
    public string Icon => DialogType switch
    {
        DialogType.Success => "✓",
        DialogType.Warning => "⚠",
        DialogType.Error => "✕",
        DialogType.Question => "?",
        DialogType.Confirmation => "?",
        _ => "ℹ"
    };

    public Brush AccentColor => DialogType switch
    {
        DialogType.Success => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF4CAF50")!),
        DialogType.Warning => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFF9800")!),
        DialogType.Error => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFF44336")!),
        DialogType.Question => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF2196F3")!),
        DialogType.Confirmation => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFFF9800")!),
        _ => new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FF00BCD4")!)
    };
}