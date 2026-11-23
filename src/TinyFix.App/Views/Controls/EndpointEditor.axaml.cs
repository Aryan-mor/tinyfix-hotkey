// Code-behind of the endpoint editor (all logic lives in the view model).

using Avalonia.Controls;

namespace TinyFix.App.Views.Controls;

/// <summary>Edits one main, fallback, or per-skill endpoint.</summary>
public partial class EndpointEditor : UserControl
{
    /// <summary>Creates the control.</summary>
    public EndpointEditor() => InitializeComponent();
}
