// The API-key field: empty, saved, replacing, and confirming removal. The view model never reads a key value.

using Avalonia.Headless.XUnit;
using TinyFix.App.Services;
using TinyFix.App.ViewModels;
using TinyFix.Core.Llm;
using TinyFix.Core.Settings;

namespace TinyFix.App.Tests;

public class KeyFieldTests
{
    [AvaloniaFact]
    public void Key_states_never_read_the_secret_and_update_usability()
    {
        const string secret = "test-key-123";
        var s = TestServices.EnsureInitialized("en");
        TestServices.Ports.Keys.Clear();
        TestServices.Ports.ResetSecretReads();
        using var page = new OnlinePageViewModel(s);
        Assert.Equal(KeyFieldState.None, page.Main.State);
        Assert.True(page.Main.ShowInput);
        Assert.False(page.Main.IsSaved);
        Assert.Equal('*', page.Main.PasswordChar);
        Assert.Equal(s.Loc["models.show_key"], page.Main.RevealName);
        Assert.Equal(0, TestServices.Ports.SecretReads);
        Assert.False(Ready(s));

        page.Main.ToggleRevealCommand.Execute(null);
        Assert.Equal('\0', page.Main.PasswordChar);
        Assert.Equal(s.Loc["models.hide_key"], page.Main.RevealName);
        page.Main.ApiKey = "  " + secret + "  ";
        page.SaveCommand.Execute(null);
        Assert.Equal(KeyFieldState.Saved, page.Main.State);
        Assert.False(page.Main.ShowInput);
        Assert.True(page.Main.IsSaved);
        Assert.Equal(EndpointEditorViewModel.SavedKeyMask, page.Main.MaskedKey);
        Assert.Equal(12, page.Main.MaskedKey.Length);
        Assert.Equal("", page.Main.ApiKey);
        Assert.Equal(secret, TestServices.Ports.Keys[LlmSecrets.MainSlot]);
        Assert.Equal(secret, TestServices.Ports.Keys[LlmProtocols.Google]);
        Assert.True(Ready(s));
        Assert.DoesNotContain(secret, File.ReadAllText(s.Store.SettingsPath));
        Assert.DoesNotContain("llm_enabled", File.ReadAllText(s.Store.SettingsPath));

        TestServices.Ports.Set(LlmSecrets.MainSlot, "ab");
        TestServices.Ports.Set(LlmProtocols.Google, "ab");
        page.Main.Load(s.Store.Settings.Llm.Main, use: true);
        TestServices.Ports.Set(LlmSecrets.MainSlot, new string('z', 40));
        TestServices.Ports.Set(LlmProtocols.Google, new string('z', 40));
        page.Main.Load(s.Store.Settings.Llm.Main, use: true);
        Assert.Equal(EndpointEditorViewModel.SavedKeyMask, page.Main.MaskedKey);
        Assert.Equal("", page.Main.ApiKey);

        page.Main.BeginReplaceCommand.Execute(null);
        page.Main.ApiKey = "draft-not-saved";
        page.Main.ProtocolIndex = 1;
        Assert.Equal("", page.Main.ApiKey);
        Assert.Equal(KeyFieldState.Saved, page.Main.State);
        Assert.Equal(new string('z', 40), TestServices.Ports.Keys[LlmSecrets.MainSlot]);
        Assert.Equal(new string('z', 40), TestServices.Ports.Keys[LlmProtocols.Google]);
        Assert.False(TestServices.Ports.Contains(LlmProtocols.OpenRouter));
        page.Main.ProtocolIndex = 0;
        Assert.Equal(KeyFieldState.Saved, page.Main.State);

        page.Main.BeginReplaceCommand.Execute(null);
        Assert.True(page.Main.IsReplacing);
        page.Main.ApiKey = "   ";
        page.Main.SaveReplacementCommand.Execute(null);
        Assert.Equal(KeyFieldState.Replacing, page.Main.State);
        Assert.Equal(new string('z', 40), TestServices.Ports.Keys[LlmSecrets.MainSlot]);
        page.Main.CancelKeyEditCommand.Execute(null);
        Assert.Equal(KeyFieldState.Saved, page.Main.State);
        Assert.Equal("", page.Main.ApiKey);

        page.Main.BeginReplaceCommand.Execute(null);
        page.Main.ApiKey = "  replaced-key  ";
        page.Main.SaveReplacementCommand.Execute(null);
        Assert.Equal(KeyFieldState.Saved, page.Main.State);
        Assert.Equal("replaced-key", TestServices.Ports.Keys[LlmSecrets.MainSlot]);
        Assert.Equal("replaced-key", TestServices.Ports.Keys[LlmProtocols.Google]);
        Assert.True(Ready(s));
        Assert.DoesNotContain("replaced-key", page.Main.Status);
        Assert.DoesNotContain("replaced-key", page.Main.ApiKey);

        page.Main.BeginRemoveCommand.Execute(null);
        Assert.Equal(KeyFieldState.ConfirmingRemove, page.Main.State);
        Assert.True(page.Main.IsConfirmingRemove);
        page.Main.CancelKeyEditCommand.Execute(null);
        Assert.Equal(KeyFieldState.Saved, page.Main.State);
        Assert.True(TestServices.Ports.Contains(LlmSecrets.MainSlot));

        page.Main.BeginRemoveCommand.Execute(null);
        page.Main.ConfirmRemoveCommand.Execute(null);
        Assert.Equal(KeyFieldState.None, page.Main.State);
        Assert.True(page.Main.ShowInput);
        Assert.False(TestServices.Ports.Contains(LlmSecrets.MainSlot));
        Assert.False(TestServices.Ports.Contains(LlmProtocols.Google));
        Assert.False(Ready(s));
        Assert.Equal(0, TestServices.Ports.SecretReads);
        Assert.DoesNotContain(secret, page.Main.Status + page.Status + page.Main.MaskedKey);
        TestServices.Ports.Keys.Clear();
    }

    [AvaloniaFact]
    public void A_skill_override_does_not_show_or_delete_the_provider_key()
    {
        const string providerKey = "test-key-123";
        var s = TestServices.EnsureInitialized("en");
        TestServices.Ports.Keys.Clear();
        TestServices.Ports.ResetSecretReads();
        TestServices.Ports.Set(LlmProtocols.Google, providerKey);
        using var editor = new EndpointEditorViewModel(s, LlmSecrets.SkillMain("fix"), "models.main", "models.fallback_enable", allowSkip: true);
        editor.Load(new LlmEndpointSettings { Protocol = LlmProtocols.Google, Model = "m" }, use: true);
        Assert.Equal(KeyFieldState.None, editor.State);
        Assert.Equal("", editor.ApiKey);
        TestServices.Ports.Set(editor.Slot, "slot-key");
        editor.Load(new LlmEndpointSettings { Protocol = LlmProtocols.Google, Model = "m" }, use: true);
        Assert.Equal(KeyFieldState.Saved, editor.State);
        Assert.Equal(EndpointEditorViewModel.SavedKeyMask, editor.MaskedKey);
        Assert.DoesNotContain("slot-key", editor.MaskedKey);
        editor.BeginRemoveCommand.Execute(null);
        editor.ConfirmRemoveCommand.Execute(null);
        Assert.Equal(KeyFieldState.None, editor.State);
        Assert.False(TestServices.Ports.Contains(editor.Slot));
        Assert.Equal(providerKey, TestServices.Ports.Keys[LlmProtocols.Google]);
        Assert.Equal(0, TestServices.Ports.SecretReads);
        TestServices.Ports.Keys.Clear();
    }

    private static bool Ready(AppServices s) => s.Store.Settings.ToLlmConfig(TestServices.Ports.Keys).Ready;
}
