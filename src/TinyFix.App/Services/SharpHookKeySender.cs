// Synthesises Ctrl+C / Ctrl+V / Ctrl+A (Cmd on macOS) through SharpHook's event simulator.

using SharpHook.Data;
using SharpHook.Simulation;
using TinyFix.Core.Platform;

namespace TinyFix.App.Services;

/// <summary><see cref="IKeySender"/> over <see cref="EventSimulator"/>.</summary>
public sealed class SharpHookKeySender : IKeySender
{
    private readonly Lazy<EventSimulator> _lazy = new(() => EventSimulator.Create("TinyFix", null));

    // Created on first use: on Linux creating it opens virtual input devices, which is slow and can fail.
    private EventSimulator Simulator => _lazy.Value;

    // macOS shortcuts use the Command key where Windows and Linux use Control.
    private static KeyCode Modifier => OperatingSystem.IsMacOS() ? KeyCode.VcLeftMeta : KeyCode.VcLeftControl;

    /// <inheritdoc />
    public void SendCopy() => Chord(KeyCode.VcC);

    /// <inheritdoc />
    public void SendPaste() => Chord(KeyCode.VcV);

    /// <inheritdoc />
    public void SendSelectAll() => Chord(KeyCode.VcA);

    /// <inheritdoc />
    public void SendUndo() => Chord(KeyCode.VcZ);

    private void Chord(KeyCode key)
    {
        Simulator.SimulateKeyPress(Modifier);
        Simulator.SimulateKeyPress(key);
        Simulator.SimulateKeyRelease(key);
        // Always released, so Control can never stay stuck down in the target app.
        Simulator.SimulateKeyRelease(Modifier);
    }
}
