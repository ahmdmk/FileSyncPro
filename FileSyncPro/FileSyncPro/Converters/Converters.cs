using System;
using System.Globalization;
using System.Windows.Data;

namespace FileSyncPro.Converters
{
    public class IntToBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int intValue && parameter is string paramStr && int.TryParse(paramStr, out int paramValue))
            {
                return intValue == paramValue;
            }
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool boolValue && boolValue)
            {
                // Assuming parameter is always a valid integer string ("0", "1", "2")
                return int.Parse((string)parameter);
            }
            return System.Windows.Data.Binding.DoNothing;
        }
    }

    public class IsTransferringToContentConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isTransferring && parameter is string param)
            {
                var values = param.Split('|');
                return isTransferring ? values[1] : values[0]; // values[0] = Start, values[1] = Cancel
            }
            return "Start Transfer";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class ConnectionTypeToStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int connectionType && parameter is string param)
            {
                var values = param.Split('|');
                if (connectionType >= 0 && connectionType < values.Length)
                {
                    return values[connectionType];
                }
            }
            return "Unknown";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class SourceTypeToStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int sourceType)
            {
                return sourceType switch
                {
                    0 => "SFTP Server",
                    1 => "SharePoint",
                    2 => "Local Folder",
                    _ => "Unknown"
                };
            }
            return "Unknown";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class DestTypeToStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int destType)
            {
                return destType switch
                {
                    0 => "SFTP Server",
                    1 => "SharePoint",
                    2 => "Local Folder",
                    _ => "Unknown"
                };
            }
            return "Unknown";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}