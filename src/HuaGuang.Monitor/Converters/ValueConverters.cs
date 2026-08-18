using System.Globalization;

namespace HuaGuang.Monitor.Converters;

public sealed class BoolToStatusColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Color.FromArgb("#3DDC97") : Color.FromArgb("#FF6B6B");

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class BoolToConnectedTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? "已连接" : "未连接";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class IsNotNullOrEmptyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string text && !string.IsNullOrWhiteSpace(text);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
