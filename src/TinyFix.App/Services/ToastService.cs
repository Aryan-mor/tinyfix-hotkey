// Shows short status messages (a small toast near the screen corner) and drives the tray icon while TinyFix works.
// It is the IStatusSink of the skill runner, so Core never touches a window.

using Avalonia.Threading;
using TinyFix.App.Views;
using TinyFix.Core.Localization;
using TinyFix.Core.Skills;

namespace TinyFix.App.Services;

/// <summary>Status messages and busy indication.</summary>
public sealed class ToastService(ILocalizer loc) : IStatusSink
{
    private ToastWindow? _window;

    /// <summary>The tray (set by the composition root; it owns the busy icon).</summary>
    public TrayService? Tray { get; set; }

    /// <summary>The text of the last message, for tests and the tray tooltip.</summary>
    public string? LastMessage { get; private set; }

    /// <inheritdoc />
    public void Show(StatusKind kind, string key, params object[] args)
    {
        var text = args.Length == 0 ? loc[key] : loc.Format(key, args);
        LastMessage = text;
        Dispatcher.UIThread.Post(() =>
        {
            _window ??= new ToastWindow();
            _window.Display(text, kind == StatusKind.Failure);
        });
    }

    /// <inheritdoc />
    public void SetBusy(bool busy, char letter = '\0') => Dispatcher.UIThread.Post(() => Tray?.SetBusy(busy, letter));
}
