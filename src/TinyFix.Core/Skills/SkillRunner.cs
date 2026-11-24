// Runs one skill end to end: capture the selection, send it through the configured AI model, paste the result back.
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

    /// <summary>Like <see cref="Show"/>, with an Undo button for a few seconds that runs <paramref name="undo"/>. Sinks without buttons just show the message.</summary>
    void ShowUndo(string key, Func<Task> undo, params object[] args) => Show(StatusKind.Info, key, args);

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

    /// <summary>The saved endpoints with the saved keys filled in. A skill override is not applied here.</summary>
    public LlmConfig CurrentLlmConfig() => store.Settings.ToLlmConfig(KeyMap(null));

    /// <summary>True when <paramref name="skill"/> has a model that can answer. An override is judged on its own.</summary>
    public bool IsUsable(Skill skill) => ConfigFor(skill).Ready;

    private LlmConfig ConfigFor(Skill skill) =>
        LlmConfigResolver.ForSkill(CurrentLlmConfig(), skill.ModelMode, skill.ModelChoice(), KeyMap(skill.Id), skill.Id);

    /// <summary>Saved keys plus the slot keys <see cref="ISecretStore.GetAll"/> may not list (per-skill accounts).</summary>
    private Dictionary<string, string> KeyMap(string? skillId)
    {
        var keys = secrets.GetAll().ToDictionary(pair => pair.Key, pair => pair.Value);
        void Take(string slot)
        {
            if (keys.ContainsKey(slot))
                return;
            var value = secrets.Get(slot);
            if (value.Length > 0)
                keys[slot] = value;
        }
        Take(LlmSecrets.MainSlot);
        Take(LlmSecrets.FallbackSlot);
        if (skillId is not null)
        {
            Take(LlmSecrets.SkillMain(skillId));
            Take(LlmSecrets.SkillFallback(skillId));
        }
        return keys;
    }

    private IFixEngine EngineFor(Skill skill, LlmConfig config) =>
        new OnlineFixEngine(llm, () => config, skill.Prompt, store.Settings.Variables);

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
        // The window the text lives in: the donut's saved one, else whatever the shortcut was pressed in.
        var window = targetWindow ?? fg.Save();
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

        // The user may have clicked elsewhere while the model worked: go back to the exact spot and prove the same text
        // is selected there. If not, never paste into whatever has the focus now; hand the result over on the clipboard.
        if (!await selection.ReturnToSelectionAsync(capture, skill.SelectAll, window, ct).ConfigureAwait(false))
        {
            logger.LogInformation("Selection moved while {Skill} ran; result left on the clipboard", skill.Name);
            if (selection.Stash(outcome.Text))
                status.Show(StatusKind.Failure, "status.moved", skill.Name);
            else
            {
                selection.RestoreClipboard(capture);
                status.Show(StatusKind.Failure, "status.failed", skill.Name);
            }
            return outcome.ModelLetter;
        }
        var result = await selection.ReplaceAsync(capture, outcome.Text, store.Settings.KeepPreviousOnClipboard, ct).ConfigureAwait(false);
        if (!result.Pasted)
        {
            logger.LogInformation("Clipboard write failed; text left unchanged ({Skill})", skill.Name);
            return outcome.ModelLetter;
        }
        logger.LogInformation("Skill {Skill} changed {Length} character(s)", skill.Name, selected.Length);
        if (store.Settings.ShowChangeToast)
            status.ShowUndo("status.changed", () => UndoAsync(selected, outcome.Text, result.After, window), skill.Name);
        return outcome.ModelLetter;
    }

    /// <summary>Puts the original text back where <paramref name="replaced"/> was pasted (the Undo button of the "changed" toast).</summary>
    private async Task UndoAsync(string original, string replaced, IFocusAnchor? after, string? window)
    {
        if (!await _busy.WaitAsync(0).ConfigureAwait(false))
            return;
        try
        {
            var ok = await selection.UndoAsync(after, window, replaced, original).ConfigureAwait(false);
            status.Show(ok ? StatusKind.Info : StatusKind.Failure, ok ? "status.undone" : "status.undo_failed");
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            logger.LogError("Undo failed: {Type}", ex.GetType().Name);
            status.Show(StatusKind.Failure, "status.undo_failed");
        }
        finally
        {
            _busy.Release();
        }
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
            var outcome = await new OnlineFixEngine(llm, () => config, config.FixPrompt, store.Settings.Variables).FixAsync(text, ct).ConfigureAwait(false);
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
