using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace UnrealPluginsGUI.Converters
{
    public static class ObjectConverters
    {
        public static IValueConverter IsNotNull { get; } = new IsNotNullConverter();

        private sealed class IsNotNullConverter : IValueConverter
        {
            public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            {
                return value != null;
            }

            public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            {
                throw new NotSupportedException();
            }
        }
    }
}
