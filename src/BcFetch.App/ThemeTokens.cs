using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using BcFetch.Settings;

namespace BcFetch.App;

internal static class ThemeTokens
{
    // Custom variants inherit Dark, so the Fluent controls and every token a variant leaves out stay dark.
    public static readonly ThemeVariant Midnight = new("Midnight", ThemeVariant.Dark);
    public static readonly ThemeVariant Mocha = new("Mocha", ThemeVariant.Dark);

    public static ThemeVariant VariantOf(Skin skin) => skin switch
    {
        Skin.Dark => ThemeVariant.Dark,
        Skin.Midnight => Midnight,
        Skin.Mocha => Mocha,
        _ => ThemeVariant.Light,
    };

    /// <summary>
    /// The brush of a theme token (App.axaml) for the theme this control shows now. Tokens live in the light and dark
    /// theme dictionaries, so a plain FindResource cannot be trusted to find them.
    /// </summary>
    public static IBrush Token(this Control control, string key)
        => control.TryFindResource(key, control.ActualThemeVariant, out var value) && value is IBrush brush ? brush : Brushes.Gray;
}
