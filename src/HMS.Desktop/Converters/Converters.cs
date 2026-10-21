using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace HMS.Desktop.Converters;

/// <summary>Visible when the value is not null (or a non-empty string). Pass "invert" to reverse.</summary>
public sealed class NotNullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var present = value is string s ? !string.IsNullOrWhiteSpace(s) : value is not null;
        if (parameter as string == "invert") present = !present;
        return present ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Shows a DateOnly as a short date and accepts edits from a DatePicker's DateTime.</summary>
public sealed class DateOnlyConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is DateOnly d ? d.ToString("dd MMM yyyy", culture) : value;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is DateTime dt ? DateOnly.FromDateTime(dt) : null;
}
