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

    /// <summary>Shows <paramref name="text"/>; a failure gets a red outline.</summary>
    public void Display(string text, bool failure)
    {
        Message.Text = text;
        Card.BorderBrush = Resource(failure ? "DangerBrush" : "SubtleBorderBrush");
        if (!IsVisible)
            Show();
        Dispatcher.UIThread.Post(Reposition, DispatcherPriority.Loaded);
        _hide.Stop();
        _hide.Start();
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
