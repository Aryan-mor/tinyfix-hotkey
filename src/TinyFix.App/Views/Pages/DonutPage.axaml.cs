// Code-behind of the Donut page (all logic lives in the view model).

using Avalonia.Controls;

namespace TinyFix.App.Views.Pages;

/// <summary>Donut settings: shortcut, size, opacity, placement and preview.</summary>
public partial class DonutPage : UserControl
{
    /// <summary>Creates the page.</summary>
    public DonutPage() => InitializeComponent();
}
