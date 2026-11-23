// The API-key field. The view model never reads a saved key back; it only asks whether one is stored.

using Avalonia.Controls;

namespace TinyFix.App.Views.Controls;

/// <summary>Masked API-key entry with the saved, replace, and remove states.</summary>
public partial class ApiKeyField : UserControl
{
    /// <summary>Creates the field.</summary>
    public ApiKeyField() => InitializeComponent();
}
