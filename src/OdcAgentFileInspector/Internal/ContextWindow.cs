namespace OdcAgentFileInspector.Internal;

internal readonly record struct OutLine(int LineNo, string Text, bool IsMatch, bool SeparatorBefore);

/// <summary>
/// grep -n -C style context. Memory is O(contextLines). Overlapping windows merge;
/// non-contiguous groups get a separator flag on their first line.
/// </summary>
internal sealed class ContextWindow
{
    private readonly int _context;
    private readonly Queue<(int LineNo, string Text)> _before = new();
    private int _afterRemaining;
    private int _lastEmitted;

    public ContextWindow(int contextLines) => _context = contextLines;

    /// <summary>
    /// Feeds one line. <paramref name="canEmitMatch"/> false means the match limit is reached:
    /// the match is not shown and trailing context stops at it.
    /// </summary>
    public void Feed(int lineNo, string text, bool isMatch, bool canEmitMatch, List<OutLine> output)
    {
        if (isMatch && canEmitMatch)
        {
            bool first = true;
            foreach (var (n, t) in _before)
            {
                if (n > _lastEmitted)
                {
                    output.Add(new OutLine(n, t, false, first && _lastEmitted != 0 && n != _lastEmitted + 1));
                    _lastEmitted = n;
                    first = false;
                }
            }
            _before.Clear();
            output.Add(new OutLine(lineNo, text, true, first && _lastEmitted != 0 && lineNo != _lastEmitted + 1));
            _lastEmitted = lineNo;
            _afterRemaining = _context;
            return;
        }

        if (isMatch) // match beyond the limit: counted by the caller, never shown
        {
            _afterRemaining = 0;
            _before.Clear();
            return;
        }

        if (_afterRemaining > 0)
        {
            output.Add(new OutLine(lineNo, text, false, false));
            _lastEmitted = lineNo;
            _afterRemaining--;
            return;
        }

        if (_context > 0 && canEmitMatch)
        {
            _before.Enqueue((lineNo, text));
            if (_before.Count > _context) _before.Dequeue();
        }
    }
}
