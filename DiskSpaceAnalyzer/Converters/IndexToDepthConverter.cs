using System;
using System.Globalization;
using System.Windows.Data;

namespace DiskSpaceAnalyzer.Converters;

public class IndexToDepthConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int index) return index + 1;

        return 3;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int depth) return Math.Max(0, depth - 1);

        return 2;
    }
}