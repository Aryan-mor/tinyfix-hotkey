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
public sealed record SelectionCapture(string? Text, IClipboardSnapshot? Snapshot, SelectionFailure Failure);

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

        var previous = clipboard.ChangeCount;
        keys.SendCopy();
        if (!await WaitForChangeAsync(previous, sentinel, ct).ConfigureAwait(false))
        {
            previous = clipboard.ChangeCount;
            keys.SendCopy();
            if (!await WaitForChangeAsync(previous, sentinel, ct).ConfigureAwait(false))
            {
                clipboard.Restore(snapshot);
                return new SelectionCapture(null, snapshot, SelectionFailure.CopyFailed);
            }
        }

        var text = clipboard.GetText();
        if (string.IsNullOrEmpty(text) || text == sentinel)
        {
            clipboard.Restore(snapshot);
            return new SelectionCapture(null, snapshot, SelectionFailure.Empty);
        }
        return new SelectionCapture(text, snapshot, SelectionFailure.None);
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

    /// <summary>Puts the original clipboard back.</summary>
    public void RestoreClipboard(SelectionCapture capture) => clipboard.Restore(capture.Snapshot);

    /// <summary>Pastes <paramref name="text"/> over the selection and restores the user's clipboard. False when the clipboard refused.</summary>
    public async Task<bool> PasteAsync(SelectionCapture capture, string text, CancellationToken ct = default)
    {
        try
        {
            clipboard.SetText(text);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            clipboard.Restore(capture.Snapshot);
            return false;
        }
        keys.SendPaste();
        await Task.Delay(PasteSettleDelay, ct).ConfigureAwait(false);
        clipboard.Restore(capture.Snapshot);
        return true;
    }
}
