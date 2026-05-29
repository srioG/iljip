using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Iljip.Converters;

/// <summary>true → Visible, false → Collapsed</summary>
public sealed class BoolToVisibility : IValueConverter
{
    public static readonly BoolToVisibility Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility v && v == Visibility.Visible;
}

/// <summary>true → Collapsed, false → Visible</summary>
public sealed class InverseBoolToVisibility : IValueConverter
{
    public static readonly InverseBoolToVisibility Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility v && v != Visibility.Visible;
}

/// <summary>bool 반전. IsEnabled="{Binding XxxFlag, Converter={x:Static conv:NotBoolConverter.Instance}}"</summary>
public sealed class NotBoolConverter : IValueConverter
{
    public static readonly NotBoolConverter Instance = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is not true;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is not true;
}
