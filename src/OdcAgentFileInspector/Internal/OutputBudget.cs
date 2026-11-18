using System.Text;

namespace OdcAgentFileInspector.Internal;

/// <summary>Caps total UTF-8 output and clips individual lines.</summary>
internal sealed class OutputBudget
{
    private readonly int _limitBytes;
    private int _usedBytes;

    public OutputBudget(int limitBytes = Limits.OutputBudgetBytes) => _limitBytes = limitBytes;

    public bool Exhausted { get; private set; }

    /// <summary>Clips <paramref name="text"/> to <paramref name="maxChars"/>. Sets <paramref name="clipped"/> when it did.</summary>
    public static string Clip(string text, int maxChars, out bool clipped)
    {
        if (text.Length <= maxChars)
        {
            clipped = false;
            return text;
        }
        clipped = true;
        // Avoid splitting a surrogate pair.
        int cut = maxChars;
        if (char.IsHighSurrogate(text[cut - 1])) cut--;
        return text.Substring(0, cut) + Limits.ClipSuffix;
    }

    /// <summary>Reserves room for one output line (plus newline). False once the budget is spent.</summary>
    public bool TryReserve(string line)
    {
        if (Exhausted) return false;
        int bytes = Encoding.UTF8.GetByteCount(line) + 1;
        if (_usedBytes + bytes > _limitBytes)
        {
            Exhausted = true;
            return false;
        }
        _usedBytes += bytes;
        return true;
    }
}
