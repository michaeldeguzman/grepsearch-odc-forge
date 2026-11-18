namespace OdcAgentFileInspector.Internal;

/// <summary>ATX heading detection: up to 3 leading spaces, 1 to 4 '#', one space, then text.</summary>
internal static class MarkdownHeading
{
    public static bool TryParse(string line, out string heading)
    {
        heading = "";
        int i = 0;
        while (i < line.Length && i < 4 && line[i] == ' ') i++;
        if (i > 3) return false;

        int hashes = 0;
        while (i + hashes < line.Length && line[i + hashes] == '#') hashes++;
        if (hashes < 1 || hashes > 4) return false;

        int after = i + hashes;
        if (after >= line.Length || line[after] != ' ') return false;
        if (line.AsSpan(after).Trim().IsEmpty) return false;

        heading = line.Substring(i).TrimEnd();
        return true;
    }
}

/// <summary>
/// Tracks fenced code blocks. A fence opens with 3 or more backticks or tildes and closes with
/// the same character, at least as many of them, and nothing else on the line.
/// </summary>
internal sealed class FenceTracker
{
    private char _fenceChar;
    private int _fenceLen;

    /// <summary>True when <paramref name="line"/> is a fence line or sits inside a fenced block.</summary>
    public bool IsInsideFence(string line)
    {
        int i = 0;
        while (i < line.Length && i < 4 && line[i] == ' ') i++;
        bool indentOk = i <= 3;

        char c = indentOk && i < line.Length ? line[i] : '\0';
        int run = 0;
        if (c is '`' or '~')
            while (i + run < line.Length && line[i + run] == c) run++;

        if (_fenceChar == '\0')
        {
            if (run >= 3)
            {
                _fenceChar = c;
                _fenceLen = run;
                return true;
            }
            return false;
        }

        if (run >= _fenceLen && c == _fenceChar && line.AsSpan(i + run).Trim().IsEmpty)
        {
            _fenceChar = '\0';
            _fenceLen = 0;
        }
        return true;
    }
}
