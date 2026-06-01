using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace TraceIntel.UI.Converters
{
    public class HopStatusBrushConverter : IValueConverter
    {
        public Brush TimeoutBrush { get; set; } = new SolidColorBrush(Color.FromRgb(74, 74, 90));   // muted
        public Brush OkBrush { get; set; } = new SolidColorBrush(Color.FromRgb(39, 174, 96));      // green

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // value: bool IsTimeout
            if (value is bool isTimeout)
            {
                return isTimeout ? TimeoutBrush : OkBrush;
            }

            return TimeoutBrush;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}

