using System;
using System.Globalization;
using System.Windows.Data;

namespace DiskSpaceAnalyzer.Converters
{
    public class FileSizeConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is long bytes)
            {
                return FormatBytes(bytes);
            }
            return "0 B";
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }

        private static string FormatBytes(long bytes)
        {
            const long kb = 1024;
            const long mb = kb * 1024;
            const long gb = mb * 1024;
            const long tb = gb * 1024;

            return bytes switch
            {
                >= tb => $"{bytes / (double)tb:F2} TB",
                >= gb => $"{bytes / (double)gb:F2} GB",
                >= mb => $"{bytes / (double)mb:F2} MB",
                >= kb => $"{bytes / (double)kb:F2} KB",
                _ => $"{bytes} B"
            };
        }
    }
}
