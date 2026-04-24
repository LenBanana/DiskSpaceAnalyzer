using System;
using System.Globalization;
using System.Windows.Data;

namespace DiskSpaceAnalyzer.Converters;

/// <summary>
///     Converts a ProgressBar's Value, Minimum, Maximum, and ActualWidth to the indicator width.
///     Used for custom progress bar templates to calculate the width of the progress indicator.
/// </summary>
public class ProgressBarWidthConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length != 4 ||
            values[0] is not double value ||
            values[1] is not double minimum ||
            values[2] is not double maximum ||
            values[3] is not double actualWidth)
            return 0.0;

        // Prevent division by zero
        if (maximum <= minimum || actualWidth <= 0) return 0.0;

        // Calculate the percentage and multiply by actual width
        var percentage = (value - minimum) / (maximum - minimum);
        var width = percentage * actualWidth;

        // Ensure width is at least 0 and at most the actual width
        return Math.Max(0, Math.Min(width, actualWidth));
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}