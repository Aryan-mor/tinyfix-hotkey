// Opens a web page in the default browser on every OS. Only https links are ever opened.

using System.Diagnostics;

namespace TinyFix.App.Services;

/// <summary>Opens links.</summary>
public static class UrlOpener
{
    /// <summary>Opens <paramref name="url"/>. False when it is not an https link or no browser could be started.</summary>
    public static bool Open(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            return false;
        try
        {
            if (OperatingSystem.IsWindows())
                Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true })?.Dispose();
            else
                Process.Start(new ProcessStartInfo(OperatingSystem.IsMacOS() ? "open" : "xdg-open") { ArgumentList = { uri.AbsoluteUri }, UseShellExecute = false })?.Dispose();
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return false;
        }
    }

    /// <summary>Opens a local text file in an editor. False when none could be started.</summary>
    public static bool OpenFile(string path)
    {
        try
        {
            // The file type may have no default program, so ask for a plain text editor first.
            if (OperatingSystem.IsWindows())
                Process.Start(new ProcessStartInfo("notepad.exe") { ArgumentList = { path }, UseShellExecute = false })?.Dispose();
            else if (OperatingSystem.IsMacOS())
                Process.Start(new ProcessStartInfo("open") { ArgumentList = { "-t", path }, UseShellExecute = false })?.Dispose();
            else
                Process.Start(new ProcessStartInfo("xdg-open") { ArgumentList = { path }, UseShellExecute = false })?.Dispose();
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return false;
        }
    }
}
