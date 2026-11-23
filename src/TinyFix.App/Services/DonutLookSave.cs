// The donut's look as a slider moves (Settings → Donut): the settings in memory change at once, and the donut reads
// them each time it opens; settings.json is written once the slider rests, as the window zoom is (UiZoom).
// Adapted from the MIT helper credited in THIRD-PARTY-NOTICES.md.

using Avalonia.Threading;
using TinyFix.Core.Settings;

namespace TinyFix.App.Services;

/// <summary>Writes the donut's size and opacity once the slider has rested.</summary>
internal static class DonutLookSave
{
    /// <summary>How long a slider rests before its value is written.</summary>
    internal static readonly TimeSpan Delay = TimeSpan.FromMilliseconds(600);

    private static DispatcherTimer? _timer;
    private static UserDataStore? _store;

    /// <summary>A change is waiting to be written.</summary>
    internal static bool IsPending => _timer is { IsEnabled: true };

    /// <summary>
    /// Writes <paramref name="store"/>'s settings once no further change comes for <see cref="Delay"/>.
    /// </summary>
    public static void Schedule(UserDataStore store)
    {
        if (_store is { } other && !ReferenceEquals(other, store))
            Flush(); // another store's change still waiting (tests make several)
        _store = store;
        if (_timer is null)
        {
            _timer = new DispatcherTimer { Interval = Delay };
            _timer.Tick += (_, _) => Flush();
        }
        _timer.Stop();
        _timer.Start();
    }

    /// <summary>Writes a change still waiting for its moment (the page is left, TinyFix exits).</summary>
    public static void Flush()
    {
        if (_timer is not { IsEnabled: true } timer)
            return;
        timer.Stop();
        _store?.SaveSettings();
    }
}
