using OutSystems.ExternalLibraries.SDK;
using OutSystems.ExternalLibraries.SDK.FileStorage;

namespace OdcAgentFileInspector.Tests.FileStorageTesting;

public sealed class TestFile : IOSFile
{
    public TestFile(string key, long size)
    {
        Key = key;
        Size = size;
    }

    public string Key { get; }
    public long Size { get; }
    public string Version => "1";
    public string ContentType => "text/plain";
}

/// <summary>Fake IFileStorage over a dictionary. Streams are deliberately non-seekable, like the real service.</summary>
public sealed class InMemoryFileStorage : IFileStorage
{
    private readonly Dictionary<string, byte[]> _files = new();
    private int _next;

    /// <summary>Delay before each stream read. Lets a test hit the scan deadline.</summary>
    public TimeSpan ReadDelay { get; set; }

    public IOSFile Add(byte[] data)
    {
        var key = "file-" + (++_next);
        _files[key] = data;
        return new TestFile(key, data.Length);
    }

    public Task<Stream> ReadFileAsStreamAsync(IOSFile file, CancellationToken ct = default)
    {
        if (!_files.TryGetValue(file.Key, out var data))
            throw new FileNotFoundException(file.Key);
        return Task.FromResult<Stream>(new ForwardOnlyStream(new MemoryStream(data, writable: false), ReadDelay));
    }

    public Task<byte[]> ReadFileAsync(IOSFile file, CancellationToken ct = default) =>
        Task.FromResult(_files[file.Key]);

    public Task<Stream> ReadFileRangeAsync(IOSFile file, long start, long length, CancellationToken ct = default) =>
        throw new NotImplementedException();

    public Task<IFileMetadata> GetFileMetadataAsync(IOSFile file, CancellationToken ct = default) =>
        throw new NotImplementedException();

    public Task<IOSFile> CreateFileAsync(string name, string contentType, byte[] content, IDictionary<string, string>? metadata = null, CancellationToken ct = default) =>
        Task.FromResult(Add(content));

    public Task<IOSFile> CreateFileFromStreamAsync(string name, string contentType, Stream content, IDictionary<string, string>? metadata = null, CancellationToken ct = default)
    {
        using var ms = new MemoryStream();
        content.CopyTo(ms);
        return Task.FromResult(Add(ms.ToArray()));
    }

    public Task<IOSFile> CreateFileFromWriterAsync(string name, string contentType, Func<Stream, CancellationToken, Task> writer, IDictionary<string, string>? metadata = null, CancellationToken ct = default) =>
        throw new NotImplementedException();

    private sealed class ForwardOnlyStream : Stream
    {
        private readonly Stream _inner;
        private readonly TimeSpan _delay;
        public ForwardOnlyStream(Stream inner, TimeSpan delay)
        {
            _inner = inner;
            _delay = delay;
        }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            if (_delay > TimeSpan.Zero) await Task.Delay(_delay, ct);
            return await _inner.ReadAsync(buffer, ct);
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) _inner.Dispose(); base.Dispose(disposing); }
    }
}
