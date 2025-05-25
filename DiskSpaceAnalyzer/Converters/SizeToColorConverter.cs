using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace DiskSpaceAnalyzer.Converters
{
    public class SizeToColorConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is double percentage)
            {
                return percentage switch
                {
                    >= 50 => new SolidColorBrush(Color.FromRgb(231, 76, 60)), // Red
                    >= 25 => new SolidColorBrush(Color.FromRgb(230, 126, 34)), // Orange
                    >= 10 => new SolidColorBrush(Color.FromRgb(241, 196, 15)), // Yellow
                    >= 5 => new SolidColorBrush(Color.FromRgb(46, 204, 113)), // Green
                    _ => new SolidColorBrush(Color.FromRgb(52, 152, 219)) // Blue
                };
            }

            return new SolidColorBrush(Color.FromRgb(52, 152, 219));
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}