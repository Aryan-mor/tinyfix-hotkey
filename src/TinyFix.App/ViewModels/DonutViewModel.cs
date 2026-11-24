// State of the skill donut: which skills are on the page, which slice is hovered and what the centre says.
// No UI types here, so it is unit tested without a window.

using CommunityToolkit.Mvvm.ComponentModel;
using TinyFix.App.Controls;
using TinyFix.Core.Donut;
using TinyFix.Core.Localization;
using TinyFix.Core.Settings;
using TinyFix.Core.Skills;

namespace TinyFix.App.ViewModels;

/// <summary>View model of the donut window.</summary>
public sealed partial class DonutViewModel : ObservableObject
{
    private readonly List<(int Index, string Name, string Shortcut, bool Usable, string? Icon, string Reason)> _all;
    private readonly ILocalizer _loc;
    private readonly DonutLook _look;

    /// <summary>
    /// Builds the donut for <paramref name="skills"/> (already filtered to the enabled ones).
    /// <paramref name="usable"/> says whether that skill's model can answer. A skill with its own override is judged on that override.
    /// <paramref name="look"/> is the size, opacity and shortcut hints copied when the donut opens; null keeps today's defaults.
    /// </summary>
    public DonutViewModel(IReadOnlyList<Skill> skills, ILocalizer loc, Func<Skill, bool> usable, DonutLook? look = null)
    {
        _loc = loc;
        _look = look ?? DonutLook.Default;
        _all = skills.Select((s, i) => (i, SkillNames.Display(s, loc), s.HotkeyValue().IsDisabled ? "" : s.DisplayHotkey(), usable(s), SliceIcons.For(s.Id), loc[s.NeedsAi ? "donut.needs_ai" : "donut.needs_layout"])).ToList();
        AiHint = _all.Count > 0 && skills.All(s => s.NeedsAi) && _all.All(item => !item.Usable) ? loc["donut.ai_hint"] : "";
    }

    /// <summary>Raised once: the chosen skill index, or null when the donut was cancelled.</summary>
    public event Action<int?>? Completed;

    /// <summary>Total number of skills.</summary>
    public int Count => _all.Count;

    /// <summary>Zero-based page.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Slices), nameof(Items), nameof(CenterTitle), nameof(CenterSubtitle), nameof(PageText), nameof(FlipHint))]
    private int _page;

    /// <summary>Hovered slice (index within <see cref="Slices"/>), or -1.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CenterTitle), nameof(CenterSubtitle), nameof(CenterNotice))]
    private int _hover = -1;

    /// <summary>Shown in the hub when no skill has a usable model, or empty.</summary>
    public string AiHint { get; }

    /// <summary>Number of pages.</summary>
    public int PageCount => DonutPaging.PageCount(Count);

    /// <summary>"Page 1 of 3: Tab or mouse wheel for more", or empty for a single page.</summary>
    public string PageText => PageCount > 1 ? _loc.Format("donut.hint_pages", Page + 1, PageCount) : "";

    /// <summary>The slices of the current page, as the donut control draws them (the slice id is the skill's index).</summary>
    public IReadOnlyList<DonutItem> Slices
    {
        get
        {
            var (start, count) = DonutPaging.PageRange(Count, Page);
            return Enumerable.Range(0, count)
                .Select(i =>
                {
                    var item = _all[start + i];
                    return new DonutItem
                    {
                        Id = item.Index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        Label = item.Name,
                        FullName = item.Name,
                        Caption = item.Shortcut.Length == 0 ? null : item.Shortcut,
                        IsEnabled = item.Usable,
                        DisabledReason = item.Reason,
                        IconKey = item.Icon,
                    };
                })
                .ToList();
        }
    }

    /// <summary>True when there is more than one page to turn to.</summary>
    public bool CanFlip => PageCount > 1;

    /// <summary>The slices, under the name the donut control binds.</summary>
    public IReadOnlyList<DonutItem> Items => Slices;

    /// <summary>Why a skill is unavailable, shown in the hub when nothing is hovered.</summary>
    public string? CenterNotice => Hover < 0 && AiHint.Length > 0 ? AiHint : null;

    /// <summary>Outer radius of the ring, in device-independent pixels.</summary>
    public double OuterRadius => _look.OuterRadius;

    /// <summary>The hole's radius as a fraction of <see cref="OuterRadius"/>.</summary>
    public double HoleRatio => 0.42;

    /// <summary>How opaque a resting slice is.</summary>
    public double SliceOpacity => _look.Opacity;

    /// <summary>Shortcuts are drawn under the skill names.</summary>
    public bool ShowCaptions => _look.ShowShortcutHints;

    /// <summary>How large the names and icons are drawn (1 is the size they were designed at).</summary>
    public double LabelScale => 1;

    /// <summary>What a slice shows when it has both a name and an icon.</summary>
    public LabelShow DefaultShow => LabelShow.Auto;

    /// <summary>How large a group's ring is. Skills have no groups; the value keeps the control's default.</summary>
    public double GroupRingSize => 1;

    /// <summary>How wide a group's ring spreads.</summary>
    public double GroupRingSpread => 1;

    /// <summary>Room kept for a group's ring. Skills have no groups, so the window stays the ring's own size.</summary>
    public bool ReserveSubRing => false;

    /// <summary>1 while the donut is showing.</summary>
    public double DonutOpacity => 1;

    /// <summary>The hub's paging hint, or null when the donut has one page.</summary>
    public string? FlipHint => PageCount > 1 ? PageText : null;

    /// <summary>The hint shows a mouse instead of a key. The menu has a flip key, so this stays false.</summary>
    public bool FlipHintWheel => false;

    /// <summary>The key that turns the page, as the hook names it.</summary>
    public string FlipKey => "Tab";

    /// <summary>A turn of the mouse wheel turns the page.</summary>
    public bool FlipWithWheel => true;

    /// <summary>Nearly opaque, so a click on a clear pixel still hits the window.</summary>
    public string HitBackground => "#01000000";

    /// <summary>Big text in the middle: the hovered skill, or the hint.</summary>
    public string CenterTitle => Hover >= 0 && Hover < Slices.Count ? Slices[Hover].Label : Count == 0 ? _loc["donut.none"] : _loc["donut.hint"];

    /// <summary>Small text in the middle: the shortcut or why a skill is unavailable, else the paging hint and how to close.</summary>
    public string CenterSubtitle
    {
        get
        {
            if (Hover >= 0 && Hover < Slices.Count)
            {
                var item = Slices[Hover];
                return item.IsEnabled ? item.Caption ?? "" : item.DisabledReason ?? "";
            }
            return PageCount > 1 ? PageText : _loc["donut.close"];
        }
    }

    /// <summary>Turns to another page (wrapping). No-op for a single page.</summary>
    public void Turn(int delta)
    {
        if (PageCount <= 1)
            return;
        Page = DonutPaging.Turn(Page, Count, delta);
        Hover = -1;
    }

    /// <summary>Picks the slice under the number key. False when that number has no slice or the skill is unavailable.</summary>
    public bool ChooseDigit(int digit)
    {
        var overall = DonutPaging.ItemForDigit(digit, Count, Page);
        return overall >= 0 && Choose(overall - DonutPaging.PageRange(Count, Page).Start);
    }

    /// <summary>Picks a slice of the current page. False when it does not exist or is not usable.</summary>
    public bool Choose(int slice)
    {
        if (slice < 0 || slice >= Slices.Count || !Slices[slice].IsEnabled)
            return false;
        Completed?.Invoke(int.Parse(Slices[slice].Id, System.Globalization.CultureInfo.InvariantCulture));
        return true;
    }

    /// <summary>Closes without a choice.</summary>
    public void Cancel() => Completed?.Invoke(null);
}

/// <summary>Display names of skills.</summary>
public static class SkillNames
{
    private static readonly Dictionary<string, string> EnglishDefaults = new()
    {
        [SkillIds.Fix] = "Fix",
        [SkillIds.FixAll] = "Fix all",
        [SkillIds.Translate] = "Translate",
        [SkillIds.Rewrite] = "Rewrite",
        [SkillIds.RewriteLanguage] = "Rewrite to target language",
        [SkillIds.ImprovePrompt] = "Improve prompt",
        [SkillIds.LayoutAnyFa] = "Any language to FA",
        [SkillIds.LayoutSwap] = "Swap two languages",
    };

    /// <summary>True for a built-in that still carries its English default name (the user never renamed it).</summary>
    public static bool IsDefaultName(Skill skill) =>
        skill.Builtin && EnglishDefaults.TryGetValue(skill.Id, out var english) && skill.Name == english;

    /// <summary>
    /// The name shown for a skill: a built-in that still carries its English default name is shown in the UI language
    /// (key <c>skill.&lt;id&gt;</c>); a name the user typed is always shown as typed.
    /// </summary>
    public static string Display(Skill skill, ILocalizer loc) =>
        skill.Builtin && EnglishDefaults.TryGetValue(skill.Id, out var english) && skill.Name == english ? loc["skill." + skill.Id] : skill.Name;
}
