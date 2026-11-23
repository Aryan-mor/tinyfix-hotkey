// Thin view of the skill donut. Pointer and key events are forwarded to DonutViewModel; opening, placing, restoring
// the previous window and running the skill are RadialController's job. The control itself draws, animates and
// hit-tests. Drag-and-drop and the download badge are gone, because a slice is a skill. The window is adapted from
// the MIT radial menu credited in THIRD-PARTY-NOTICES.md.

using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using TinyFix.App.Controls;
using TinyFix.App.Services;
using TinyFix.App.ViewModels;

namespace TinyFix.App.Views;

/// <summary>The window that hosts the donut.</summary>
public partial class RadialWindow : Window
{
    private readonly ActivationGrace _grace = new();
    private bool _activated;
    private DispatcherTimer? _safety;

    /// <summary>Creates the window.</summary>
    public RadialWindow()
    {
        InitializeComponent();
        Motion.Track(this);
        // Halfway through a flip (Tab or the wheel) the donut shows the next page of skills.
        Donut.FaceSwapping += (_, _) => Vm?.Turn(1);
        Donut.SegmentInvoked += (_, index) => Vm?.Choose(index);
        Donut.CenterInvoked += (_, _) => Vm?.Cancel();
        Donut.ActiveIndexChanged += (_, index) =>
        {
            if (Vm is { } vm)
                vm.Hover = index;
        };
        // Tunnel: the donut marks every press handled, so a bubble handler would never see a click in the hole or
        // the corners.
        AddHandler(InputElement.PointerPressedEvent, OnRootPressed, RoutingStrategies.Tunnel);
        Deactivated += OnDeactivated;
        Activated += (_, _) => _activated = true;
    }

    private DonutViewModel? Vm => DataContext as DonutViewModel;

    /// <summary>The control that draws the ring (for tests).</summary>
    public DonutMenu Menu => Donut;

    /// <summary>The control that draws the ring.</summary>
    public DonutMenu DonutControl => Donut;

    /// <summary>Starts a new showing. The window is reused, so the activation state and the safety timer are reset.</summary>
    /// <param name="forgiveFirstFocusLoss">On macOS the first focus loss as the donut opens is forgiven.</param>
    public void BeginShow(bool forgiveFirstFocusLoss)
    {
        _activated = false;
        if (forgiveFirstFocusLoss)
            _grace.Start();
        else
            _grace.Cancel();
        // If the OS never activates the window, a click outside cannot close it, so give up after a while instead of
        // leaving a topmost window behind.
        _safety?.Stop();
        _safety = new DispatcherTimer { Interval = TimeSpan.FromSeconds(25) };
        _safety.Tick += (_, _) => Vm?.Cancel();
        _safety.Start();
    }

    /// <summary>The showing ended (choice or cancel): stop the safety timer.</summary>
    public void EndShow()
    {
        _safety?.Stop();
        _safety = null;
        _grace.Cancel();
    }

    /// <inheritdoc />
    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        Donut.PlayOpenAnimation();
        Donut.Focus();
    }

    /// <inheritdoc />
    protected override void OnClosed(EventArgs e)
    {
        EndShow();
        _activated = false;
        base.OnClosed(e);
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(Avalonia.AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        // Hidden, however that happened: a group waiting to open doesn't open on the hidden donut.
        if (change.Property == IsVisibleProperty && change.NewValue is false)
            Donut.CollapseGroup();
    }

    /// <inheritdoc />
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || Vm is not { } vm)
            return;
        var digit = e.Key switch
        {
            >= Key.D1 and <= Key.D9 => e.Key - Key.D0,
            >= Key.NumPad1 and <= Key.NumPad9 => e.Key - Key.NumPad0,
            _ => 0,
        };
        if (digit != 0)
        {
            vm.ChooseDigit(digit);
            e.Handled = true;
            return;
        }
        if (Donut.HandleKey(e.Key, e.KeyModifiers))
        {
            e.Handled = true;
            return;
        }
        if (e.Key == Key.Escape)
        {
            vm.Cancel();
            e.Handled = true;
        }
    }

    private void OnRootPressed(object? sender, PointerPressedEventArgs e)
    {
        // The corners, the hole and any button other than the left one close the donut. A slice press is left to the
        // control, which invokes the slice when the button comes up.
        var kind = Donut.HitTest(e.GetPosition(Donut)).Kind;
        if (!e.GetCurrentPoint(Root).Properties.IsLeftButtonPressed || kind is DonutHitKind.Outside or DonutHitKind.Center)
            Vm?.Cancel();
    }

    private void OnDeactivated(object? sender, EventArgs e)
    {
        if (!_activated)
            return;
        if (_grace.Forgives())
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (IsVisible && !IsActive)
                    Activate();
            });
            return;
        }
        Vm?.Cancel();
    }
}
