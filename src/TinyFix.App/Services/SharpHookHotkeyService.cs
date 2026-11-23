// Global shortcuts through SharpHook (libuiohook): one low-level keyboard hook for Windows, macOS and X11.
//
// The hook thread does almost nothing: it keeps the state of the modifier keys, compares each key press with the
// registered shortcuts and, on a match, swallows the key (where the OS allows) and hands the work to the thread pool.
// Windows holds every key press until the hook callback returns, so nothing slow may happen here.

using Microsoft.Extensions.Logging;
using SharpHook;
using SharpHook.Data;
using TinyFix.Core.Hotkeys;
using TinyFix.Core.Platform;

namespace TinyFix.App.Services;

/// <summary><see cref="IHotkeyService"/> over a SharpHook <see cref="SimpleGlobalHook"/>.</summary>
public sealed class SharpHookHotkeyService(ILogger log) : IHotkeyService
{
    private readonly Lock _gate = new();
    private readonly HashSet<KeyCode> _down = [];
    private IReadOnlyList<(string Id, KeyCode Key, int Modifiers)> _bindings = [];
    private SimpleGlobalHook? _hook;
    private Thread? _thread;

    /// <inheritdoc />
    public event Action<HotkeyPressedEventArgs>? Pressed;

    /// <inheritdoc />
    public bool Suspended { get; set; }

    /// <inheritdoc />
    public void SetBindings(IReadOnlyList<HotkeyBinding> bindings)
    {
        var mapped = bindings
            .Where(b => !b.Key.IsDisabled && KeyMap.TryGetKeyCode(b.Key.Vk, out _))
            .Select(b => (b.Id, KeyMap.TryGetKeyCode(b.Key.Vk, out var code) ? code : KeyCode.VcUndefined, b.Key.ModifierBits))
            .ToList();
        lock (_gate)
            _bindings = mapped;
    }

    /// <inheritdoc />
    public bool Start()
    {
        if (_hook is not null)
            return true;
        var enabled = new ManualResetEventSlim();
        Exception? failure = null;
        var hook = new SimpleGlobalHook(globalHookProvider: null);
        hook.HookEnabled += (_, _) => enabled.Set();
        hook.KeyPressed += OnKeyPressed;
        hook.KeyReleased += (_, e) =>
        {
            lock (_gate)
                _down.Remove(e.Data.KeyCode);
        };
        var thread = new Thread(() =>
        {
            try
            {
                hook.Run();
            }
            catch (Exception ex)
            {
                failure = ex;
                enabled.Set();
            }
        })
        {
            // Windows holds every key press until this thread has handled it.
            IsBackground = true,
            Name = "TinyFix global input",
            Priority = ThreadPriority.Highest,
        };
        thread.Start();
        // The hook reports "enabled" within a moment; a failure (no Accessibility permission on macOS, no X server)
        // arrives as an exception from Run.
        enabled.Wait(TimeSpan.FromSeconds(3));
        if (failure is not null || !enabled.IsSet)
        {
            log.LogError("Could not start the global keyboard hook ({Type})", failure?.GetType().Name ?? "timeout");
            try
            {
                hook.Dispose();
            }
            catch (HookException)
            {
                // Already stopped.
            }
            return false;
        }
        _hook = hook;
        _thread = thread;
        log.LogInformation("Global keyboard hook started");
        return true;
    }

    private void OnKeyPressed(object? sender, KeyboardHookEventArgs e)
    {
        var code = e.Data.KeyCode;
        string? hit = null;
        lock (_gate)
        {
            // Key repeat sends many presses while the shortcut is held: only the first one counts.
            if (!_down.Add(code) || Suspended)
                return;
            var mask = e.RawEvent.Mask;
            var modifiers = (mask.HasCtrl() ? HotkeyModifiers.Control : 0) | (mask.HasAlt() ? HotkeyModifiers.Alt : 0) |
                            (mask.HasShift() ? HotkeyModifiers.Shift : 0) | (mask.HasMeta() ? HotkeyModifiers.Win : 0);
            foreach (var binding in _bindings)
            {
                if (binding.Key == code && binding.Modifiers == modifiers)
                {
                    hit = binding.Id;
                    break;
                }
            }
        }
        if (hit is null)
            return;
        // Synchronous, on the hook thread, as SharpHook requires; only effective on Windows and macOS.
        e.SuppressEvent = true;
        var id = hit;
        _ = Task.Run(() =>
        {
            try
            {
                Pressed?.Invoke(new HotkeyPressedEventArgs(id));
            }
            catch (Exception ex)
            {
                log.LogError("A shortcut handler failed ({Type})", ex.GetType().Name);
            }
        });
    }

    /// <inheritdoc />
    public void Dispose()
    {
        var hook = _hook;
        _hook = null;
        if (hook is null)
            return;
        try
        {
            // Stop the native hook and let its thread end before disposing.
            if (hook is { IsDisposed: false, IsRunning: true })
                hook.Stop();
            _thread?.Join(TimeSpan.FromSeconds(2));
            hook.Dispose();
        }
        catch (HookException ex)
        {
            log.LogWarning("Could not stop the global keyboard hook ({Result})", ex.Result);
        }
    }
}
