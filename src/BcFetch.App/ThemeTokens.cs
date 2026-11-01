using Avalonia.Controls;
using Avalonia.Media;

namespace BcFetch.App;

internal static class ThemeTokens
{
    /// <summary>
    /// The brush of a theme token (App.axaml) for the theme this control shows now. Tokens live in the light and dark
    /// theme dictionaries, so a plain FindResource cannot be trusted to find them.
    /// </summary>
    public static IBrush Token(this Control control, string key)
        => control.TryFindResource(key, control.ActualThemeVariant, out var value) && value is IBrush brush ? brush : Brushes.Gray;
}
