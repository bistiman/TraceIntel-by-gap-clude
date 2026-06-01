using System;
using System.Globalization;
using System.Windows.Data;

namespace TraceIntel.UI.Converters
{
    /// <summary>
    /// Binds a string property to RadioButton.IsChecked using ConverterParameter.
    /// Convert: returns true when value == parameter.
    /// ConvertBack: when checked, returns parameter string; otherwise Binding.DoNothing.
    /// </summary>
    public sealed class RadioStringEqualsConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var current = value?.ToString() ?? string.Empty;
            var target = parameter?.ToString() ?? string.Empty;
            return string.Equals(current, target, StringComparison.OrdinalIgnoreCase);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isChecked && isChecked)
            {
                return parameter?.ToString() ?? string.Empty;
            }

            return System.Windows.Data.Binding.DoNothing;
        }
    }
}

