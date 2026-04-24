using System;
using System.Globalization;
using System.Windows.Data;

namespace DiskSpaceAnalyzer.Converters;

public class SortModeToOpacityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values is not [string currentSort, string buttonSort])
            return 0.7;

        return currentSort == buttonSort ? 1.0 : 0.7;
    }
    
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}