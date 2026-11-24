// The Cost page: tokens and money per model, and how often the fallback model had to answer. It reads the usage ledger
// (usage.jsonl), which the LLM client fills after every request.

using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TinyFix.App.Services;
using TinyFix.Core.Usage;

namespace TinyFix.App.ViewModels;

/// <summary>One line of the per-model table, already formatted.</summary>
public sealed record CostRow(string Model, string Requests, string Input, string Output, string Cost);

/// <summary>Usage and cost.</summary>
public sealed partial class CostPageViewModel : PageViewModel
{
    private readonly AppServices _s;
    private bool _confirmReset;

    /// <summary>Creates the page.</summary>
    public CostPageViewModel(AppServices services)
        : base(services.Loc, "nav.cost")
    {
        _s = services;
        _s.Usage.Recorded += OnRecorded;
        _resetText = _s.Loc["cost.reset"];
        Track();
        Refresh();
    }

    /// <summary>The per-model lines, most expensive first.</summary>
    public ObservableCollection<CostRow> Rows { get; } = [];

    /// <summary>0 today, 1 the last 30 days, 2 everything.</summary>
    [ObservableProperty]
    private int _periodIndex = 1;

    /// <summary>"$0.0123 · 87 requests".</summary>
    [ObservableProperty]
    private string _totalText = "";

    /// <summary>"12,345 tokens in, 2,345 tokens out".</summary>
    [ObservableProperty]
    private string _tokensText = "";

    /// <summary>Which model answered how often.</summary>
    [ObservableProperty]
    private string _fallbackText = "";

    /// <summary>The reasons the main model failed.</summary>
    [ObservableProperty]
    private string _failuresText = "";

    /// <summary>True when at least one request is in the chosen period.</summary>
    [ObservableProperty]
    private bool _hasRows;

    /// <summary>True when a model without a known price makes the total a minimum.</summary>
    [ObservableProperty]
    private bool _partial;

    /// <summary>The text of the reset button (it asks once more before it deletes).</summary>
    [ObservableProperty]
    private string _resetText;

    partial void OnPeriodIndexChanged(int value) => Refresh();

    private void OnRecorded(object? sender, EventArgs e) => Dispatcher.UIThread.Post(Refresh);

    /// <summary>Reads the ledger and the price list again and fills the page.</summary>
    public void Refresh()
    {
        var pricing = UsagePricing.Load(Path.Combine(_s.DataDirectory, UsagePricing.FileName));
        DateTimeOffset? since = PeriodIndex switch
        {
            0 => new DateTimeOffset(DateTime.Today),
            1 => DateTimeOffset.UtcNow.AddDays(-30),
            _ => null,
        };
        var summary = UsageSummary.Build(_s.Usage.ReadAll(), pricing, since);
        Rows.Clear();
        foreach (var model in summary.Models)
        {
            Rows.Add(new CostRow(
                $"{model.Model}  ({model.Protocol})",
                model.Failures == 0 ? Number(model.Requests) : _s.Loc.Format("cost.requests_failed", model.Requests, model.Failures),
                Number(model.InputTokens), Number(model.OutputTokens), Money(model.Cost, model.CostPartial)));
        }
        HasRows = summary.Requests > 0;
        Partial = summary.CostPartial;
        TotalText = _s.Loc.Format("cost.total", Money(summary.Cost, summary.CostPartial), Number(summary.Requests));
        TokensText = _s.Loc.Format("cost.tokens", Number(summary.InputTokens), Number(summary.OutputTokens));
        FallbackText = _s.Loc.Format("cost.fallback", Number(summary.AnsweredByMain), Number(summary.AnsweredByFallback),
            (summary.FallbackShare * 100).ToString("0", CultureInfo.InvariantCulture) + "%");
        FailuresText = summary.MainFailures.Count == 0
            ? _s.Loc["cost.no_failures"]
            : _s.Loc.Format("cost.failures", string.Join(", ", summary.MainFailures.Select(f => $"{f.Error} ×{f.Count}")));
    }

    private static string Number(long value) => value.ToString("N0", CultureInfo.InvariantCulture);

    private static string Money(decimal dollars, bool partial)
    {
        if (partial && dollars == 0)
            return "–";
        return "$" + dollars.ToString(dollars >= 1 ? "0.00" : "0.0000", CultureInfo.InvariantCulture) + (partial ? "+" : "");
    }

    /// <summary>Deletes the stored usage after a second click.</summary>
    [RelayCommand]
    private void Reset()
    {
        if (!_confirmReset)
        {
            _confirmReset = true;
            ResetText = _s.Loc["cost.reset_confirm"];
            return;
        }
        _confirmReset = false;
        ResetText = _s.Loc["cost.reset"];
        _s.Usage.Clear();
    }

    /// <summary>Opens prices.json (created from the built-in prices when it does not exist yet).</summary>
    [RelayCommand]
    private void EditPrices()
    {
        var path = Path.Combine(_s.DataDirectory, UsagePricing.FileName);
        try
        {
            if (!File.Exists(path))
                TinyFix.Core.IO.AtomicFile.WriteAllText(path, UsagePricing.Template());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }
        UrlOpener.OpenFile(path);
    }

    /// <inheritdoc />
    protected override void OnLanguageChanged(object? sender, EventArgs e)
    {
        base.OnLanguageChanged(sender, e);
        ResetText = _s.Loc[_confirmReset ? "cost.reset_confirm" : "cost.reset"];
        Refresh();
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        _s.Usage.Recorded -= OnRecorded;
        base.Dispose();
    }
}
