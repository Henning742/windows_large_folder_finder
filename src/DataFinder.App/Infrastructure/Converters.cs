using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace DataFinder.App.Infrastructure;

/// <summary>Indents a tree row by its depth, so a nested folder reads as nested.</summary>
public sealed class DepthToIndentConverter : IValueConverter
{
    private const double IndentPerLevel = 14d;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        new Thickness((value is int depth ? depth : 0) * IndentPerLevel, 0, 0, 0);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Hides an element without giving up its space, which keeps the expander buttons of a tree lined
/// up whether a row has children or not.
/// </summary>
public sealed class BoolToHiddenVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Visible : Visibility.Hidden;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
