// What a million tokens cost. OpenRouter reports the exact cost of each request, so that wins. For other providers the
// built-in list (the public OpenRouter prices, checked 2026-11-24) is used, and prices.json in the data folder adds or
// replaces models. A model with no known price shows tokens only; it is never guessed.

using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using TinyFix.Core.Llm;

namespace TinyFix.Core.Usage;

/// <summary>Dollars per million tokens.</summary>
public readonly record struct TokenPrice(decimal InputPerMillion, decimal OutputPerMillion);

/// <summary>Looks up prices and prices attempts.</summary>
public sealed partial class UsagePricing
{
    /// <summary>The price file name in the data folder.</summary>
    public const string FileName = "prices.json";

    private static readonly Dictionary<string, TokenPrice> BuiltIn = new()
    {
        ["gemini-3-5-flash-lite"] = new(0.30m, 2.50m),
        ["gemini-3-5-flash"] = new(1.50m, 9.00m),
        ["gemini-3-6-flash"] = new(0.75m, 3.75m),
        ["deepseek-v4-1-flash"] = new(0.0507m, 0.2028m),
        ["claude-sonnet-5-5"] = new(2.00m, 10.00m),
        ["claude-haiku-5-5"] = new(0.10m, 0.50m),
        ["claude-opus-5-5"] = new(4.00m, 20.00m),
        ["claude-sonnet-4-5"] = new(3.00m, 15.00m),
        ["gpt-4o-mini"] = new(0.15m, 0.60m),
    };

    private readonly Dictionary<string, TokenPrice> _own = [];

    [GeneratedRegex("-\\d{8}$")]
    private static partial Regex DateSuffix();

    /// <summary>Reads <paramref name="path"/> (a damaged or missing file means no own prices).</summary>
    public static UsagePricing Load(string? path)
    {
        var pricing = new UsagePricing();
        if (path is null || !File.Exists(path))
            return pricing;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return pricing;
            foreach (var model in doc.RootElement.EnumerateObject())
            {
                if (model.Value.ValueKind == JsonValueKind.Object &&
                    model.Value.TryGetProperty("input", out var input) && input.TryGetDecimal(out var inputPrice) &&
                    model.Value.TryGetProperty("output", out var output) && output.TryGetDecimal(out var outputPrice))
                    pricing._own[Key(model.Name)] = new TokenPrice(inputPrice, outputPrice);
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return pricing;
        }
        return pricing;
    }

    /// <summary>The text of a prices.json that shows the format, with the built-in prices as examples.</summary>
    public static string Template()
    {
        var lines = BuiltIn.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair =>
            string.Create(CultureInfo.InvariantCulture,
                $"  \"{pair.Key}\": {{ \"input\": {pair.Value.InputPerMillion}, \"output\": {pair.Value.OutputPerMillion} }}"));
        return "{\n" + string.Join(",\n", lines) + "\n}\n";
    }

    /// <summary>"google/gemini-3.5-flash-lite", "models/Gemini-3.5-Flash-Lite" and "gemini-3-5-flash-lite" are the same model.</summary>
    public static string Key(string model)
    {
        var id = model.Trim().ToLowerInvariant();
        var slash = id.LastIndexOf('/');
        if (slash >= 0)
            id = id[(slash + 1)..];
        var colon = id.IndexOf(':');
        if (colon >= 0)
            id = id[..colon];
        return DateSuffix().Replace(id.Replace('.', '-'), "");
    }

    /// <summary>The price of <paramref name="model"/>, or null when unknown. Local servers are free.</summary>
    public TokenPrice? Find(string protocol, string model)
    {
        if (LlmProtocols.IsLocalPreset(protocol))
            return new TokenPrice(0, 0);
        var key = Key(model);
        if (_own.TryGetValue(key, out var own))
            return own;
        return BuiltIn.TryGetValue(key, out var known) ? known : null;
    }

    /// <summary>Dollars for one attempt: what the provider reported, else tokens times the price, else null (unknown).</summary>
    public decimal? CostOf(LlmAttempt attempt)
    {
        if (attempt.Cost is { } reported)
            return reported;
        if (attempt.InputTokens == 0 && attempt.OutputTokens == 0)
            return 0;
        return Find(attempt.Protocol, attempt.Model) is { } price
            ? (attempt.InputTokens * price.InputPerMillion + attempt.OutputTokens * price.OutputPerMillion) / 1_000_000m
            : null;
    }
}
