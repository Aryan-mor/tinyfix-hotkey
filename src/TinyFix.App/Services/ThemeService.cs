// Applies the theme variant (system, light, dark) and the soft look's computed resources (accent shades, corner radii,
// the UI font chain, the reading direction) through SoftPalette. Fonts are system fonts only: no font files are bundled.

using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;
using TinyFix.Core.Settings;

namespace TinyFix.App.Services;

/// <summary>Theme and font handling.</summary>
public sealed class ThemeService(UserDataStore store)
{
    /// <summary>The theme variant for a setting ("system", "light", "dark").</summary>
    public static ThemeVariant VariantFor(string setting) => setting switch
    {
        "light" => ThemeVariant.Light,
        "dark" => ThemeVariant.Dark,
        _ => ThemeVariant.Default,
    };

    /// <summary>Applies the stored theme and the font and direction of <paramref name="language"/> to the running application.</summary>
    public void Apply(string language)
    {
        if (Application.Current is not { } app)
            return;
        app.RequestedThemeVariant = VariantFor(store.Settings.Theme);
        var resources = app.Resources;
        SoftPalette.ApplyAccent(resources, Color.Parse(SoftPalette.DefaultAccent));
        SoftPalette.ApplyCornerRadius(resources, SoftPalette.DefaultCornerRadius);
        resources["AppFontFamily"] = SoftPalette.FontFamilyFor(language);
        SoftPalette.ApplyFlowDirection(resources, language);
    }
}
