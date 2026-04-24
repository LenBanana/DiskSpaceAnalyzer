using System;
using System.Globalization;
using System.Windows.Data;
using DiskSpaceAnalyzer.Models;

namespace DiskSpaceAnalyzer.Converters;

public sealed class ScanEngineDisplayConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value switch
        {
            ScanEngine.Sequential => "Sequential (single-threaded)",
            ScanEngine.Parallel => "⚡ Parallel (multi-threaded)",
            ScanEngine.Mft => "🚀 MFT (raw NTFS — admin)",
            _ => value?.ToString() ?? string.Empty
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Binding.DoNothing;
    }
}