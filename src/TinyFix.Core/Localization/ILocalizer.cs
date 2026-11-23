// Localization contract. The Avalonia implementation lives in TinyFix.App (LocalizationService).
// Translations are JSON files in src/TinyFix.App/Assets/i18n. See docs/DOTNET-ADDING-A-LANGUAGE.md.

namespace TinyFix.Core.Localization;

/// <summary>Looks up translated UI strings by key.</summary>
public interface ILocalizer
{
    /// <summary>Current language code, e.g. <c>"de"</c>.</summary>
    string Language { get; }

    /// <summary>Returns the translation for <paramref name="key"/>, falling back to English, then to the key itself.</summary>
    string this[string key] { get; }

    /// <summary>Translation with <see cref="string.Format(string, object[])"/> style arguments.</summary>
    string Format(string key, params object[] args);

    /// <summary>Raised after the language changed.</summary>
    event EventHandler? LanguageChanged;
}
