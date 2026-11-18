using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using OdcAgentFileInspector.Internal;
using OutSystems.ExternalLibraries.SDK;
using OutSystems.ExternalLibraries.SDK.FileStorage.Exceptions;

namespace OdcAgentFileInspector;

public class AgentFileInspector : IAgentFileInspector
{
    private readonly ILogger _logger;

    public AgentFileInspector(ILogger logger) => _logger = logger ?? NullLogger.Instance;

    public AgentFileInspector() : this(NullLogger.Instance) { }

    /// <summary>Overall scan deadline. Tests shorten it; production uses Limits.ScanDeadline.</summary>
    internal TimeSpan ScanDeadline { get; set; } = Limits.ScanDeadline;

    // ---- GrepFile ----

    public void GrepFile(IOSFile file, string pattern, bool isRegex, bool ignoreCase, int contextLines, int maxMatches,
        out string formattedMatches, out int totalMatchesFound, out bool isTruncated, out string errorMessage)
    {
        var r = Guard(() => GrepAsync(file, pattern, isRegex, ignoreCase, contextLines, maxMatches),
            e => new GrepResult("", 0, false, e));
        formattedMatches = r.Text;
        totalMatchesFound = r.Total;
        isTruncated = r.Truncated;
        errorMessage = r.Error;
    }

    private sealed record GrepResult(string Text, int Total, bool Truncated, string Error);

    private async Task<GrepResult> GrepAsync(IOSFile file, string pattern, bool isRegex, bool ignoreCase, int contextLines, int maxMatches)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            return new GrepResult("", 0, false, "Pattern is empty.");

        contextLines = Math.Clamp(contextLines, 0, Limits.MaxContextLines);
        if (maxMatches <= 0) maxMatches = Limits.DefaultMaxMatches;
        maxMatches = Math.Clamp(maxMatches, 1, Limits.MaxMatchesCeiling);

        LineMatcher matcher;
        if (isRegex)
        {
            try
            {
                matcher = LineMatcher.Regex(pattern, ignoreCase);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
            {
                return new GrepResult("", 0, false, $"{ex.Message} {Limits.RegexHint}");
            }
        }
        else
        {
            matcher = LineMatcher.Literal(pattern, ignoreCase);
        }

        var window = new ContextWindow(contextLines);
        var budget = new OutputBudget();
        var body = new StringBuilder();
        var pending = new List<OutLine>();

        int total = 0, shown = 0;
        bool truncated = false, lineCapHit = false, matchCapHit = false;

        ScanResult scan;
        try
        {
            scan = await LineScanner.ScanAsync(file, (no, text) =>
            {
                if (no > Limits.MaxScannedLines)
                {
                    lineCapHit = true;
                    return false;
                }

                bool isMatch = matcher.IsMatch(text);
                if (isMatch)
                {
                    total++;
                    // Count one past the cap so exactly 10,000 matches is still an exact total.
                    if (total > Limits.MaxCountedMatches)
                    {
                        total = Limits.MaxCountedMatches;
                        matchCapHit = true;
                        return false;
                    }
                }

                if (!budget.Exhausted)
                {
                    pending.Clear();
                    window.Feed(no, text, isMatch, canEmitMatch: shown < maxMatches, pending);
                    foreach (var o in pending)
                    {
                        if (o.SeparatorBefore && !budget.TryReserve("--")) break;
                        string clipped = OutputBudget.Clip(o.Text, Limits.GrepLineClip, out bool wasClipped);
                        string rendered = (o.IsMatch ? "> " : "  ") + o.LineNo + ": " + clipped;
                        if (!budget.TryReserve(rendered)) break;
                        if (o.SeparatorBefore) body.Append("--\n");
                        body.Append(rendered).Append('\n');
                        if (wasClipped) truncated = true;
                        if (o.IsMatch) shown++;
                    }
                }
                return true;
            }, ScanDeadline);
        }
        catch (RegexMatchTimeoutException)
        {
            return new GrepResult("", 0, false, Limits.RegexTimeoutError);
        }

        if (scan.IsBinary)
            return new GrepResult("", 0, false, Limits.BinaryError);

        if (lineCapHit || matchCapHit) truncated = true;
        if (budget.Exhausted)
        {
            truncated = true;
            body.Append(Limits.BudgetNote).Append('\n');
        }
        if (scan.TimedOut)
        {
            truncated = true;
            body.Append($"[scan stopped at line {scan.LinesRead}: time limit]\n");
        }
        if (total > shown) truncated = true;

        if (total == 0)
            return new GrepResult($"No matches for \"{pattern}\".", 0, truncated, "");

        string count = matchCapHit
            ? Limits.MaxCountedMatches.ToString("N0", CultureInfo.InvariantCulture) + "+"
            : total.ToString(CultureInfo.InvariantCulture);
        string suffix = matchCapHit ? " (count stopped at limit; use a more specific pattern)" : "";
        string header = $"Showing {shown} of {count} matches for \"{pattern}\"{suffix}\n";
        return new GrepResult(header + body.ToString().TrimEnd('\n'), total, truncated, "");
    }

    // ---- ReadFileLines ----

    public void ReadFileLines(IOSFile file, int startLine, int endLine,
        out string content, out int totalLines, out bool hasMoreBelow, out string errorMessage)
    {
        var r = Guard(() => ReadLinesAsync(file, startLine, endLine),
            e => new ReadResult("", 0, false, e));
        content = r.Content;
        totalLines = r.TotalLines;
        hasMoreBelow = r.HasMoreBelow;
        errorMessage = r.Error;
    }

    private sealed record ReadResult(string Content, int TotalLines, bool HasMoreBelow, string Error);

    private async Task<ReadResult> ReadLinesAsync(IOSFile file, int startLine, int endLine)
    {
        long start = Math.Max(startLine, 1);
        long end = Math.Max(endLine, start);
        end = Math.Min(end, start + Limits.MaxReadSpan - 1);

        var budget = new OutputBudget();
        var body = new StringBuilder();

        var scan = await LineScanner.ScanAsync(file, (no, text) =>
        {
            // Keep counting to the end of the file so totalLines is exact.
            if (no >= start && no <= end && !budget.Exhausted)
            {
                string clipped = OutputBudget.Clip(text, Limits.ReadLineClip, out _);
                string rendered = no + ": " + clipped;
                if (budget.TryReserve(rendered))
                    body.Append(rendered).Append('\n');
            }
            return true;
        }, ScanDeadline);

        if (scan.IsBinary)
            return new ReadResult("", 0, false, Limits.BinaryError);

        if (scan.TimedOut)
        {
            body.Append($"[scan stopped at line {scan.LinesRead}: time limit]\n");
            return new ReadResult(body.ToString().TrimEnd('\n'), scan.LinesRead, true, "");
        }

        if (start > scan.LinesRead)
            return new ReadResult("", scan.LinesRead, false,
                $"StartLine {start} is beyond the end of the file ({scan.LinesRead} lines).");

        if (budget.Exhausted) body.Append(Limits.BudgetNote).Append('\n');

        return new ReadResult(body.ToString().TrimEnd('\n'), scan.LinesRead, end < scan.LinesRead, "");
    }

    // ---- GetFileOutline ----

    public void GetFileOutline(IOSFile file, int maxHeadings,
        out string outline, out int totalLines, out string errorMessage)
    {
        var r = Guard(() => OutlineAsync(file, maxHeadings), e => new OutlineResult("", 0, e));
        outline = r.Outline;
        totalLines = r.TotalLines;
        errorMessage = r.Error;
    }

    private sealed record OutlineResult(string Outline, int TotalLines, string Error);

    private async Task<OutlineResult> OutlineAsync(IOSFile file, int maxHeadings)
    {
        if (maxHeadings <= 0) maxHeadings = Limits.DefaultMaxHeadings;
        maxHeadings = Math.Clamp(maxHeadings, 1, Limits.MaxHeadingsCeiling);

        var budget = new OutputBudget();
        var body = new StringBuilder();
        var fence = new FenceTracker();
        int headings = 0, shown = 0;

        var scan = await LineScanner.ScanAsync(file, (no, text) =>
        {
            if (fence.IsInsideFence(text)) return true;
            if (!MarkdownHeading.TryParse(text, out string heading)) return true;
            headings++;
            if (shown >= maxHeadings || budget.Exhausted) return true;
            string rendered = "Line " + no + ": " + OutputBudget.Clip(heading, Limits.GrepLineClip, out _);
            if (budget.TryReserve(rendered))
            {
                body.Append(rendered).Append('\n');
                shown++;
            }
            return true;
        }, ScanDeadline);

        if (scan.IsBinary)
            return new OutlineResult("", 0, Limits.BinaryError);

        var inv = CultureInfo.InvariantCulture;
        string summary = $"Lines: {scan.LinesRead.ToString("N0", inv)} | Bytes: {scan.Bytes.ToString("N0", inv)} | Headings: {headings.ToString("N0", inv)} (showing {shown.ToString("N0", inv)})";

        var sb = new StringBuilder(summary).Append('\n');
        if (headings == 0)
            sb.Append("No Markdown headings found.\n");
        else
            sb.Append(body);
        if (budget.Exhausted) sb.Append(Limits.BudgetNote).Append('\n');
        if (scan.TimedOut) sb.Append($"[scan stopped at line {scan.LinesRead}: time limit]\n");

        return new OutlineResult(sb.ToString().TrimEnd('\n'), scan.LinesRead, "");
    }

    // ---- Error boundary: never let an exception escape an [OSAction] ----

    private T Guard<T>(Func<Task<T>> work, Func<string, T> onError)
    {
        try
        {
            return work().GetAwaiter().GetResult();
        }
        catch (FileStorageException ex)
        {
            _logger.LogError(ex, "File storage error reading file");
            return onError(Limits.StorageError);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in AgentFileInspector");
            return onError(Limits.GenericError);
        }
    }
}
