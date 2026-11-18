using System.Text;
using OutSystems.ExternalLibraries.SDK;
using OutSystems.ExternalLibraries.SDK.FileStorage;

namespace OdcAgentFileInspector.Internal;

internal sealed record ScanResult(bool IsBinary, int LinesRead, long Bytes, bool TimedOut, bool StoppedByCaller);

/// <summary>Streams an ODC File line by line. Never loads the whole file.</summary>
internal static class LineScanner
{
    /// <summary>
    /// Calls <paramref name="onLine"/>(lineNo, text) for each line; return false to stop early.
    /// </summary>
    public static async Task<ScanResult> ScanAsync(IOSFile file, Func<int, string, bool> onLine, TimeSpan? deadline = null)
    {
        using var cts = new CancellationTokenSource(deadline ?? Limits.ScanDeadline);
        var ct = cts.Token;

        int lineNo = 0;
        ReplayStream? replay = null;
        try
        {
            await using var raw = await FileStorage.GetInstance().ReadFileAsStreamAsync(file, ct);

            // Sniff the first bytes without assuming the stream is seekable.
            var prefix = new byte[Limits.BinarySniffBytes];
            int got = 0;
            while (got < prefix.Length)
            {
                int n = await raw.ReadAsync(prefix.AsMemory(got), ct);
                if (n == 0) break;
                got += n;
            }

            if (LooksBinary(prefix, got))
                return new ScanResult(true, 0, got, false, false);

            replay = new ReplayStream(prefix, got, raw);
            using var reader = new StreamReader(replay, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 16 * 1024);

            while (true)
            {
                var line = await reader.ReadLineAsync(ct);
                if (line is null) break;
                lineNo++;
                if (!onLine(lineNo, line))
                    return new ScanResult(false, lineNo, replay.TotalBytes, false, true);
            }
            return new ScanResult(false, lineNo, replay.TotalBytes, false, false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return new ScanResult(false, lineNo, replay?.TotalBytes ?? 0, true, false);
        }
        finally
        {
            if (replay is not null) await replay.DisposeAsync();
        }
    }

    private static bool LooksBinary(byte[] buf, int count)
    {
        if (count >= 2 && ((buf[0] == 0xFF && buf[1] == 0xFE) || (buf[0] == 0xFE && buf[1] == 0xFF)))
            return false; // UTF-16 BOM: NUL bytes are expected
        for (int i = 0; i < count; i++)
            if (buf[i] == 0x00) return true;
        return false;
    }

    /// <summary>Replays already-read bytes, then continues with the inner stream. Counts every byte served.</summary>
    private sealed class ReplayStream : Stream
    {
        private readonly byte[] _prefix;
        private readonly int _prefixLen;
        private readonly Stream _inner;
        private int _pos;

        public ReplayStream(byte[] prefix, int prefixLen, Stream inner)
        {
            _prefix = prefix;
            _prefixLen = prefixLen;
            _inner = inner;
        }

        public long TotalBytes { get; private set; }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count) =>
            Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            if (_pos < _prefixLen)
            {
                int n = Math.Min(buffer.Length, _prefixLen - _pos);
                _prefix.AsSpan(_pos, n).CopyTo(buffer);
                _pos += n;
                TotalBytes += n;
                return n;
            }
            int r = _inner.Read(buffer);
            TotalBytes += r;
            return r;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_pos < _prefixLen)
            {
                int n = Math.Min(buffer.Length, _prefixLen - _pos);
                _prefix.AsMemory(_pos, n).CopyTo(buffer);
                _pos += n;
                TotalBytes += n;
                return n;
            }
            int r = await _inner.ReadAsync(buffer, cancellationToken);
            TotalBytes += r;
            return r;
        }
    }
}
