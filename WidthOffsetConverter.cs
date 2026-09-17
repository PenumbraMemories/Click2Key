using System.Globalization;
using System.Windows.Data;

namespace Click2Key.Controls;

public sealed class WidthOffsetConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is double width && double.IsFinite(width)
            ? Math.Max(0, width - (double.TryParse(parameter?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var offset) ? offset : 0))
            : 0d;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
