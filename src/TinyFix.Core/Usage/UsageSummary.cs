// Adds up attempts for the cost page: one row per model, the totals, and how often the fallback had to answer.

namespace TinyFix.Core.Usage;

/// <summary>Totals of one provider and model.</summary>
public sealed record ModelUsage(string Protocol, string Model, int Requests, int Failures, long InputTokens, long OutputTokens,
    decimal Cost, bool CostPartial);

/// <summary>One kind of failure and how often it happened to the main model.</summary>
public sealed record FailureCount(string Error, int Count);

/// <summary>Everything the cost page shows.</summary>
public sealed record UsageSummary(IReadOnlyList<ModelUsage> Models, int Requests, int Failures, long InputTokens, long OutputTokens,
    decimal Cost, bool CostPartial, int AnsweredByMain, int AnsweredByFallback, IReadOnlyList<FailureCount> MainFailures)
{
    /// <summary>Share of answers that came from the fallback, 0 to 1 (0 when nothing answered).</summary>
    public double FallbackShare => AnsweredByMain + AnsweredByFallback == 0 ? 0 : (double)AnsweredByFallback / (AnsweredByMain + AnsweredByFallback);

    /// <summary>Adds up the attempts that finished at or after <paramref name="since"/> (all when null).</summary>
    public static UsageSummary Build(IEnumerable<LlmAttempt> attempts, UsagePricing pricing, DateTimeOffset? since = null)
    {
        var used = attempts.Where(a => since is null || a.At >= since).ToList();
        var models = used
            .GroupBy(a => (a.Protocol, a.Model))
            .Select(group =>
            {
                decimal cost = 0;
                var partial = false;
                foreach (var attempt in group)
                {
                    if (pricing.CostOf(attempt) is { } dollars)
                        cost += dollars;
                    else
                        partial = true;
                }
                return new ModelUsage(group.Key.Protocol, group.Key.Model, group.Count(), group.Count(a => !a.Success),
                    group.Sum(a => a.InputTokens), group.Sum(a => a.OutputTokens), cost, partial);
            })
            .OrderByDescending(m => m.Cost).ThenByDescending(m => m.InputTokens + m.OutputTokens)
            .ToList();
        var failures = used.Where(a => !a.Success && a.Role == 'M')
            .GroupBy(a => a.Error).Select(g => new FailureCount(g.Key, g.Count())).OrderByDescending(f => f.Count).ToList();
        return new UsageSummary(models, used.Count, used.Count(a => !a.Success), used.Sum(a => a.InputTokens), used.Sum(a => a.OutputTokens),
            models.Sum(m => m.Cost), models.Any(m => m.CostPartial),
            used.Count(a => a.Success && a.Role is 'M' or 'L'), used.Count(a => a.Success && a.Role == 'F'), failures);
    }
}
