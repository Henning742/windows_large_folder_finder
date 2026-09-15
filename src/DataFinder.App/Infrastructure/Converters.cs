using System.Globalization;
using System.Windows;
using System.Windows.Data;
using DataFinder.App.Services;

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

/// <summary>
/// Draws a picture from the bytes it was read as, at the size a thumbnail needs.
///
/// The card asks for its picture only when it comes on screen, so the gallery decodes the few
/// folders being looked at instead of every folder of a long list, and the pictures of the cards
/// that scroll away are thrown away with them.
/// </summary>
public sealed class ThumbnailFromBytesConverter : IValueConverter
{
    private const int ThumbnailPixelWidth = 320;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is byte[] bytes && bytes.Length > 0
            ? PreviewService.ToThumbnail(bytes, ThumbnailPixelWidth)
            : null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
