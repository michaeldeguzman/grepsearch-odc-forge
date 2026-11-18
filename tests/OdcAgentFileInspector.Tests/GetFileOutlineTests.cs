using System.Text;
using OdcAgentFileInspector.Tests.FileStorageTesting;
using OutSystems.ExternalLibraries.SDK;
using OutSystems.ExternalLibraries.SDK.FileStorage;

namespace OdcAgentFileInspector.Tests;

[TestFixture, NonParallelizable]
public class GetFileOutlineTests
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

    private record R(string Outline, int Total, string Error);

    private IOSFile Add(string text) => _storage.Add(Encoding.UTF8.GetBytes(text));

    private R Outline(IOSFile f, int max = 0)
    {
        _sut.GetFileOutline(f, max, out var o, out var n, out var e);
        return new R(o, n, e);
    }

    [Test]
    public void Headings_H1ToH4_AreListedWithLineNumbers()
    {
        var text = "# One\ntext\n## Two\n### Three\n#### Four\n";
        var r = Outline(Add(text));
        Assert.That(r.Outline, Is.EqualTo(
            "Lines: 5 | Bytes: " + Encoding.UTF8.GetByteCount(text) + " | Headings: 4 (showing 4)\n" +
            "Line 1: # One\nLine 3: ## Two\nLine 4: ### Three\nLine 5: #### Four"));
        Assert.That(r.Total, Is.EqualTo(5));
        Assert.That(r.Error, Is.Empty);
    }

    [Test]
    public void H5_IsIgnored()
    {
        var r = Outline(Add("##### five\n# one\n"));
        Assert.That(r.Outline, Does.Contain("Headings: 1 (showing 1)"));
        Assert.That(r.Outline, Does.Not.Contain("five"));
    }

    [Test]
    public void HashTagWithoutSpace_IsIgnored()
    {
        var r = Outline(Add("#tag\n#### \n# real\n"));
        Assert.That(r.Outline, Does.Contain("Headings: 1 (showing 1)"));
        Assert.That(r.Outline, Does.Contain("Line 3: # real"));
    }

    [Test]
    public void LeadingSpaces_UpToThreeAllowed()
    {
        var r = Outline(Add("   ## ok\n    ## code block\n"));
        Assert.That(r.Outline, Does.Contain("Line 1: ## ok"));
        Assert.That(r.Outline, Does.Not.Contain("code block"));
    }

    [Test]
    public void BacktickFence_HidesHeadings()
    {
        var r = Outline(Add("# a\n```\n# hidden\n```\n# b\n"));
        Assert.That(r.Outline, Does.Contain("Headings: 2 (showing 2)"));
        Assert.That(r.Outline, Does.Not.Contain("hidden"));
        Assert.That(r.Outline, Does.Contain("Line 5: # b"));
    }

    [Test]
    public void TildeFence_HidesHeadings()
    {
        var r = Outline(Add("# a\n~~~\n# hidden\n~~~\n# b\n"));
        Assert.That(r.Outline, Does.Not.Contain("hidden"));
        Assert.That(r.Outline, Does.Contain("Headings: 2"));
    }

    [Test]
    public void TildeLine_InsideBacktickFence_DoesNotCloseIt()
    {
        var r = Outline(Add("```\n~~~\n# hidden\n```\n# shown\n"));
        Assert.That(r.Outline, Does.Not.Contain("hidden"));
        Assert.That(r.Outline, Does.Contain("Line 5: # shown"));
    }

    [Test]
    public void ShorterFence_DoesNotCloseLongerFence()
    {
        var r = Outline(Add("````\n```\n# hidden\n````\n# shown\n"));
        Assert.That(r.Outline, Does.Not.Contain("hidden"));
        Assert.That(r.Outline, Does.Contain("Line 5: # shown"));
    }

    [Test]
    public void MaxHeadings_CapsOutput_ButCountsAll()
    {
        var text = string.Join("\n", Enumerable.Range(1, 30).Select(i => "# H" + i));
        var r = Outline(Add(text), 5);
        Assert.That(r.Outline, Does.Contain("Headings: 30 (showing 5)"));
        Assert.That(r.Outline.Split('\n'), Has.Length.EqualTo(6));
    }

    [Test]
    public void MaxHeadings_ZeroDefaultsTo100_AndClampsTo200()
    {
        var text = string.Join("\n", Enumerable.Range(1, 300).Select(i => "# H" + i));
        Assert.That(Outline(Add(text), 0).Outline, Does.Contain("Headings: 300 (showing 100)"));
        Assert.That(Outline(Add(text), 999).Outline, Does.Contain("Headings: 300 (showing 200)"));
    }

    [Test]
    public void ByteCount_MatchesFixtureSize()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "sample.md");
        var bytes = File.ReadAllBytes(path);
        var r = Outline(_storage.Add(bytes));
        Assert.That(r.Outline, Does.StartWith("Lines: "));
        Assert.That(r.Outline, Does.Contain($"| Bytes: {bytes.Length:N0} |"));
        Assert.That(r.Outline, Does.Contain("Line 1: # Sample Spec"));
        Assert.That(r.Outline, Does.Not.Contain("not a heading"));
    }

    [Test]
    public void ByteCount_IncludesBomAndMultibyteChars()
    {
        var bytes = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("# café 中\n")).ToArray();
        var r = Outline(_storage.Add(bytes));
        Assert.That(r.Outline, Does.Contain($"| Bytes: {bytes.Length} |"));
        Assert.That(r.Outline, Does.Contain("Line 1: # café 中"));
    }

    [Test]
    public void NoHeadings_ReportsSummaryAndMessage()
    {
        var text = "plain\ntext\n";
        var r = Outline(Add(text));
        Assert.That(r.Outline, Is.EqualTo(
            "Lines: 2 | Bytes: " + text.Length + " | Headings: 0 (showing 0)\nNo Markdown headings found."));
    }

    [Test]
    public void EmptyFile_ZeroLines()
    {
        var r = Outline(_storage.Add(Array.Empty<byte>()));
        Assert.That(r.Total, Is.EqualTo(0));
        Assert.That(r.Outline, Does.StartWith("Lines: 0 | Bytes: 0 |"));
        Assert.That(r.Error, Is.Empty);
    }

    [Test]
    public void LargeCounts_UseThousandsSeparators()
    {
        var r = Outline(Add(string.Join("\n", Enumerable.Range(1, 1284).Select(i => "x"))));
        Assert.That(r.Outline, Does.StartWith("Lines: 1,284 |"));
    }

    [Test]
    public void LongHeading_IsClipped()
    {
        var r = Outline(Add("# " + new string('h', 400)));
        Assert.That(r.Outline, Does.Contain(" ... [clipped]"));
    }

    [Test]
    public void BinaryFile_ReturnsError()
    {
        var r = Outline(_storage.Add(new byte[] { 0x50, 0x4B, 0x00, 0x01 }));
        Assert.That(r.Error, Does.StartWith("File appears to be binary"));
        Assert.That(r.Outline, Is.Empty);
    }

    [Test]
    public void Deadline_AddsNote()
    {
        _storage.ReadDelay = TimeSpan.FromMilliseconds(20);
        _sut.ScanDeadline = TimeSpan.FromMilliseconds(250);
        var r = Outline(Add(string.Join("\n", Enumerable.Range(1, 60_000).Select(i => "# H" + i))));
        Assert.That(r.Outline, Does.Match(@"\[scan stopped at line [1-9]\d*: time limit\]"));
        Assert.That(r.Error, Is.Empty);
    }

    [Test]
    public void MissingFile_ReturnsErrorInsteadOfThrowing()
    {
        var r = Outline(new TestFile("nope", 0));
        Assert.That(r.Error, Is.Not.Empty);
    }
}
