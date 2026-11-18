using System.Text;
using OdcAgentFileInspector.Tests.FileStorageTesting;
using OutSystems.ExternalLibraries.SDK;
using OutSystems.ExternalLibraries.SDK.FileStorage;

namespace OdcAgentFileInspector.Tests;

[TestFixture, NonParallelizable]
public class ReadFileLinesTests
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

    private record R(string Content, int Total, bool More, string Error);

    private IOSFile Add(string text) => _storage.Add(Encoding.UTF8.GetBytes(text));

    private static string Lines(int count, Func<int, string>? make = null) =>
        string.Join("\n", Enumerable.Range(1, count).Select(make ?? (i => "l" + i)));

    private R Read(IOSFile f, int start, int end)
    {
        _sut.ReadFileLines(f, start, end, out var c, out var n, out var more, out var e);
        return new R(c, n, more, e);
    }

    [Test]
    public void NormalRange_NumbersLines_AndTotalIsExact()
    {
        var r = Read(Add(Lines(10)), 3, 5);
        Assert.That(r.Content, Is.EqualTo("3: l3\n4: l4\n5: l5"));
        Assert.That(r.Total, Is.EqualTo(10));
        Assert.That(r.More, Is.True);
        Assert.That(r.Error, Is.Empty);
    }

    [TestCase(0)]
    [TestCase(-7)]
    public void StartLine_BelowOne_BecomesOne(int start)
    {
        var r = Read(Add(Lines(5)), start, 2);
        Assert.That(r.Content, Is.EqualTo("1: l1\n2: l2"));
    }

    [Test]
    public void EndLine_BeforeStart_BecomesStart()
    {
        var r = Read(Add(Lines(10)), 4, 1);
        Assert.That(r.Content, Is.EqualTo("4: l4"));
    }

    [Test]
    public void Span_Over200_IsCappedAtStartPlus199()
    {
        var r = Read(Add(Lines(500)), 10, 9999);
        var lines = r.Content.Split('\n');
        Assert.That(lines, Has.Length.EqualTo(200));
        Assert.That(lines[0], Is.EqualTo("10: l10"));
        Assert.That(lines[^1], Is.EqualTo("209: l209"));
        Assert.That(r.More, Is.True);
        Assert.That(r.Total, Is.EqualTo(500));
    }

    [Test]
    public void StartLine_BeyondEof_ReturnsError()
    {
        var r = Read(Add(Lines(3)), 4, 10);
        Assert.That(r.Content, Is.Empty);
        Assert.That(r.Error, Is.EqualTo("StartLine 4 is beyond the end of the file (3 lines)."));
        Assert.That(r.Total, Is.EqualTo(3));
        Assert.That(r.More, Is.False);
    }

    [Test]
    public void EmptyFile_StartLineOne_IsBeyondEof()
    {
        var r = Read(_storage.Add(Array.Empty<byte>()), 1, 5);
        Assert.That(r.Error, Is.EqualTo("StartLine 1 is beyond the end of the file (0 lines)."));
        Assert.That(r.Total, Is.EqualTo(0));
    }

    [Test]
    public void HasMoreBelow_FalseWhenRangeReachesLastLine()
    {
        var r = Read(Add(Lines(5)), 4, 5);
        Assert.That(r.More, Is.False);
        Assert.That(Read(Add(Lines(5)), 4, 100).More, Is.False);
    }

    [Test]
    public void HasMoreBelow_TrueWhenLinesRemain()
    {
        Assert.That(Read(Add(Lines(5)), 1, 4).More, Is.True);
    }

    [Test]
    public void LongLine_ClippedAt500()
    {
        var r = Read(Add("a" + new string('b', 800)), 1, 1);
        Assert.That(r.Content, Is.EqualTo("1: " + ("a" + new string('b', 800)).Substring(0, 500) + " ... [clipped]"));
    }

    [Test]
    public void Line_Of500Chars_NotClipped()
    {
        var r = Read(Add(new string('c', 500)), 1, 1);
        Assert.That(r.Content, Does.Not.Contain("[clipped]"));
    }

    [Test]
    public void Output_CappedAt64KB()
    {
        var line = new string('z', 500);
        var r = Read(Add(Lines(300, _ => line)), 1, 200);
        Assert.That(Encoding.UTF8.GetByteCount(r.Content), Is.LessThanOrEqualTo(64 * 1024 + 100));
        Assert.That(r.Content, Does.EndWith("[output truncated at 64 KB]"));
        Assert.That(r.Total, Is.EqualTo(300));
    }

    [TestCase("a\nb\nc\n")]
    [TestCase("a\r\nb\r\nc\r\n")]
    [TestCase("a\rb\rc\r")]
    [TestCase("a\nb\nc")]
    public void LineEndings_TotalLinesIsThree(string content)
    {
        var r = Read(Add(content), 2, 2);
        Assert.That(r.Content, Is.EqualTo("2: b"));
        Assert.That(r.Total, Is.EqualTo(3));
    }

    [Test]
    public void BinaryFile_ReturnsError()
    {
        var r = Read(_storage.Add(new byte[] { 1, 0, 2 }), 1, 1);
        Assert.That(r.Error, Does.StartWith("File appears to be binary"));
        Assert.That(r.Content, Is.Empty);
    }

    [Test]
    public void StartLine_AtIntMax_DoesNotOverflow()
    {
        var r = Read(Add(Lines(3)), int.MaxValue, int.MaxValue);
        Assert.That(r.Error, Does.StartWith("StartLine 2147483647 is beyond the end"));
    }

    [Test]
    public void Deadline_ReturnsPartialContentWithNote()
    {
        _storage.ReadDelay = TimeSpan.FromMilliseconds(20);
        _sut.ScanDeadline = TimeSpan.FromMilliseconds(250);
        var r = Read(Add(Lines(60_000, i => "filler line " + i)), 1, 3);
        Assert.That(r.Content, Does.StartWith("1: filler line 1\n"));
        Assert.That(r.Content, Does.Match(@"\[scan stopped at line [1-9]\d*: time limit\]"));
        Assert.That(r.Error, Is.Empty);
    }

    [Test]
    public void MissingFile_ReturnsErrorInsteadOfThrowing()
    {
        var r = Read(new TestFile("nope", 0), 1, 1);
        Assert.That(r.Error, Is.Not.Empty);
    }
}
