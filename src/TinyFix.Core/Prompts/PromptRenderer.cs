// Fills prompt templates and cleans model output. Ported from prompts.py.

using System.Text.RegularExpressions;

namespace TinyFix.Core.Prompts;

/// <summary>Template rendering and clean-up of what a model sends back.</summary>
public static partial class PromptRenderer
{
    /// <summary>Placeholder for the selected text.</summary>
    public const string ContentToken = "{{content}}";

    /// <summary>Placeholder for the target language (Translate).</summary>
    public const string LanguageToken = "{{language}}";

    /// <summary>Language Translate writes in until the user picks another.</summary>
    public const string DefaultTargetLanguage = "English";

    [GeneratedRegex("^```[^\\n]*\\n([\\s\\S]*?)\\n```$")]
    private static partial Regex Fence();

    /// <summary>
    /// Replaces <c>{{content}}</c> and (when <paramref name="language"/> is given) <c>{{language}}</c>. A template
    /// without <c>{{content}}</c> gets the text appended, so a hand-written prompt never silently drops the selection.
    /// </summary>
    public static string Render(string? template, string content, string? language = null)
    {
        var text = template ?? "";
        if (language is not null)
            text = text.Replace(LanguageToken, language, StringComparison.Ordinal);
        if (text.Contains(ContentToken, StringComparison.Ordinal))
            return text.Replace(ContentToken, content, StringComparison.Ordinal);
        if (text.Length > 0 && !text.EndsWith('\n'))
            text += "\n";
        if (text.Length > 0)
            text += "\n";
        return text + content;
    }

    /// <summary>The default template for a built-in skill id, or the generic custom prompt.</summary>
    public static string DefaultFor(string? skillId) => (skillId ?? "").Trim() switch
    {
        "fix" or "fix-all" => DefaultPrompts.Fix,
        "translate" => DefaultPrompts.Translate,
        "rewrite" => DefaultPrompts.Rewrite,
        "rewrite-language" => DefaultPrompts.RewriteLanguage,
        "improve-prompt" => DefaultPrompts.ImprovePrompt,
        _ => DefaultCustomPrompt,
    };

    /// <summary>Starting prompt of a new custom skill.</summary>
    public const string DefaultCustomPrompt =
        "Rewrite or transform the text below as instructed. Keep the original language unless you are asked to translate.\n\n" +
        "Do not add a heading, explanation, or commentary. Do not wrap the result in quotation marks or code fences. Return only the result.\n\n" +
        "{{content}}\n";

    /// <summary>Removes a wrapping code fence or a single layer of quotes a model may add.</summary>
    public static string StripModelOutput(string? text)
    {
        if (text is null)
            return "";
        var cleaned = text.Trim();
        if (cleaned.Length == 0)
            return "";
        var fence = Fence().Match(cleaned);
        if (fence.Success)
        {
            cleaned = fence.Groups[1].Value.Trim();
        }
        else if (cleaned.StartsWith("```", StringComparison.Ordinal) && cleaned.EndsWith("```", StringComparison.Ordinal) &&
                 CountOccurrences(cleaned, "```") >= 2)
        {
            var inner = cleaned[3..];
            var newline = inner.IndexOf('\n');
            if (newline != -1)
                inner = inner[(newline + 1)..];
            if (inner.EndsWith("```", StringComparison.Ordinal))
                inner = inner[..^3];
            cleaned = inner.Trim();
        }
        if (cleaned.Length >= 2 && cleaned[0] == cleaned[^1] && cleaned[0] is '"' or '\'')
        {
            // Only unwrap a single quoted block, not dialogue with many quotes.
            var body = cleaned[1..^1];
            if (!body.Contains(cleaned[0]))
                cleaned = body.Trim();
        }
        return cleaned;
    }

    private static int CountOccurrences(string text, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(needle, index, StringComparison.Ordinal)) != -1)
        {
            count++;
            index += needle.Length;
        }
        return count;
    }
}
