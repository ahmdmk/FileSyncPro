using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using FileSyncPro.Models;

namespace FileSyncPro.Converters
{
    public class LogLevelToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is LogLevel level)
            {
                return level switch
                {
                    LogLevel.SUCCESS => new SolidColorBrush(Colors.Green),
                    LogLevel.WARNING => new SolidColorBrush(Colors.Orange),
                    LogLevel.ERROR => new SolidColorBrush(Colors.Red),
                    LogLevel.DEBUG => new SolidColorBrush(Colors.Gray),
                    _ => new SolidColorBrush(Colors.Black)
                };
            }
            return new SolidColorBrush(Colors.Black);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}