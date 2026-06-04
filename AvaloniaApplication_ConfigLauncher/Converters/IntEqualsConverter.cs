using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace AvaloniaApplication_ConfigLauncher.Converters;

public class IntEqualsConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int intValue && parameter is string paramStr && int.TryParse(paramStr, out int target))
            return intValue == target;
        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
