// Code-behind of the toast: sets the text, places the window at the bottom-right corner of the primary screen and
// hides it again after a few seconds.

using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace TinyFix.App.Views;

/// <summary>A self-hiding notice.</summary>
public partial class ToastWindow : Window
{
    private readonly DispatcherTimer _hide = new() { Interval = TimeSpan.FromSeconds(3.2) };

    /// <summary>Creates the toast.</summary>
    public ToastWindow()
    {
        InitializeComponent();
        _hide.Tick += (_, _) =>
        {
            _hide.Stop();
            Hide();
        };
    }

    /// <summary>The text on screen.</summary>
    public string Text => Message.Text ?? "";

    private Action? _undo;

    /// <summary>
    /// Shows <paramref name="text"/>; a failure gets a red outline. With <paramref name="undo"/> the toast also has a
    /// button labelled <paramref name="undoLabel"/> and stays for three seconds.
    /// </summary>
    public void Display(string text, bool failure, string? undoLabel = null, Action? undo = null)
    {
        Message.Text = text;
        _undo = undo;
        UndoButton.IsVisible = undo is not null;
        UndoButton.Content = undoLabel;
        _hide.Interval = undo is null ? TimeSpan.FromSeconds(3.2) : TimeSpan.FromSeconds(3);
        Card.BorderBrush = Resource(failure ? "DangerBrush" : "SubtleBorderBrush");
        if (!IsVisible)
            Show();
        Dispatcher.UIThread.Post(Reposition, DispatcherPriority.Loaded);
        _hide.Stop();
        _hide.Start();
    }

    private void OnUndo(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        var undo = _undo;
        _undo = null;
        _hide.Stop();
        Hide();
        undo?.Invoke();
    }

    private Avalonia.Media.IBrush? Resource(string key) =>
        this.TryFindResource(key, ActualThemeVariant, out var value) ? value as Avalonia.Media.IBrush : null;

    private void Reposition()
    {
        var screen = Screens?.Primary;
        if (screen is null)
            return;
        var scale = screen.Scaling;
        var width = (int)(Bounds.Width * scale);
        var height = (int)(Bounds.Height * scale);
        var area = screen.WorkingArea;
        Position = new PixelPoint(area.Right - width - (int)(8 * scale), area.Bottom - height - (int)(8 * scale));
    }
}
