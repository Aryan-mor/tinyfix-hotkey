// Code-behind of the setup window: step slide, focus on the model step, and Escape / Enter.
// Skip, the close button, and Finish all mark setup complete. Escape does not, while a key is only typed.
// Enter in the API key box runs the connection test. After it works, Enter follows Continue.

using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TinyFix.App.Controls;
using TinyFix.App.Services;
using TinyFix.App.ViewModels;
using TinyFix.App.Views.Controls;
using TinyFix.Core.Donut;
using TinyFix.Core.Skills;

namespace TinyFix.App.Views;

/// <summary>The first-run setup window.</summary>
public partial class SetupWindow : Window
{
    private int _seen = -1;

    /// <summary>Creates the window over <paramref name="viewModel"/>.</summary>
    public SetupWindow(SetupWizardViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.RequestClose += OnRequestClose;
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
        AddHandler(KeyDownEvent, OnTunnelKeyDown, RoutingStrategies.Tunnel);
        BodyScroll.SizeChanged += (_, _) => FitBody();
        Motion.Track(this);
        UiZoom.Track(this);
        FillPreview();
        Opened += (_, _) =>
        {
            if (viewModel.IsWelcome)
                WelcomeDonut.PlayOpenAnimation();
            FitBody();
            FocusModelEntry();
        };
    }

    /// <summary>For the XAML loader.</summary>
    public SetupWindow()
        : this(new SetupWizardViewModel(AppHost.Services))
    {
    }

    /// <inheritdoc />
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || DataContext is not SetupWizardViewModel vm)
            return;
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            vm.TryDismissFromEscape();
            return;
        }
        if (e.Key != Key.Enter || e.KeyModifiers != KeyModifiers.None)
            return;
        if (FocusManager?.GetFocusedElement() is Button or ShortcutRecorder)
            return;
        if (KeyBoxFocused())
            return;
        if (!vm.CanContinue)
            return;
        e.Handled = true;
        vm.ContinueCommand.Execute(null);
    }

    /// <inheritdoc />
    protected override void OnClosing(WindowClosingEventArgs e)
    {
        if (DataContext is SetupWizardViewModel vm)
            vm.Dismiss();
        base.OnClosing(e);
    }

    /// <inheritdoc />
    protected override void OnClosed(EventArgs e)
    {
        if (DataContext is SetupWizardViewModel vm)
        {
            vm.RequestClose -= OnRequestClose;
            vm.PropertyChanged -= OnViewModelPropertyChanged;
            vm.Dispose();
            DataContext = null;
        }
        base.OnClosed(e);
    }

    private void OnRequestClose(object? sender, EventArgs e) => Close();

    private void OnTunnelKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || e.KeyModifiers != KeyModifiers.None)
            return;
        if (e.Key == Key.Enter)
        {
            if (DataContext is not SetupWizardViewModel vm || !vm.IsModel || !KeyBoxFocused())
                return;
            e.Handled = true;
            if (vm.TestCommand.CanExecute(null))
                vm.TestCommand.Execute(null);
            return;
        }
        if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down)
            MoveProvider(e);
    }

    private void MoveProvider(KeyEventArgs e)
    {
        if (FocusManager?.GetFocusedElement() is not RadioButton current || current.GroupName != "setup-provider")
            return;
        var chips = this.GetVisualDescendants().OfType<RadioButton>()
            .Where(button => button.GroupName == "setup-provider" && button.IsEffectivelyVisible)
            .ToList();
        var index = chips.IndexOf(current);
        if (index < 0 || chips.Count == 0)
            return;
        var forward = e.Key is Key.Right or Key.Down;
        if (FlowDirection == FlowDirection.RightToLeft && e.Key is Key.Left or Key.Right)
            forward = !forward;
        var next = (index + (forward ? 1 : -1) + chips.Count) % chips.Count;
        e.Handled = true;
        chips[next].IsChecked = true;
        chips[next].Focus();
    }

    private bool KeyBoxFocused() =>
        FocusManager?.GetFocusedElement() is TextBox box && box.FindAncestorOfType<ApiKeyField>() is not null;

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (DataContext is not SetupWizardViewModel vm)
            return;
        if (e.PropertyName == nameof(SetupWizardViewModel.TestSucceeded) && vm.TestSucceeded)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (DataContext is SetupWizardViewModel current && current.TestSucceeded && current.IsModel)
                    ContinueButton.Focus();
            }, DispatcherPriority.Input);
            return;
        }
        if (e.PropertyName != nameof(SetupWizardViewModel.Step))
            return;
        BodyScroll.Offset = new Vector(0, 0);
        if (vm.IsWelcome)
            WelcomeDonut.PlayOpenAnimation();
        var next = (int)vm.Step;
        var animate = _seen >= 0 && Motion.Enabled;
        var forward = vm.StepDelta >= 0;
        _seen = next;
        FitBody();
        FocusModelEntry();
        if (animate && PanelFor(vm.Step) is { } panel)
            _ = SlideAsync(panel, forward);
    }

    private void FitBody()
    {
        var height = BodyScroll.Viewport.Height;
        if (double.IsNaN(height) || height < 1)
            height = BodyScroll.Bounds.Height;
        if (height > 1 && Math.Abs(BodyHost.MinHeight - height) > 0.5)
            BodyHost.MinHeight = height;
    }

    private void FocusModelEntry()
    {
        if (DataContext is not SetupWizardViewModel vm || !vm.IsModel)
            return;
        var focusKey = vm.FocusApiKey;
        Dispatcher.UIThread.Post(() =>
        {
            if (!IsVisible || DataContext is not SetupWizardViewModel current || !current.IsModel)
                return;
            UpdateLayout();
            Control target = ModelBox;
            if (focusKey)
            {
                var box = KeyField.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(item => item.IsEffectivelyVisible);
                if (box is not null)
                    target = box;
            }
            target.Focus();
        }, DispatcherPriority.Background);
    }

    private Control? PanelFor(SetupStep step) => step switch
    {
        SetupStep.Welcome => WelcomeStep,
        SetupStep.Model => ModelStep,
        SetupStep.Skills => SkillsStep,
        SetupStep.Done => DoneStep,
        _ => null,
    };

    private static Task SlideAsync(Control panel, bool forward)
    {
        var shift = forward ? 28d : -28d;
        var animation = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(220),
            Easing = new CubicEaseOut(),
            FillMode = FillMode.Forward,
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0),
                    Setters =
                    {
                        new Setter(OpacityProperty, 0d),
                        new Setter(MarginProperty, new Thickness(shift, 8, -shift, -8)),
                    },
                },
                new KeyFrame
                {
                    Cue = new Cue(1),
                    Setters =
                    {
                        new Setter(OpacityProperty, 1d),
                        new Setter(MarginProperty, new Thickness(0)),
                    },
                },
            },
        };
        return animation.RunAsync(panel);
    }

    private void FillPreview()
    {
        WelcomeDonut.Items =
        [
            Slice(SkillIds.Fix),
            Slice(SkillIds.Translate),
            Slice(SkillIds.Rewrite),
            Slice(SkillIds.ImprovePrompt),
        ];
    }

    private static DonutItem Slice(string id) => new()
    {
        Id = id,
        Label = id,
        IconKey = SliceIcons.For(id),
        IsEnabled = true,
        Show = LabelShow.IconOnly,
    };
}
