using System;
using System.Globalization;
using System.Linq;
using System.Windows.Data;

namespace TraceIntel.UI.Converters
{
    public class JoinConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length == 0)
                return string.Empty;

            string separator = parameter?.ToString() ?? " ";

            return string.Join(separator,
                values.Where(v => v != null)
                      .Select(v => v.ToString()));
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}