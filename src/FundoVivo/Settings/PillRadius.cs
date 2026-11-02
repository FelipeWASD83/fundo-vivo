using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace FundoVivo.Settings;

/// <summary>
/// Half the element's real height as its corner radius: a true pill. WPF does not clamp a radius bigger than the
/// shape the way CSS does (a radius of 999 draws stretched ellipses around the control), so it is bound to the height.
/// </summary>
public sealed class PillRadius : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        new CornerRadius(value is double h && h > 0 ? h / 2 : 0);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;

    /// <summary>The same for a border made in code.</summary>
    public static void Apply(Border border) =>
        border.SizeChanged += (_, e) => border.CornerRadius = new CornerRadius(e.NewSize.Height / 2);
}
