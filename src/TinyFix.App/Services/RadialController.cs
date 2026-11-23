// Opens, places and closes the skill donut.
//
// The menu shortcut opens it where Settings says (the caret, else the pointer, or always the pointer): the window is
// transparent, topmost and not activated by the mere fact of being shown, then activated so number keys, Tab and Esc
// reach it. A slice is a skill. Picking one restores the window the user was typing in and only then runs the skill.
// Esc, a click in the hole or outside, or a click that deactivates the window cancels. A preview from Settings uses
// the same window and the look saved so far, and a pick only closes it.
// Tab and the mouse wheel flip to the next page of skills, with the donut's flip animation.
//
// Adapted from the MIT radial controller credited in THIRD-PARTY-NOTICES.md. Drag mode, file conversion, progress
// and preset editing are not part of TinyFix: the methods that did that work are gone, and a slice runs a skill.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using TinyFix.App.Controls;
using TinyFix.App.ViewModels;
using TinyFix.App.Views;
using TinyFix.Core.Localization;
using TinyFix.Core.Platform;
using TinyFix.Core.Settings;
using TinyFix.Core.Skills;

namespace TinyFix.App.Services;

/// <summary>Shows the donut and runs the skill that was picked.</summary>
public sealed class RadialController(UserDataStore store, ILocalizer loc, SkillRunner runner, PlatformServices platform, ILogger log)
{
    private RadialWindow? _window;
    private int _open;

    /// <summary>True while a donut is on screen (or being opened).</summary>
    public bool IsOpen => Volatile.Read(ref _open) != 0;

    /// <summary>True while the donut window is showing.</summary>
    public bool IsVisible => _window?.IsVisible == true;

    /// <summary>The donut window once it was created (tests drive it).</summary>
    internal RadialWindow? Window => _window;

    /// <summary>Opens the donut and, when a skill is picked, runs it on the window the user came from.</summary>
    public async Task OpenAsync(CancellationToken ct = default)
    {
        if (store.Settings.Paused)
        {
            log.LogInformation("Skill menu ignored: paused");
            return;
        }
        if (Interlocked.Exchange(ref _open, 1) != 0)
            return;
        try
        {
            var fg = platform.Foreground;
            var target = fg.Save();
            var skills = SkillCatalog.VisibleMenuSkills(store.Skills);
            if (skills.Count == 0)
            {
                AppHost.Services.Toasts.Show(StatusKind.Info, "donut.none");
                return;
            }
            // So the held shortcut keys cannot be read as a key press that dismisses the donut.
            await fg.WaitForModifiersReleasedAsync(TimeSpan.FromMilliseconds(750), ct).ConfigureAwait(false);
            var at = DonutOpenAt.Point(platform.Caret, store.Settings.Donut.OpenAt);
            var choice = await Dispatcher.UIThread.InvokeAsync(() => ShowAsync(skills, at, runner.IsUsable)).ConfigureAwait(false);
            if (choice is { } index)
            {
                // Restoring happens inside RunAsync, right before the keys are sent.
                await runner.RunAsync(skills[index], target, ct).ConfigureAwait(false);
            }
            else
            {
                fg.Restore(target);
                log.LogInformation("Skill menu cancelled");
            }
        }
        finally
        {
            Volatile.Write(ref _open, 0);
        }
    }

    /// <summary>
    /// Opens the donut over <paramref name="at"/> with the current look and closes it on Esc or a pick.
    /// A pick does not run a skill.
    /// </summary>
    public async Task PreviewAsync(ScreenPoint at)
    {
        if (Interlocked.Exchange(ref _open, 1) != 0)
            return;
        try
        {
            var skills = SkillCatalog.VisibleMenuSkills(store.Skills);
            if (skills.Count == 0)
            {
                AppHost.Services.Toasts.Show(StatusKind.Info, "donut.none");
                return;
            }
            await Dispatcher.UIThread.InvokeAsync(() => ShowAsync(skills, at, runner.IsUsable)).ConfigureAwait(false);
        }
        finally
        {
            Volatile.Write(ref _open, 0);
        }
    }

    /// <summary>Shows the donut (UI thread) and completes with the chosen skill index, or null.</summary>
    internal async Task<int?> ShowAsync(IReadOnlyList<Skill> skills, ScreenPoint at, Func<Skill, bool> usable)
    {
        var vm = new DonutViewModel(skills, loc, usable, DonutLook.From(store.Settings.Donut));
        var window = EnsureWindow();
        var done = new TaskCompletionSource<int?>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnCompleted(int? result) => done.TrySetResult(result);
        vm.Completed += OnCompleted;
        window.DataContext = vm;
        // macOS can take the focus back once as the donut opens. Elsewhere a focus loss closes it.
        window.BeginShow(OperatingSystem.IsMacOS());
        window.DonutControl.PlayOpenAnimation();
        PlaceAndShow(window, at.X, at.Y, activate: true);
        try
        {
            return await done.Task.ConfigureAwait(true);
        }
        finally
        {
            vm.Completed -= OnCompleted;
            window.EndShow();
            window.DonutControl.CollapseGroup();
            // Hidden, not closed: the window is reused, so the next opening is instant.
            window.Hide();
        }
    }

    private RadialWindow EnsureWindow()
    {
        if (_window is not null)
            return _window;
        var window = new RadialWindow();
        _window = window;
        return window;
    }

    /// <summary>
    /// Top-left corner of a <paramref name="sidePx"/> square window centred on <paramref name="cursor"/>, moved into
    /// <paramref name="area"/> when that would cross its edge. With group slices the window holds the ring they fan out
    /// (<see cref="DonutMenu.SideFor"/>), so near a screen edge the donut moves in far enough for every ring to fit.
    /// </summary>
    internal static PixelPoint WindowPosition(PixelPoint cursor, PixelRect? area, int sidePx)
    {
        var position = new PixelPoint(cursor.X - sidePx / 2, cursor.Y - sidePx / 2);
        if (area is not { } a)
            return position;
        return new PixelPoint(
            Math.Clamp(position.X, a.X, Math.Max(a.X, a.Right - sidePx)),
            Math.Clamp(position.Y, a.Y, Math.Max(a.Y, a.Bottom - sidePx)));
    }

    private void PlaceAndShow(RadialWindow window, int x, int y, bool activate)
    {
        var cursor = new PixelPoint(x, y);
        var screens = window.Screens;
        var screen = screens?.ScreenFromPoint(cursor) ?? screens?.Primary;
        var scaling = OperatingSystem.IsMacOS() ? 1 : screen?.Scaling ?? 1;

        // Window size follows the donut size (SizeToContent); compute it up front to centre on the caret.
        var vm = (DonutViewModel)window.DataContext!;
        var side = DonutMenu.SideFor(vm.OuterRadius, vm.ReserveSubRing, vm.GroupRingSize);
        var sidePx = (int)Math.Ceiling(side * scaling);
        var position = WindowPosition(cursor, screen?.WorkingArea, sidePx);

        Motion.Refresh();
        window.Position = position;
        if (!window.IsVisible)
            window.Show();
        window.Position = position; // again, in case showing changed the DPI scale
        // Showing a window that is already marked topmost does not move it in the z-order. Put it back on top, and
        // take the keyboard: Activate() alone is refused for a background process on Windows.
        BringToFront(window);
        if (activate)
        {
            window.Activate();
            // ShowActivated is false, so focus the donut explicitly for Tab, arrows, number keys and Enter.
            window.DonutControl.Focus();
        }
    }

    // Activate() alone is refused by Windows for a background process; the platform's restore trick (attaching to the
    // foreground thread's input) lets the donut take the focus so number keys and Esc reach it.
    private void BringToFront(Window window)
    {
        if (window.TryGetPlatformHandle() is { } handle && handle.HandleDescriptor is "HWND" or "XID")
            platform.Foreground.Restore(handle.Handle.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
}
