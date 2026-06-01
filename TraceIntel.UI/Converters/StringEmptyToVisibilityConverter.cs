using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace TraceIntel.UI.Converters
{
    public sealed class StringEmptyToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var s = value?.ToString();
            return string.IsNullOrWhiteSpace(s) ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}

