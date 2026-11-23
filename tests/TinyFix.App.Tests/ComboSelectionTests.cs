// The General language combo used to go blank after a choice: the item list was rebuilt, so the selected object
// was no longer in it. These tests pick every language, theme and protocol and read the text the closed combo shows.

using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TinyFix.App.Services;
using TinyFix.App.ViewModels;
using TinyFix.App.Views;
using TinyFix.App.Views.Pages;
using TinyFix.Core.Llm;

namespace TinyFix.App.Tests;

public class ComboSelectionTests
{
    [AvaloniaFact]
    public void Each_language_theme_and_protocol_stays_visible_in_its_combo()
    {
        var s = TestServices.EnsureInitialized("en");
        using var vm = new SettingsViewModel(s);
        vm.Selected = vm.Pages.OfType<GeneralPageViewModel>().Single();
        var window = new SettingsWindow(vm) { Width = 1040, Height = 720 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var general = window.GetVisualDescendants().OfType<GeneralPage>().Single();
            var languageBox = general.FindControl<ComboBox>("LanguageBox")!;
            var themeBox = general.FindControl<ComboBox>("ThemeBox")!;
            Assert.NotNull(languageBox);
            Assert.NotNull(themeBox);
            var page = (GeneralPageViewModel)vm.Selected;

            foreach (var option in page.Languages.ToArray())
            {
                var themeBefore = themeBox.SelectedIndex;
                languageBox.SelectedItem = option;
                Dispatcher.UIThread.RunJobs();
                Assert.Same(option, languageBox.SelectedItem);
                Assert.Equal(option.Name, Shown(languageBox));
                if (option.Code != "auto")
                    Assert.Equal(AppLanguages.All.Single(l => l.Code == option.Code).NativeName, Shown(languageBox));
                Assert.False(string.IsNullOrWhiteSpace(Shown(languageBox)));
                Assert.Equal(themeBefore, themeBox.SelectedIndex);
                Assert.False(string.IsNullOrWhiteSpace(Shown(themeBox)));
            }

            page.SelectedLanguage = page.Languages.First(l => l.Code == "en");
            Dispatcher.UIThread.RunJobs();
            s.Loc.SetLanguage("en");

            for (var index = 0; index < themeBox.ItemCount; index++)
            {
                themeBox.SelectedIndex = index;
                Dispatcher.UIThread.RunJobs();
                Assert.Equal(index, themeBox.SelectedIndex);
                var item = Assert.IsType<ComboBoxItem>(themeBox.Items[index]);
                Assert.False(string.IsNullOrWhiteSpace(item.Content?.ToString()));
                Assert.Equal(item.Content!.ToString(), Shown(themeBox));
            }

            vm.Selected = vm.Pages.OfType<OnlinePageViewModel>().Single();
            Dispatcher.UIThread.RunJobs();
            var protocols = window.GetVisualDescendants().OfType<ComboBox>().Where(c => c.Name == "ProtocolBox").ToList();
            Assert.Equal(2, protocols.Count);
            var keys = new[]
            {
                "models.provider_google", "models.provider_openrouter", "models.provider_openai", "models.provider_anthropic",
                "models.provider_ollama", "models.provider_lmstudio", "models.provider_custom",
            };
            Assert.Equal(LlmProtocols.All.Count, keys.Length);
            foreach (var combo in protocols)
            {
                for (var index = 0; index < keys.Length; index++)
                {
                    combo.SelectedIndex = index;
                    Dispatcher.UIThread.RunJobs();
                    Assert.Equal(index, combo.SelectedIndex);
                    Assert.Equal(s.Loc[keys[index]], Shown(combo));
                }
            }
        }
        finally
        {
            window.Close();
            s.Store.Settings.Language = "en";
            s.Store.Settings.Theme = "system";
            s.Loc.SetLanguage("en");
            s.Theme.Apply("en");
        }
    }

    /// <summary>The text of the closed combo (the selection box), not a row in the drop-down.</summary>
    private static string Shown(ComboBox combo)
    {
        var text = combo.GetVisualDescendants().OfType<TextBlock>()
            .Where(block => !block.GetVisualAncestors().OfType<Popup>().Any())
            .Select(block => block.Text)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        if (!string.IsNullOrWhiteSpace(text))
            return text;
        return combo.SelectionBoxItem switch
        {
            string content => content,
            ComboBoxItem item => item.Content?.ToString() ?? "",
            { } other => other.ToString() ?? "",
            null => "",
        };
    }
}
