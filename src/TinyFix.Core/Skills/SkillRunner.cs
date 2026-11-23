// Runs one skill end to end: capture the selection, send it through the online model, paste the result back.
// Ported from app.py (run_skill, on_fix_clipboard) minus the offline engine. One job at a time; failures leave the
// user's text unchanged and restore the clipboard.

using Microsoft.Extensions.Logging;
using TinyFix.Core.Engine;
using TinyFix.Core.Llm;
using TinyFix.Core.Platform;
using TinyFix.Core.Selection;
using TinyFix.Core.Settings;

namespace TinyFix.Core.Skills;

/// <summary>How serious a status message is.</summary>
public enum StatusKind
{
    Info,
    Failure,
}

/// <summary>Where the runner reports what it is doing. Messages are localization keys, never finished sentences.</summary>
public interface IStatusSink
{
    /// <summary>Shows a short message (a toast or tray notice). <paramref name="key"/> is a key of the i18n files.</summary>
    void Show(StatusKind kind, string key, params object[] args);

    /// <summary>Shows or clears the "working" state of the tray icon; <paramref name="letter"/> tells which model answered.</summary>
    void SetBusy(bool busy, char letter = '\0');
}

/// <summary>Orchestrates the skills.</summary>
public sealed class SkillRunner(
    UserDataStore store,
    LlmClient llm,
    ISecretStore secrets,
    SelectionService selection,
    PlatformServices platform,
    IStatusSink status,
    ILogger logger)
{
    private readonly SemaphoreSlim _busy = new(1, 1);

    /// <summary>The online settings with the saved keys filled in.</summary>
    public LlmConfig CurrentLlmConfig()
    {
        var settings = store.Settings;
        var keys = secrets.GetAll();
        string Key(string provider) => keys.TryGetValue(provider, out var k) ? k : "";
        var fallbackProvider = string.IsNullOrWhiteSpace(settings.LlmFallbackProvider) ? settings.LlmProvider : settings.LlmFallbackProvider;
        return settings.ToLlmConfig(Key(settings.LlmProvider), Key(fallbackProvider));
    }

    /// <summary>True when an online model is switched on and has a key.</summary>
    public bool AiReady => CurrentLlmConfig().Ready;

    private LlmConfig ConfigFor(Skill skill) =>
        LlmConfigResolver.ForSkill(CurrentLlmConfig(), skill.ModelMode, skill.ModelChoice(), secrets.GetAll());

    private IFixEngine EngineFor(Skill skill, LlmConfig config) =>
        new OnlineFixEngine(llm, () => config, skill.Prompt, store.Settings.LlmTargetLanguage);

    /// <summary>Runs <paramref name="skill"/> on the text of the window <paramref name="targetWindow"/> (a token from <see cref="IForegroundWindow.Save"/>).</summary>
    public async Task RunAsync(Skill skill, string? targetWindow, CancellationToken ct = default)
    {
        if (store.Settings.Paused)
        {
            logger.LogInformation("Skill ignored: paused ({Skill})", skill.Name);
            return;
        }
        var config = ConfigFor(skill);
        if (!config.Ready)
        {
            status.Show(StatusKind.Info, "status.ai_off");
            return;
        }
        if (!await _busy.WaitAsync(0, ct).ConfigureAwait(false))
        {
            logger.LogInformation("Skill ignored: busy ({Skill})", skill.Name);
            return;
        }
        status.SetBusy(true);
        var letter = '\0';
        try
        {
            letter = await RunLockedAsync(skill, targetWindow, config, ct).ConfigureAwait(false);
        }
        finally
        {
            _busy.Release();
            status.SetBusy(false, letter);
        }
    }

    private async Task<char> RunLockedAsync(Skill skill, string? targetWindow, LlmConfig config, CancellationToken ct)
    {
        var fg = platform.Foreground;
        if (targetWindow is not null)
        {
            fg.Restore(targetWindow);
            await Task.Delay(50, ct).ConfigureAwait(false);
        }
        if (fg.IsTargetElevated())
        {
            status.Show(StatusKind.Info, "status.elevated");
            return '\0';
        }

        var capture = await selection.CaptureAsync(skill.SelectAll, ct).ConfigureAwait(false);
        if (capture.Failure != SelectionFailure.None)
        {
            status.Show(StatusKind.Info, capture.Failure == SelectionFailure.SnapshotFailed ? "status.clipboard_unreadable" : "status.nothing_selected");
            return '\0';
        }
        var selected = capture.Text!;
        status.Show(StatusKind.Info, "status.running", skill.Name);

        FixOutcome outcome;
        try
        {
            outcome = await EngineFor(skill, config).FixAsync(selected, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            selection.RestoreClipboard(capture);
            throw;
        }
        catch (LlmException ex)
        {
            logger.LogInformation("Skill failed; text left unchanged ({Skill}, {Kind})", skill.Name, ex.Kind);
            selection.RestoreClipboard(capture);
            status.Show(StatusKind.Failure, ex is LlmTimeoutException ? "status.timeout" : "status.failed", skill.Name);
            return '\0';
        }
        catch (Exception ex)
        {
            // Never the text or the key in the log: only the exception type.
            logger.LogError("Skill failed ({Skill}): {Type}", skill.Name, ex.GetType().Name);
            selection.RestoreClipboard(capture);
            status.Show(StatusKind.Failure, "status.failed", skill.Name);
            return '\0';
        }

        if (outcome.Text.Length == 0)
        {
            selection.RestoreClipboard(capture);
            status.Show(StatusKind.Failure, "status.failed", skill.Name);
            return '\0';
        }
        if (outcome.Text == selected)
        {
            selection.RestoreClipboard(capture);
            status.Show(StatusKind.Info, "status.no_changes", skill.Name);
            return outcome.ModelLetter;
        }
        if (targetWindow is not null)
        {
            fg.Restore(targetWindow);
            await Task.Delay(20, ct).ConfigureAwait(false);
        }
        if (!await selection.PasteAsync(capture, outcome.Text, ct).ConfigureAwait(false))
            logger.LogInformation("Clipboard write failed; text left unchanged ({Skill})", skill.Name);
        else
            logger.LogInformation("Skill {Skill} changed {Length} character(s)", skill.Name, selected.Length);
        return outcome.ModelLetter;
    }

    /// <summary>Fixes the text on the clipboard in place (tray action, and the way to use TinyFix where global shortcuts do not work).</summary>
    public async Task FixClipboardAsync(CancellationToken ct = default)
    {
        if (store.Settings.Paused)
        {
            status.Show(StatusKind.Info, "status.paused");
            return;
        }
        var config = CurrentLlmConfig();
        if (!config.Ready)
        {
            status.Show(StatusKind.Info, "status.ai_off");
            return;
        }
        if (!await _busy.WaitAsync(0, ct).ConfigureAwait(false))
            return;
        status.SetBusy(true);
        var letter = '\0';
        try
        {
            var clipboard = platform.Clipboard;
            var text = clipboard.GetText();
            if (string.IsNullOrEmpty(text))
            {
                status.Show(StatusKind.Info, "clipboard.empty");
                return;
            }
            var outcome = await new OnlineFixEngine(llm, () => config, config.FixPrompt).FixAsync(text, ct).ConfigureAwait(false);
            letter = outcome.ModelLetter;
            if (outcome.Text == text || outcome.Text.Length == 0)
            {
                status.Show(StatusKind.Info, "clipboard.clean");
                return;
            }
            clipboard.SetText(outcome.Text);
            status.Show(StatusKind.Info, "clipboard.fixed");
        }
        catch (LlmException)
        {
            status.Show(StatusKind.Failure, "clipboard.failed");
        }
        finally
        {
            _busy.Release();
            status.SetBusy(false, letter);
        }
    }
}
