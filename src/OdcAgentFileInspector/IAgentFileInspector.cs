using OutSystems.ExternalLibraries.SDK;

namespace OdcAgentFileInspector;

[OSInterface(
    Name = "AgentFileInspector",
    Description = "Streaming grep, line-range reader and outline extractor over ODC File values for AI agents.",
    IconResourceName = "OdcAgentFileInspector.Resources.AgentFileInspector.png")]
public interface IAgentFileInspector
{
    [OSAction(Description = "Streams an ODC File and returns 1-based matching lines with optional before/after context.")]
    void GrepFile(
        [OSParameter(DataType = OSDataType.File, Description = "Target text, log, CSV or Markdown file to scan.")] IOSFile file,
        [OSParameter(Description = "Literal text or regular expression to search for.")] string pattern,
        [OSParameter(Description = "True to treat pattern as a .NET regular expression (NonBacktracking). False for literal text.")] bool isRegex,
        [OSParameter(Description = "True for case-insensitive matching.")] bool ignoreCase,
        [OSParameter(Description = "Context lines before and after each match (0 to 5).")] int contextLines,
        [OSParameter(Description = "Maximum matches to return (1 to 50; 0 means 20).")] int maxMatches,
        [OSParameter(Description = "Line-numbered match blocks formatted for an LLM.")] out string formattedMatches,
        [OSParameter(Description = "Total matches counted in the file, up to the scan cap.")] out int totalMatchesFound,
        [OSParameter(Description = "True if results were limited by maxMatches, scan caps, output size or line clipping.")] out bool isTruncated,
        [OSParameter(Description = "Error message for invalid input, invalid regex or unreadable content. Empty on success.")] out string errorMessage);

    [OSAction(Description = "Reads a range of lines (1-based, inclusive, max 200). Read only what you need: when reading a section, stop at the next heading of the same or higher level from the outline.")]
    void ReadFileLines(
        [OSParameter(DataType = OSDataType.File, Description = "Target text or Markdown file to read.")] IOSFile file,
        [OSParameter(Description = "1-based first line to read.")] int startLine,
        [OSParameter(Description = "1-based inclusive last line to read (capped at startLine + 199).")] int endLine,
        [OSParameter(Description = "Line-numbered text for the requested range.")] out string content,
        [OSParameter(Description = "Total number of lines in the file.")] out int totalLines,
        [OSParameter(Description = "True when more lines exist after the returned range.")] out bool hasMoreBelow,
        [OSParameter(Description = "Error message. Empty on success.")] out string errorMessage);

    [OSAction(Description = "Returns line count, byte size and Markdown headings (# to ####) with 1-based line numbers.")]
    void GetFileOutline(
        [OSParameter(DataType = OSDataType.File, Description = "Target text or Markdown file to outline.")] IOSFile file,
        [OSParameter(Description = "Maximum headings to return (1 to 200; 0 means 100).")] int maxHeadings,
        [OSParameter(Description = "Summary line plus headings with line numbers.")] out string outline,
        [OSParameter(Description = "Total number of lines in the file.")] out int totalLines,
        [OSParameter(Description = "Error message. Empty on success.")] out string errorMessage);
}
