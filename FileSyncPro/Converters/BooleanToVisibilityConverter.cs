using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace FileSyncPro.Converters
{
    public class BooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // Handle string comparison for visibility based on destination type
            if (value is string destinationType && parameter is string expectedType)
            {
                return destinationType == expectedType ? Visibility.Visible : Visibility.Collapsed;
            }
            
            // Handle boolean value with optional "inverse" parameter
            if (value is bool boolValue)
            {
                if (parameter?.ToString() == "inverse")
                {
                    return boolValue ? Visibility.Collapsed : Visibility.Visible;
                }
                return boolValue ? Visibility.Visible : Visibility.Collapsed;
            }

            // Handle integer value comparison with string parameter
            if (value is int intValue && parameter is string paramStr && int.TryParse(paramStr, out int paramValue))
            {
                return intValue == paramValue ? Visibility.Visible : Visibility.Collapsed;
            }

            return Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}