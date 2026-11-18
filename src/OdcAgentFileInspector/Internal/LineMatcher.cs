using System.Text.RegularExpressions;

namespace OdcAgentFileInspector.Internal;

/// <summary>
/// Decides whether a line matches. Literal mode uses ordinal comparison.
/// Regex mode uses RegexOptions.NonBacktracking only. There is no fallback to a backtracking engine.
/// </summary>
internal sealed class LineMatcher
{
    private readonly string? _pattern;
    private readonly StringComparison _comparison;
    private readonly Regex? _regex;

    private LineMatcher(string pattern, bool ignoreCase)
    {
        _pattern = pattern;
        _comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    }

    private LineMatcher(Regex regex) => _regex = regex;

    public static LineMatcher Literal(string pattern, bool ignoreCase) => new(pattern, ignoreCase);

    /// <summary>Throws ArgumentException (including RegexParseException) or NotSupportedException for patterns NonBacktracking rejects.</summary>
    public static LineMatcher Regex(string pattern, bool ignoreCase)
    {
        var options = RegexOptions.NonBacktracking | RegexOptions.CultureInvariant;
        if (ignoreCase) options |= RegexOptions.IgnoreCase;
        return new LineMatcher(new Regex(pattern, options, Limits.RegexLineTimeout));
    }

    /// <summary>Throws RegexMatchTimeoutException in regex mode when one line exceeds the per-line timeout.</summary>
    public bool IsMatch(string line) =>
        _regex is not null ? _regex.IsMatch(line) : line.Contains(_pattern!, _comparison);
}
