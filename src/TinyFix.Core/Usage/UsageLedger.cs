// The record of every model request: one JSON line per attempt in usage.jsonl. Append-only so a crash never damages
// older lines, and a damaged line is skipped on read.

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TinyFix.Core.Usage;

/// <summary>Stores and reads <see cref="LlmAttempt"/>s.</summary>
public sealed class UsageLedger(string directory)
{
    /// <summary>File name inside the data folder.</summary>
    public const string FileName = "usage.jsonl";

    private readonly Lock _gate = new();

    /// <summary>Full path of the ledger file.</summary>
    public string Path { get; } = System.IO.Path.Combine(directory, FileName);

    /// <summary>Raised after the ledger changed (on the thread that made the request).</summary>
    public event EventHandler? Recorded;

    /// <summary>Adds one attempt. Never throws: a full disk must not break a request.</summary>
    public void Record(LlmAttempt attempt)
    {
        var line = JsonSerializer.Serialize(new LedgerLine(
            attempt.At.ToString("O", CultureInfo.InvariantCulture), attempt.Protocol, attempt.Model, attempt.Role.ToString(),
            attempt.Success, attempt.Error, attempt.LatencyMs, attempt.InputTokens, attempt.OutputTokens, attempt.Cost),
            LedgerContext.Default.LedgerLine);
        try
        {
            lock (_gate)
            {
                Directory.CreateDirectory(directory);
                File.AppendAllText(Path, line + "\n");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }
        Recorded?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Every stored attempt, oldest first.</summary>
    public IReadOnlyList<LlmAttempt> ReadAll()
    {
        var list = new List<LlmAttempt>();
        string[] lines;
        try
        {
            lock (_gate)
                lines = File.Exists(Path) ? File.ReadAllLines(Path) : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return list;
        }
        foreach (var text in lines)
        {
            if (text.Length == 0)
                continue;
            try
            {
                var line = JsonSerializer.Deserialize(text, LedgerContext.Default.LedgerLine);
                if (line is not null && DateTimeOffset.TryParse(line.At, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at))
                {
                    list.Add(new LlmAttempt(at, line.Protocol, line.Model, line.Role.Length > 0 ? line.Role[0] : 'M', line.Ok, line.Error,
                        line.Ms, line.In, line.Out, line.Cost));
                }
            }
            catch (JsonException)
            {
                // A half-written last line after a crash: skip it.
            }
        }
        return list;
    }

    /// <summary>Forgets everything.</summary>
    public void Clear()
    {
        try
        {
            lock (_gate)
            {
                if (File.Exists(Path))
                    File.Delete(Path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }
        Recorded?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>The stored shape of one attempt (short names keep the file small).</summary>
internal sealed record LedgerLine(string At, string Protocol, string Model, string Role, bool Ok, string Error, int Ms, long In, long Out, decimal? Cost);

[JsonSerializable(typeof(LedgerLine))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class LedgerContext : JsonSerializerContext;
