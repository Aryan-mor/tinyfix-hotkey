// Names of prompt variables. {{content}} is reserved for the selected text; {{language}} is built in.

using System.Text.RegularExpressions;

namespace TinyFix.Core.Prompts;

/// <summary>Rules for the names a user can give a prompt variable.</summary>
public static partial class VariableNames
{
    /// <summary>The selected text. A user variable may not use this name.</summary>
    public const string Content = "content";

    /// <summary>The language Translate (and any other prompt) writes in. Always present, and not removable.</summary>
    public const string Language = "language";

    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]*$")]
    private static partial Regex NamePattern();

    /// <summary>True for the reserved name <see cref="Content"/> (case-sensitive).</summary>
    public static bool IsReserved(string name) => name == Content;

    /// <summary>Letters, digits and underscore, not starting with a digit. <see cref="Content"/> fails this on purpose.</summary>
    public static bool IsWellFormed(string name) => !IsReserved(name) && NamePattern().IsMatch(name);

    /// <summary>
    /// Null when <paramref name="name"/> can be saved next to <paramref name="others"/>. Otherwise
    /// <c>reserved</c>, <c>invalid</c> or <c>duplicate</c> (comparison is case-sensitive).
    /// </summary>
    public static string? Check(string name, IEnumerable<string> others)
    {
        if (IsReserved(name))
            return "reserved";
        if (!NamePattern().IsMatch(name))
            return "invalid";
        if (others.Any(other => other == name))
            return "duplicate";
        return null;
    }
}
