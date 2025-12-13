using System;
using System.Globalization;
using System.Windows.Data;

namespace DiskSpaceAnalyzer.Converters;

/// <summary>
/// Converts byte values to human-readable file size strings (B, KB, MB, GB, TB).
/// </summary>
public class BytesToSizeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not long bytes)
            return "0 B";
        
        string[] sizes = { "B", "KB", "MB", "GB", "TB" };
        double len = bytes;
        int order = 0;
        
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        
        return $"{len:F2} {sizes[order]}";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
