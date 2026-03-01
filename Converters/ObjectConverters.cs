using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace UnrealPluginsGUI.Converters
{
    public static class ObjectConverters
    {
        public static IValueConverter IsNotNull { get; } = new IsNotNullConverter();
        public static IValueConverter IsEnabledToColor { get; } = new IsEnabledToColorConverter();

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

        private sealed class IsEnabledToColorConverter : IValueConverter
        {
            public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            {
                if (value is bool isEnabled)
                {
                    return isEnabled ? new SolidColorBrush(Colors.LightGreen) : new SolidColorBrush(Colors.LightCoral);
                }
                return new SolidColorBrush(Colors.Gray);
            }

            public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            {
                throw new NotSupportedException();
            }
        }
    }
}
