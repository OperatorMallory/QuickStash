using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace QuickStash.Converters;

/// <summary>Visible when the value is non-null (and not an empty string); Collapsed otherwise. Parameter "invert" flips it.</summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool hasValue = value is not null && !(value is string s && s.Length == 0);
        if (parameter as string == "invert") hasValue = !hasValue;
        return hasValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>bool → Visibility. Parameter "invert" flips it.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool flag = value is true;
        if (parameter as string == "invert") flag = !flag;
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Visibility.Visible ^ (parameter as string == "invert");
}

/// <summary>Formats a UTC timestamp compactly in local time: "14:32" today, "Yesterday 14:32", "Mar 3 14:32", "Mar 3 2024".</summary>
public sealed class RelativeTimeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not DateTime utc) return string.Empty;
        return Format(utc.ToLocalTime(), DateTime.Now);
    }

    public static string Format(DateTime local, DateTime now)
    {
        if (local.Date == now.Date) return local.ToString("HH:mm", CultureInfo.CurrentCulture);
        if (local.Date == now.Date.AddDays(-1)) return "Yesterday " + local.ToString("HH:mm", CultureInfo.CurrentCulture);
        if (local.Year == now.Year) return local.ToString("MMM d HH:mm", CultureInfo.CurrentCulture);
        return local.ToString("MMM d yyyy", CultureInfo.CurrentCulture);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Visible when the value is the number 0 (empty-state placeholders).</summary>
public sealed class ZeroToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}
