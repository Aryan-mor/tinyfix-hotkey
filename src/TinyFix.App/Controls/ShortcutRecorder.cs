// A button that records a shortcut: click it, press the combination, done. While it waits for keys the global
// shortcuts are suspended, so pressing an existing shortcut records it instead of running a skill.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using TinyFix.App.Services;
using TinyFix.Core.Hotkeys;

namespace TinyFix.App.Controls;

/// <summary>Records a keyboard shortcut into <see cref="Value"/> (text such as "Ctrl+Alt+T", or "none").</summary>
public sealed class ShortcutRecorder : Button
{
    /// <summary>The shortcut text.</summary>
    public static readonly StyledProperty<string> ValueProperty =
        AvaloniaProperty.Register<ShortcutRecorder, string>(nameof(Value), "none", defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    private bool _recording;
    private bool _wasSuspended;

    static ShortcutRecorder() => ValueProperty.Changed.AddClassHandler<ShortcutRecorder>((r, _) => r.UpdateText());

    /// <summary>Creates the recorder.</summary>
    public ShortcutRecorder()
    {
        HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left;
        MinWidth = 190;
        LostFocus += (_, _) => End();
        if (AppHost.IsBuilt)
            ToolTip.SetTip(this, AppHost.Services.Loc["shortcut.record"]);
        UpdateText();
    }

    /// <summary>The shortcut text.</summary>
    public string Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    // A subclass of Button keeps Button's look: without this Avalonia looks for a theme of the subclass and finds none.
    protected override Type StyleKeyOverride => typeof(Button);

    /// <summary>True while it waits for keys.</summary>
    public bool IsRecording => _recording;

    /// <summary>Raised when a key combination was rejected (no modifier on a plain key).</summary>
    public event EventHandler? Rejected;

    private void UpdateText()
    {
        var loc = AppHost.Services?.Loc;
        if (_recording)
            Content = loc?["shortcut.press"] ?? "Press keys...";
        else
            Content = HotkeyParser.ParseOrDisabled(Value) is { IsDisabled: false } key ? key.Display : loc?["shortcut.none"] ?? "None";
    }

    /// <inheritdoc />
    protected override void OnClick()
    {
        base.OnClick();
        Begin();
    }

    /// <summary>Starts recording (also used by tests).</summary>
    public void Begin()
    {
        if (_recording)
            return;
        _recording = true;
        // Pause the global hook: otherwise pressing an existing shortcut would run its skill instead of being recorded.
        if (AppHost.Services?.Hotkeys is { } hotkeys)
        {
            _wasSuspended = hotkeys.Suspended;
            hotkeys.Suspended = true;
        }
        UpdateText();
        Focus();
    }

    private void End()
    {
        if (!_recording)
            return;
        _recording = false;
        if (AppHost.Services?.Hotkeys is { } hotkeys)
            hotkeys.Suspended = _wasSuspended;
        UpdateText();
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        End();
        base.OnDetachedFromVisualTree(e);
    }

    /// <inheritdoc />
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (!_recording)
        {
            base.OnKeyDown(e);
            return;
        }
        e.Handled = true;
        if (AvaloniaKeys.IsModifier(e.Key))
            return;
        if (e.Key == Key.Escape)
        {
            End();
            return;
        }
        if (e.Key is Key.Back or Key.Delete && e.KeyModifiers == KeyModifiers.None)
        {
            Value = "none";
            End();
            return;
        }
        var vk = AvaloniaKeys.ToVk(e.Key);
        if (vk == 0)
            return;
        var bits = AvaloniaKeys.ToBits(e.KeyModifiers);
        var key = new Hotkey(bits, vk, HotkeyParser.Format(bits, vk));
        if (!key.IsSafe)
        {
            Rejected?.Invoke(this, EventArgs.Empty);
            AppHost.Services?.Toasts.Show(Core.Skills.StatusKind.Info, "shortcut.need_modifier");
            return;
        }
        Value = key.Display;
        End();
    }
}
