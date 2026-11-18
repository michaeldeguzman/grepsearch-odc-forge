namespace OdcAgentFileInspector.Internal;

internal static class Limits
{
    public const int BinarySniffBytes = 1024;
    public static readonly TimeSpan ScanDeadline = TimeSpan.FromSeconds(60);
    public static readonly TimeSpan RegexLineTimeout = TimeSpan.FromSeconds(2);

    public const int DefaultMaxHeadings = 100;
    public const int MaxHeadingsCeiling = 200;

    public const int MaxContextLines = 5;
    public const int DefaultMaxMatches = 20;
    public const int MaxMatchesCeiling = 50;
    public const int MaxCountedMatches = 10_000;
    public const int MaxScannedLines = 1_000_000;

    public const int GrepLineClip = 300;
    public const int ReadLineClip = 500;
    public const int OutputBudgetBytes = 64 * 1024;

    public const int MaxReadSpan = 200;

    public const string RegexHint =
        "NonBacktracking mode does not support lookarounds or backreferences; simplify the pattern.";
    public const string RegexTimeoutError =
        "The regular expression took too long on one line and the scan was stopped. Simplify the pattern.";
    public const string BudgetNote = "[output truncated at 64 KB]";

    public const string ClipSuffix = " ... [clipped]";
    public const string BinaryError =
        "File appears to be binary (e.g. PDF, DOCX, ZIP). Convert it to text or Markdown first, for example with OmniDoc2MD.";
    public const string StorageError = "Could not read the file from storage.";
    public const string GenericError = "Unexpected error while processing the file.";
}
