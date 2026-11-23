// A donut opened while another app is in front can lose focus once as it appears, before the user did anything:
// the click-mode donut closes when it loses focus, so that one loss would close it at once. The first focus loss
// within a moment of opening is forgiven instead. Adapted from the MIT helper credited in THIRD-PARTY-NOTICES.md.

namespace TinyFix.App.Services;

/// <summary>Forgives one focus loss in the moment after the donut opens.</summary>
internal sealed class ActivationGrace(TimeSpan length, Func<DateTime> now)
{
    /// <summary>Long enough for an activation to settle; short enough that a quick click elsewhere still closes.</summary>
    public static readonly TimeSpan Default = TimeSpan.FromSeconds(1);

    private DateTime? _openedAt;

    public ActivationGrace() : this(Default, () => DateTime.UtcNow) { }

    /// <summary>The donut just opened: forgive the next focus loss within the grace.</summary>
    public void Start() => _openedAt = now();

    /// <summary>The donut opened for something else, or closed: focus losses close it again.</summary>
    public void Cancel() => _openedAt = null;

    /// <summary>
    /// The donut lost focus: true when that is forgiven (once, and only within the grace after <see cref="Start"/>).
    /// </summary>
    public bool Forgives()
    {
        if (_openedAt is not { } openedAt)
            return false;
        _openedAt = null;
        return now() - openedAt <= length;
    }
}
