// Gets the selected text out of another app and puts the result back: snapshot the clipboard, simulate copy, wait for
// the clipboard to change, later paste and restore. Ported from app.py (copy_selection / run_skill). The OS specifics
// sit behind IClipboardAccess, IKeySender and IForegroundWindow, so this logic is testable with fakes.

using TinyFix.Core.Platform;

namespace TinyFix.Core.Selection;

/// <summary>Why a capture did not give text.</summary>
public enum SelectionFailure
{
    /// <summary>It worked.</summary>
    None,

    /// <summary>The clipboard could not be read or copied (another app holds it).</summary>
    SnapshotFailed,

    /// <summary>Copy was sent twice and the clipboard never changed.</summary>
    CopyFailed,

    /// <summary>The copy gave no text.</summary>
    Empty,
}

/// <summary>The result of <see cref="SelectionService.CaptureAsync"/>.</summary>
/// <param name="Text">The selected text when <paramref name="Failure"/> is <see cref="SelectionFailure.None"/>.</param>
/// <param name="Snapshot">The user's original clipboard, to put back with <see cref="SelectionService.RestoreClipboard"/>.</param>
/// <param name="Anchor">Where the selection was, to return to it after the model answered. Null when the OS cannot say.</param>
public sealed record SelectionCapture(string? Text, IClipboardSnapshot? Snapshot, SelectionFailure Failure, IFocusAnchor? Anchor = null);

/// <summary>What a paste left behind.</summary>
/// <param name="Pasted">False when the clipboard refused.</param>
/// <param name="After">Where the pasted text sits, for undo. Null when unknown.</param>
public sealed record ReplaceResult(bool Pasted, IFocusAnchor? After);

/// <summary>Reads and replaces the selection of the focused app through the clipboard.</summary>
public sealed class SelectionService(IClipboardAccess clipboard, IKeySender keys, IForegroundWindow foreground)
{
    private static readonly TimeSpan ClipboardChangeTimeout = TimeSpan.FromMilliseconds(400);

    /// <summary>Delay after sending paste before the clipboard is restored; the target app reads it asynchronously.</summary>
    public TimeSpan PasteSettleDelay { get; set; } = TimeSpan.FromMilliseconds(120);

    /// <summary>Copies the selection (or the whole field first) and returns it. The clipboard is restored on failure.</summary>
    public async Task<SelectionCapture> CaptureAsync(bool selectAll, CancellationToken ct = default)
    {
        var snapshot = clipboard.Snapshot();
        if (snapshot is null)
            return new SelectionCapture(null, null, SelectionFailure.SnapshotFailed);

        // Without a change counter a sentinel makes "the clipboard changed" observable by comparing text.
        var sentinel = clipboard.ChangeCount < 0 ? "tinyfix-" + Guid.NewGuid().ToString("N") : null;
        if (sentinel is not null)
            clipboard.SetText(sentinel);

        await foreground.WaitForModifiersReleasedAsync(TimeSpan.FromMilliseconds(500), ct).ConfigureAwait(false);
        if (selectAll)
        {
            keys.SendSelectAll();
            await Task.Delay(50, ct).ConfigureAwait(false);
        }

        var (copied, text) = await TryCopyAsync(sentinel, ct).ConfigureAwait(false);
        if (!copied)
        {
            clipboard.Restore(snapshot);
            return new SelectionCapture(null, snapshot, SelectionFailure.CopyFailed);
        }
        if (string.IsNullOrEmpty(text) || text == sentinel)
        {
            clipboard.Restore(snapshot);
            return new SelectionCapture(null, snapshot, SelectionFailure.Empty);
        }
        // After the copy (and after Ctrl+A), so the anchor holds the range that was really copied.
        return new SelectionCapture(text, snapshot, SelectionFailure.None, foreground.SaveFocus());
    }

    /// <summary>Sends copy (twice at most) and waits for the clipboard to change.</summary>
    private async Task<(bool Copied, string? Text)> TryCopyAsync(string? sentinel, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var previous = clipboard.ChangeCount;
            keys.SendCopy();
            if (await WaitForChangeAsync(previous, sentinel, ct).ConfigureAwait(false))
                return (true, clipboard.GetText());
        }
        return (false, null);
    }

    private async Task<bool> WaitForChangeAsync(long previous, string? sentinel, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + ClipboardChangeTimeout;
        while (DateTime.UtcNow < deadline)
        {
            var changed = sentinel is null ? clipboard.ChangeCount != previous : clipboard.GetText() != sentinel;
            if (changed)
            {
                await Task.Delay(20, ct).ConfigureAwait(false);
                return true;
            }
            await Task.Delay(10, ct).ConfigureAwait(false);
        }
        return false;
    }

    private static string Normalize(string? text) => (text ?? "").Replace("\r\n", "\n", StringComparison.Ordinal);

    /// <summary>
    /// Goes back to where the capture was taken and proves it: the same range is selected again (or, for a whole-field
    /// skill, select-all is sent again) and a copy must give exactly the captured text. False means the user moved on
    /// and pasting now would land in the wrong place. The clipboard is left holding whatever the check copied.
    /// </summary>
    public async Task<bool> ReturnToSelectionAsync(SelectionCapture capture, bool selectAll, string? windowToken, CancellationToken ct = default)
    {
        var exact = false;
        if (capture.Anchor is { } anchor)
            exact = anchor.Restore();
        else if (windowToken is not null)
            foreground.Restore(windowToken);
        await Task.Delay(40, ct).ConfigureAwait(false);
        if (foreground.IsTargetElevated())
            return false;
        if (selectAll && !exact)
        {
            keys.SendSelectAll();
            await Task.Delay(50, ct).ConfigureAwait(false);
        }
        return await SelectionIsAsync(capture.Text, ct).ConfigureAwait(false);
    }

    private async Task<bool> SelectionIsAsync(string? expected, CancellationToken ct)
    {
        var sentinel = clipboard.ChangeCount < 0 ? "tinyfix-" + Guid.NewGuid().ToString("N") : null;
        if (sentinel is not null)
            clipboard.SetText(sentinel);
        var (copied, text) = await TryCopyAsync(sentinel, ct).ConfigureAwait(false);
        return copied && Normalize(text) == Normalize(expected);
    }

    /// <summary>Puts the original clipboard back.</summary>
    public void RestoreClipboard(SelectionCapture capture) => clipboard.Restore(capture.Snapshot);

    /// <summary>Puts <paramref name="text"/> on the clipboard without pasting (the result of a run that could not paste). False when the clipboard refused.</summary>
    public bool Stash(string text)
    {
        try
        {
            clipboard.SetText(text);
            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    /// <summary>Pastes <paramref name="text"/> over the selection and restores the user's clipboard. False when the clipboard refused.</summary>
    public async Task<bool> PasteAsync(SelectionCapture capture, string text, CancellationToken ct = default) =>
        (await ReplaceAsync(capture, text, keepPrevious: false, ct).ConfigureAwait(false)).Pasted;

    /// <summary>
    /// Pastes <paramref name="text"/> over the selection. Afterwards the clipboard holds the replaced text when
    /// <paramref name="keepPrevious"/> is set, else what it held before TinyFix started.
    /// </summary>
    public async Task<ReplaceResult> ReplaceAsync(SelectionCapture capture, string text, bool keepPrevious, CancellationToken ct = default)
    {
        if (!Stash(text))
        {
            clipboard.Restore(capture.Snapshot);
            return new ReplaceResult(false, null);
        }
        keys.SendPaste();
        await Task.Delay(PasteSettleDelay, ct).ConfigureAwait(false);
        var after = capture.Anchor?.AfterPaste();
        if (!keepPrevious || capture.Text is null || !Stash(capture.Text))
            clipboard.Restore(capture.Snapshot);
        return new ReplaceResult(true, after);
    }

    /// <summary>
    /// Puts <paramref name="original"/> back over <paramref name="replaced"/>. With an exact anchor the pasted range is
    /// selected, checked and overwritten; otherwise (or when that check fails) the window is brought back and the app's
    /// own undo is sent. The clipboard is left as it was.
    /// </summary>
    public async Task<bool> UndoAsync(IFocusAnchor? after, string? windowToken, string replaced, string original, CancellationToken ct = default)
    {
        var snapshot = clipboard.Snapshot();
        try
        {
            if (after?.Restore() == true)
            {
                await Task.Delay(40, ct).ConfigureAwait(false);
                if (!foreground.IsTargetElevated() && await SelectionIsAsync(replaced, ct).ConfigureAwait(false) && Stash(original))
                {
                    keys.SendPaste();
                    await Task.Delay(PasteSettleDelay, ct).ConfigureAwait(false);
                    return true;
                }
            }
            if (windowToken is null)
                return false;
            foreground.Restore(windowToken);
            await Task.Delay(60, ct).ConfigureAwait(false);
            keys.SendUndo();
            return true;
        }
        finally
        {
            clipboard.Restore(snapshot);
        }
    }
}
