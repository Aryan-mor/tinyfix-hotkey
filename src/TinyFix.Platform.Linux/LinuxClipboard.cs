// The Linux clipboard through command-line tools: wl-clipboard on Wayland, xclip (or xsel) on X11. Only text is
// snapshotted and restored: other formats cannot be read back portably with these tools. UNTESTED on real desktops.

using TinyFix.Core.Platform;

namespace TinyFix.Platform.Linux;

/// <summary><see cref="IClipboardAccess"/> over xclip, xsel or wl-clipboard.</summary>
public sealed class LinuxClipboard(bool wayland) : IClipboardAccess
{
    private sealed class TextSnapshot(string? text) : IClipboardSnapshot
    {
        public string? Text { get; } = text;

        public void Dispose()
        {
        }
    }

    /// <inheritdoc />
    /// <remarks>These tools have no change counter; the selection service compares text instead.</remarks>
    public long ChangeCount => -1;

    /// <inheritdoc />
    public IClipboardSnapshot? Snapshot() => new TextSnapshot(GetText());

    /// <inheritdoc />
    public void Restore(IClipboardSnapshot? snapshot)
    {
        if (snapshot is not TextSnapshot saved)
            return;
        if (saved.Text is null)
            Clear();
        else
            SetText(saved.Text);
    }

    /// <inheritdoc />
    public string? GetText()
    {
        var result = wayland && ExternalProcess.Exists("wl-paste")
            ? ExternalProcess.Run("wl-paste", ["--no-newline"])
            : ExternalProcess.Exists("xclip")
                ? ExternalProcess.Run("xclip", ["-selection", "clipboard", "-o"])
                : ExternalProcess.Run("xsel", ["--clipboard", "--output"]);
        return result.Success ? result.Output : null;
    }

    /// <inheritdoc />
    /// <exception cref="IOException">No clipboard tool is installed or it failed.</exception>
    public void SetText(string text)
    {
        var result = wayland && ExternalProcess.Exists("wl-copy")
            ? ExternalProcess.Run("wl-copy", [], text)
            : ExternalProcess.Exists("xclip")
                ? ExternalProcess.Run("xclip", ["-selection", "clipboard", "-i"], text)
                : ExternalProcess.Run("xsel", ["--clipboard", "--input"], text);
        if (!result.Success)
            throw new IOException("Could not write the clipboard: install xclip (X11) or wl-clipboard (Wayland).");
    }

    /// <inheritdoc />
    public void Clear()
    {
        if (wayland && ExternalProcess.Exists("wl-copy"))
            ExternalProcess.Run("wl-copy", ["--clear"]);
        else if (ExternalProcess.Exists("xclip"))
            ExternalProcess.Run("xclip", ["-selection", "clipboard", "-i"], "");
        else
            ExternalProcess.Run("xsel", ["--clipboard", "--clear"]);
    }
}
