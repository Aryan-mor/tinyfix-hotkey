// Detects which display server the session uses. X11 is supported; Wayland blocks global key hooks and synthetic
// input for ordinary apps, so there TinyFix is best-effort and points users to the "Fix clipboard" tray action.

namespace TinyFix.Platform.Linux;

/// <summary>Display server detection from environment variables.</summary>
public static class LinuxDisplay
{
    /// <summary>True for a Wayland session. <paramref name="env"/> reads an environment variable (injectable for tests).</summary>
    public static bool IsWayland(Func<string, string?> env)
    {
        var type = env("XDG_SESSION_TYPE");
        if (!string.IsNullOrWhiteSpace(type))
            return type.Equals("wayland", StringComparison.OrdinalIgnoreCase);
        // No session type: a Wayland display without an X display means Wayland.
        return !string.IsNullOrWhiteSpace(env("WAYLAND_DISPLAY")) && string.IsNullOrWhiteSpace(env("DISPLAY"));
    }

    /// <summary>True when the running session is Wayland.</summary>
    public static bool IsWayland() => IsWayland(Environment.GetEnvironmentVariable);
}
