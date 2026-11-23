// Code-behind of the About page: the project link button opens the browser.

using Avalonia.Controls;
using TinyFix.App.ViewModels;

namespace TinyFix.App.Views.Pages;

/// <summary>About and notices.</summary>
public partial class AboutPage : UserControl
{
    /// <summary>Creates the page.</summary>
    public AboutPage()
    {
        InitializeComponent();
        RepositoryButton.Click += (_, _) => (DataContext as AboutPageViewModel)?.OpenRepository();
    }
}
