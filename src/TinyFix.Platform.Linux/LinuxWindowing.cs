// Focused window and pointer position on X11 through xdotool. Wayland offers neither to ordinary apps, so there the
// adapters report "unknown" and the app falls back to simple behaviour. UNTESTED on real desktops.

using System.Globalization;
using System.Text.RegularExpressions;
using TinyFix.Core.Platform;

namespace TinyFix.Platform.Linux;

/// <summary><see cref="IForegroundWindow"/> over xdotool.</summary>
public sealed partial class LinuxForeground(bool wayland) : IForegroundWindow
{
    [GeneratedRegex("^[0-9]+$")]
    private static partial Regex WindowId();

    /// <inheritdoc />
    public string? Save()
    {
        if (wayland || !ExternalProcess.Exists("xdotool"))
            return null;
        var result = ExternalProcess.Run("xdotool", ["getactivewindow"]);
        var id = result.Output.Trim();
        return result.Success && WindowId().IsMatch(id) ? id : null;
    }

    /// <inheritdoc />
    public bool Restore(string? token)
    {
        // The token is only digits: validated so nothing else ever reaches the command line.
        if (token is null || !WindowId().IsMatch(token) || wayland)
            return false;
        return ExternalProcess.Run("xdotool", ["windowactivate", "--sync", token]).Success;
    }

    /// <inheritdoc />
    public bool IsTargetElevated() => false;

    /// <inheritdoc />
    /// <remarks>X11 offers no cheap global key state without a hook; a short pause lets a released shortcut settle.</remarks>
    public async Task<bool> WaitForModifiersReleasedAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        await Task.Delay(Math.Min(150, (int)timeout.TotalMilliseconds), ct).ConfigureAwait(false);
        return true;
    }
}

/// <summary><see cref="ICaretLocator"/> that uses the pointer position (X11 has no portable caret query).</summary>
public sealed partial class LinuxCaret(bool wayland) : ICaretLocator
{
    [GeneratedRegex("X=(-?\\d+)\\s+Y=(-?\\d+)")]
    private static partial Regex Location();

    /// <inheritdoc />
    /// <remarks>X11 has no portable caret query, so the caret and the pointer are the same point.</remarks>
    public ScreenPoint GetCaretOrCursor() => GetCursor();

    /// <inheritdoc />
    public ScreenPoint GetCursor()
    {
        if (!wayland && ExternalProcess.Exists("xdotool"))
        {
            var result = ExternalProcess.Run("xdotool", ["getmouselocation", "--shell"]);
            var match = Location().Match(result.Output.Replace('\n', ' '));
            if (result.Success && match.Success)
            {
                return new ScreenPoint(int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
                    int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture));
            }
        }
        return new ScreenPoint(200, 200);
    }
}
