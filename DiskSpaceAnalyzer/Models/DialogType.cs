namespace DiskSpaceAnalyzer.Models;

public enum DialogType
{
    Information,
    Success,
    Warning,
    Error,
    Confirmation,
    Question
}

public enum DialogButton
{
    OK,
    OKCancel,
    YesNo,
    YesNoCancel
}

public enum DialogResult
{
    None,
    OK,
    Cancel,
    Yes,
    No
}