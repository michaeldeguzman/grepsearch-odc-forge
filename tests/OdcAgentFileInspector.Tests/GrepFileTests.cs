using System.Text;
using OdcAgentFileInspector.Tests.FileStorageTesting;
using OutSystems.ExternalLibraries.SDK;
using OutSystems.ExternalLibraries.SDK.FileStorage;

namespace OdcAgentFileInspector.Tests;

[TestFixture, NonParallelizable]
public class GrepFileTests
{
    private InMemoryFileStorage _storage = null!;
    private AgentFileInspector _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _storage = new InMemoryFileStorage();
        FileStorage.Reset();
        FileStorage.Configure(() => _storage);
        _sut = new AgentFileInspector();
    }

    [TearDown]
    public void TearDown() => FileStorage.Reset();

    private record R(string Text, int Total, bool Truncated, string Error);

    private IOSFile Add(string text) => _storage.Add(Encoding.UTF8.GetBytes(text));

    private R Grep(IOSFile f, string pattern, bool ignoreCase = false, int ctx = 0, int max = 0, bool regex = false)
    {
        _sut.GrepFile(f, pattern, regex, ignoreCase, ctx, max, out var t, out var n, out var tr, out var e);
        return new R(t, n, tr, e);
    }

    private static string Lines(int count, Func<int, string> make) =>
        string.Join("\n", Enumerable.Range(1, count).Select(make));

    // ---- basic matching ----

    [Test]
    public void Literal_CaseSensitive_VsIgnoreCase()
    {
        var f = Add("Error one\nerror two\nERROR three\n");
        Assert.That(Grep(f, "error").Total, Is.EqualTo(1));
        Assert.That(Grep(f, "error", ignoreCase: true).Total, Is.EqualTo(3));
    }

    [Test]
    public void Literal_TreatsRegexCharsAsText()
    {
        var f = Add("a.c\nabc\n");
        var r = Grep(f, "a.c");
        Assert.That(r.Total, Is.EqualTo(1));
        Assert.That(r.Text, Does.Contain("> 1: a.c"));
    }

    [Test]
    public void OutputFormat_HeaderAndMarkers()
    {
        var f = Add(File.ReadAllText(Fixture("sample.log")));
        var r = Grep(f, "ERR-4012", ctx: 1);
        Assert.That(r.Error, Is.Empty);
        Assert.That(r.Text, Does.StartWith("Showing 1 of 1 matches for \"ERR-4012\"\n"));
        Assert.That(r.Text, Does.Contain("  2: 2026-01-01 10:00:01 INFO listening on port 8080"));
        Assert.That(r.Text, Does.Contain("> 3: 2026-01-01 10:00:02 ERROR ERR-4012 Payment declined"));
        Assert.That(r.Text, Does.Contain("  4: 2026-01-01 10:00:03 INFO retrying in 30s"));
        Assert.That(r.Truncated, Is.False);
    }

    [Test]
    public void NoMatches_NoError()
    {
        var r = Grep(Add("abc\n"), "zzz");
        Assert.That(r.Text, Is.EqualTo("No matches for \"zzz\"."));
        Assert.That(r.Total, Is.EqualTo(0));
        Assert.That(r.Error, Is.Empty);
    }

    [TestCase("")]
    [TestCase("   ")]
    public void EmptyPattern_ReturnsError(string pattern)
    {
        var r = Grep(Add("abc\n"), pattern);
        Assert.That(r.Error, Is.EqualTo("Pattern is empty."));
        Assert.That(r.Text, Is.Empty);
    }

    // ---- regex ----

    [Test]
    public void Regex_Matches()
    {
        var f = Add("order 12 ok\norder abc ok\norder 345 ok\n");
        var r = Grep(f, @"order \d+ ok", regex: true);
        Assert.That(r.Error, Is.Empty);
        Assert.That(r.Total, Is.EqualTo(2));
        Assert.That(r.Text, Does.Contain("> 1: order 12 ok").And.Contain("> 3: order 345 ok"));
    }

    [Test]
    public void Regex_IgnoreCase()
    {
        var f = Add("Error\nerror\nfine\n");
        Assert.That(Grep(f, "^error$", regex: true).Total, Is.EqualTo(1));
        Assert.That(Grep(f, "^error$", ignoreCase: true, regex: true).Total, Is.EqualTo(2));
    }

    [Test]
    public void Regex_InvalidSyntax_ReturnsError()
    {
        var r = Grep(Add("abc\n"), "(unclosed", regex: true);
        Assert.That(r.Error, Is.Not.Empty);
        Assert.That(r.Error, Does.Contain("NonBacktracking mode does not support lookarounds or backreferences; simplify the pattern."));
        Assert.That(r.Text, Is.Empty);
    }

    [TestCase(@"(?<=a)b")]
    [TestCase(@"(a)\1")]
    public void Regex_LookaroundOrBackreference_ReturnsNonBacktrackingError_NoFallback(string pattern)
    {
        // "abab" would match (a)\1-style or lookbehind patterns under a backtracking engine.
        var r = Grep(Add("aab\nab\n"), pattern, regex: true);
        Assert.That(r.Error, Does.Contain("NonBacktracking mode does not support lookarounds or backreferences; simplify the pattern."));
        Assert.That(r.Text, Is.Empty);
        Assert.That(r.Total, Is.EqualTo(0));
    }

    [Test]
    public void Regex_UsesCountCapAndContext()
    {
        var r = Grep(Add(Lines(50, i => i == 25 ? "TARGET 7" : "x" + i)), @"TARGET \d", ctx: 1, regex: true);
        Assert.That(r.Text, Is.EqualTo("Showing 1 of 1 matches for \"TARGET \\d\"\n  24: x24\n> 25: TARGET 7\n  26: x26"));
    }

    // ---- deadline ----

    [Test]
    public void Deadline_StopsScan_SetsTruncated_AddsNote()
    {
        _storage.ReadDelay = TimeSpan.FromMilliseconds(20);
        _sut.ScanDeadline = TimeSpan.FromMilliseconds(250);
        var f = Add(Lines(60_000, i => i % 100 == 0 ? "HIT " + i : "filler line " + i));
        var r = Grep(f, "HIT");
        Assert.That(r.Truncated, Is.True);
        Assert.That(r.Text, Does.Match(@"\[scan stopped at line [1-9]\d*: time limit\]"));
        Assert.That(r.Error, Is.Empty);
    }

    // ---- context ----

    [Test]
    public void Context_Zero_ShowsOnlyMatches()
    {
        var f = Add(Lines(10, i => i == 5 ? "HIT" : "x" + i));
        var r = Grep(f, "HIT", ctx: 0);
        Assert.That(r.Text, Is.EqualTo("Showing 1 of 1 matches for \"HIT\"\n> 5: HIT"));
    }

    [Test]
    public void Context_Two_ShowsWindow()
    {
        var f = Add(Lines(10, i => i == 5 ? "HIT" : "x" + i));
        var r = Grep(f, "HIT", ctx: 2);
        Assert.That(r.Text, Is.EqualTo("Showing 1 of 1 matches for \"HIT\"\n  3: x3\n  4: x4\n> 5: HIT\n  6: x6\n  7: x7"));
    }

    [Test]
    public void Context_Five_ShowsFiveEachSide_AndClampsAbove()
    {
        var f = Add(Lines(20, i => i == 10 ? "HIT" : "x" + i));
        var five = Grep(f, "HIT", ctx: 5).Text;
        var clamped = Grep(f, "HIT", ctx: 99).Text;
        Assert.That(five, Does.Contain("  5: x5").And.Contain(" 15: x15"));
        Assert.That(five, Does.Not.Contain("  4: x4").And.Not.Contain(" 16: x16"));
        Assert.That(clamped, Is.EqualTo(five));
    }

    [Test]
    public void Context_OverlappingWindows_Merge()
    {
        var f = Add(Lines(10, i => i is 4 or 6 ? "HIT" : "x" + i));
        var r = Grep(f, "HIT", ctx: 2);
        Assert.That(r.Text, Does.Not.Contain("--"));
        Assert.That(r.Text, Is.EqualTo(
            "Showing 2 of 2 matches for \"HIT\"\n  2: x2\n  3: x3\n> 4: HIT\n  5: x5\n> 6: HIT\n  7: x7\n  8: x8"));
    }

    [Test]
    public void Context_NonContiguousGroups_GetSeparator()
    {
        var f = Add(Lines(20, i => i is 3 or 15 ? "HIT" : "x" + i));
        var r = Grep(f, "HIT", ctx: 1);
        Assert.That(r.Text, Is.EqualTo(
            "Showing 2 of 2 matches for \"HIT\"\n  2: x2\n> 3: HIT\n  4: x4\n--\n  14: x14\n> 15: HIT\n  16: x16"));
    }

    [Test]
    public void Context_AdjacentGroups_NoSeparator()
    {
        // Window 1 ends at line 4, window 2 starts at line 5: contiguous, so no "--".
        var f = Add(Lines(10, i => i is 3 or 6 ? "HIT" : "x" + i));
        var r = Grep(f, "HIT", ctx: 1);
        Assert.That(r.Text, Does.Not.Contain("--"));
    }

    [Test]
    public void Match_OnFirstAndLastLine_ContextAtEdges()
    {
        var f = Add("HIT\nb\nc\nd\nHIT");
        var r = Grep(f, "HIT", ctx: 2);
        Assert.That(r.Text, Is.EqualTo(
            "Showing 2 of 2 matches for \"HIT\"\n> 1: HIT\n  2: b\n  3: c\n  4: d\n> 5: HIT"));
    }

    // ---- maxMatches ----

    [Test]
    public void MaxMatches_Zero_DefaultsTo20_AndTotalCountsBeyond()
    {
        var f = Add(Lines(100, _ => "HIT"));
        var r = Grep(f, "HIT", max: 0);
        Assert.That(r.Text, Does.StartWith("Showing 20 of 100 matches"));
        Assert.That(r.Total, Is.EqualTo(100));
        Assert.That(r.Truncated, Is.True);
    }

    [Test]
    public void MaxMatches_One()
    {
        var r = Grep(Add(Lines(5, _ => "HIT")), "HIT", max: 1);
        Assert.That(r.Text, Does.StartWith("Showing 1 of 5 matches"));
        Assert.That(r.Truncated, Is.True);
    }

    [Test]
    public void MaxMatches_Hundred_ClampsTo50()
    {
        var r = Grep(Add(Lines(200, _ => "HIT")), "HIT", max: 100);
        Assert.That(r.Text, Does.StartWith("Showing 50 of 200 matches"));
    }

    [Test]
    public void MaxMatches_Reached_TrailingContextStopsAtNextMatch()
    {
        var f = Add("HIT\nx\nHIT\ny\n");
        var r = Grep(f, "HIT", ctx: 2, max: 1);
        Assert.That(r.Text, Is.EqualTo("Showing 1 of 2 matches for \"HIT\"\n> 1: HIT\n  2: x"));
    }

    [Test]
    public void MatchCount_ExactlyAtCap_ShowsPlainHeader()
    {
        var r = Grep(Add(Lines(10_000, _ => "HIT")), "HIT");
        Assert.That(r.Text, Does.StartWith("Showing 20 of 10000 matches for \"HIT\"\n"));
        Assert.That(r.Text, Does.Not.Contain("10,000+"));
        Assert.That(r.Total, Is.EqualTo(10_000));
    }

    [TestCase(10_001)]
    [TestCase(12_000)]
    public void MatchCount_PastCap_ShowsPlusHeader(int lines)
    {
        var r = Grep(Add(Lines(lines, _ => "HIT")), "HIT");
        Assert.That(r.Text, Does.StartWith(
            "Showing 20 of 10,000+ matches for \"HIT\" (count stopped at limit; use a more specific pattern)\n"));
        Assert.That(r.Total, Is.EqualTo(10_000));
        Assert.That(r.Truncated, Is.True);
    }

    [Test]
    public void MatchCount_BelowCap_ShowsExactTotal()
    {
        var r = Grep(Add(Lines(9_999, _ => "HIT")), "HIT");
        Assert.That(r.Text, Does.StartWith("Showing 20 of 9999 matches for \"HIT\"\n"));
    }

    [Test]
    public void LineCap_Of1000000Lines_IsNotMatchCap()
    {
        // 1,000,001 lines: only line 1,000,001 matches, so the line cap stops the scan before it.
        var sb = new StringBuilder();
        for (int i = 1; i <= 1_000_000; i++) sb.Append("x\n");
        sb.Append("HIT\n");
        var r = Grep(Add(sb.ToString()), "HIT");
        Assert.That(r.Text, Is.EqualTo("No matches for \"HIT\"."));
        Assert.That(r.Truncated, Is.True);
    }

    [Test]
    public void NotTruncated_WhenAllMatchesShown()
    {
        Assert.That(Grep(Add("HIT\nHIT\n"), "HIT").Truncated, Is.False);
    }

    // ---- clipping and output budget ----

    [Test]
    public void LongLine_ClippedAt300_SetsTruncated()
    {
        var r = Grep(Add("HIT" + new string('a', 500)), "HIT");
        Assert.That(r.Truncated, Is.True);
        Assert.That(r.Text, Does.Contain(" ... [clipped]"));
        var shown = r.Text.Split('\n')[1];
        Assert.That(shown, Is.EqualTo("> 1: " + ("HIT" + new string('a', 500)).Substring(0, 300) + " ... [clipped]"));
    }

    [Test]
    public void Line_Of300Chars_NotClipped()
    {
        var r = Grep(Add("HIT" + new string('a', 297)), "HIT");
        Assert.That(r.Truncated, Is.False);
        Assert.That(r.Text, Does.Not.Contain("[clipped]"));
    }

    [Test]
    public void Output_CappedAt64KB()
    {
        // 50 matches x ~ 5 context-free 290-char lines would be ~14 KB; use context to push past 64 KB.
        var line = new string('z', 290);
        var f = Add(Lines(5000, i => i % 10 == 0 ? "HIT " + line : line));
        var r = Grep(f, "HIT", ctx: 5, max: 50);
        Assert.That(Encoding.UTF8.GetByteCount(r.Text), Is.LessThanOrEqualTo(64 * 1024 + 200));
        Assert.That(r.Text, Does.Contain("[output truncated at 64 KB]"));
        Assert.That(r.Truncated, Is.True);
        Assert.That(r.Total, Is.EqualTo(500));
    }

    // ---- line endings, encodings, edge files ----

    [TestCase("a\nHIT\nc\n")]
    [TestCase("a\r\nHIT\r\nc\r\n")]
    [TestCase("a\rHIT\rc\r")]
    [TestCase("a\nHIT\nc")] // no trailing newline
    public void LineEndings_AllRecognised(string content)
    {
        var r = Grep(Add(content), "HIT");
        Assert.That(r.Text, Does.Contain("> 2: HIT"));
        Assert.That(r.Total, Is.EqualTo(1));
    }

    [Test]
    public void LastLineWithoutTrailingNewline_IsSearched()
    {
        var r = Grep(Add("a\nb\nHIT"), "HIT");
        Assert.That(r.Text, Does.Contain("> 3: HIT"));
    }

    [Test]
    public void Utf8Bom_IsStripped()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("HIT first\nsecond")).ToArray();
        var r = Grep(_storage.Add(bytes), "HIT");
        Assert.That(r.Text, Does.Contain("> 1: HIT first"));
    }

    [Test]
    public void Utf16Bom_IsAllowedNotBinary()
    {
        var bytes = Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes("hello\nHIT here\n")).ToArray();
        var r = Grep(_storage.Add(bytes), "HIT");
        Assert.That(r.Error, Is.Empty);
        Assert.That(r.Text, Does.Contain("> 2: HIT here"));
    }

    [Test]
    public void EmptyFile_NoMatches_NoError()
    {
        var r = Grep(_storage.Add(Array.Empty<byte>()), "HIT");
        Assert.That(r.Error, Is.Empty);
        Assert.That(r.Total, Is.EqualTo(0));
    }

    [Test]
    public void BinaryFile_ReturnsOmniDoc2MdError()
    {
        var bytes = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x00, 0x01, 0x02, 0x48, 0x49, 0x54 };
        var r = Grep(_storage.Add(bytes), "HIT");
        Assert.That(r.Error, Does.StartWith("File appears to be binary"));
        Assert.That(r.Error, Does.Contain("OmniDoc2MD"));
        Assert.That(r.Text, Is.Empty);
        Assert.That(r.Total, Is.EqualTo(0));
    }

    [Test]
    public void Binary_NulBeyondFirstKilobyte_NotDetected()
    {
        // Documents the sniff window: only the first 1,024 bytes are checked.
        var bytes = Encoding.UTF8.GetBytes(new string('a', 2000) + "\n").Concat(new byte[] { 0 }).ToArray();
        var r = Grep(_storage.Add(bytes), "zzz");
        Assert.That(r.Error, Is.Empty);
    }

    [Test]
    public void SniffedBytes_AreNotLostOrDuplicated()
    {
        // Lines straddle the 1,024-byte sniff boundary; every line must be seen exactly once.
        var f = Add(Lines(500, i => $"line{i:D4} HIT"));
        var r = Grep(f, "HIT", max: 50);
        Assert.That(r.Total, Is.EqualTo(500));
        Assert.That(r.Text, Does.Contain("> 1: line0001 HIT"));
    }

    [Test]
    public void MissingFile_ReturnsErrorInsteadOfThrowing()
    {
        var ghost = new TestFile("nope", 0);
        var r = Grep(ghost, "HIT");
        Assert.That(r.Error, Is.Not.Empty);
        Assert.That(r.Text, Is.Empty);
    }

    [Test, Category("Perf")]
    public void Perf_50MbFile_FinishesUnder10Seconds_WithoutRetainedGrowth()
    {
        var sb = new StringBuilder(52 * 1024 * 1024);
        var filler = new string('x', 90);
        for (int i = 1; sb.Length < 50 * 1024 * 1024; i++)
            sb.Append(i % 5000 == 0 ? "ERROR " : "INFO  ").Append(filler).Append('\n');
        var f = Add(sb.ToString());
        sb.Clear();

        long before = GC.GetTotalMemory(forceFullCollection: true);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var r = Grep(f, "ERROR", ctx: 2);
        sw.Stop();
        long retained = GC.GetTotalMemory(forceFullCollection: true) - before;

        TestContext.Out.WriteLine($"Perf: {f.Size / 1024 / 1024} MB, {r.Total} matches, {sw.ElapsedMilliseconds} ms, retained {retained / 1024} KB");
        Assert.That(r.Error, Is.Empty);
        Assert.That(r.Total, Is.GreaterThan(0));
        Assert.That(sw.Elapsed, Is.LessThan(TimeSpan.FromSeconds(10)));
        Assert.That(retained, Is.LessThan(10 * 1024 * 1024), "memory retained after the scan");
    }

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);
}
