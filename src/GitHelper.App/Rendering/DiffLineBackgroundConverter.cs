using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using GitHelper.Core.Model;

namespace GitHelper.App.Rendering;

/// <summary>
/// Tints an added or a removed line. A converter rather than a style, because compiled
/// bindings cannot compare an enum to a constant — the same reason ExplainPanelViewModel
/// exposes its state as booleans instead of pushing the comparison into XAML.
///
/// The tints are deliberately translucent, like the #20-alpha grey the explain panel already
/// uses behind code: one pair of colours then reads correctly against both the light and the
/// dark theme, and the view never has to know which is in force.
///
/// The tint is never the only signal. DiffView shows <see cref="DiffLine.Marker"/> — git's
/// own plus and minus — in its own column beside it, because colour alone is no answer to a
/// colour-blind reader, and because the glossary this app ships promises the marker.
/// </summary>
public sealed class DiffLineBackgroundConverter : IValueConverter
{
    /// <summary>One instance, referenced from XAML with {x:Static}.</summary>
    public static DiffLineBackgroundConverter Instance { get; } = new();

    private static readonly IBrush Added =
        new SolidColorBrush(Color.FromArgb(0x28, 0x00, 0xA0, 0x00));

    private static readonly IBrush Removed =
        new SolidColorBrush(Color.FromArgb(0x28, 0xC0, 0x00, 0x00));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            DiffLineKind.Added => Added,
            DiffLineKind.Removed => Removed,

            // Context and the no-newline marker are not changes, so they get no tint.
            _ => Brushes.Transparent,
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("A diff line's tint is derived, never set.");
}
