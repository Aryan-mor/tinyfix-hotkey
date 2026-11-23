// Code-behind of the setup window: step slide, the welcome donut, and Escape / Enter.
// Skip, the close button, and Finish all mark setup complete. Escape does not, while a key is only typed.

using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using TinyFix.App.Controls;
using TinyFix.App.Services;
using TinyFix.App.ViewModels;
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
        Motion.Track(this);
        UiZoom.Track(this);
        FillPreview();
        Opened += (_, _) =>
        {
            if (viewModel.IsWelcome)
                WelcomeDonut.PlayOpenAnimation();
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

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SetupWizardViewModel.Step) || DataContext is not SetupWizardViewModel vm)
            return;
        BodyScroll.Offset = new Vector(0, 0);
        if (vm.IsWelcome)
            WelcomeDonut.PlayOpenAnimation();
        var next = (int)vm.Step;
        var animate = _seen >= 0 && Motion.Enabled;
        var forward = vm.StepDelta >= 0;
        _seen = next;
        if (animate && PanelFor(vm.Step) is { } panel)
            _ = SlideAsync(panel, forward);
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
