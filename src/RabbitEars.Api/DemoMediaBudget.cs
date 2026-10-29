using System.Diagnostics;

namespace RabbitEars.Api;

/// <summary>Bounds active media responses and paces all their reads against one byte budget.</summary>
public sealed class DemoMediaBudget(BoxOptions options)
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, int> _active = [];
    private readonly SemaphoreSlim _readGate = new(1, 1);
    private int _total;
    private long _nextRead;

    public IDisposable? Enter(string address)
    {
        lock (_gate)
        {
            var count = _active.GetValueOrDefault(address);
            if (_total >= options.Demo.MaxMediaResponses || count >= options.Demo.MaxMediaResponsesPerAddress)
            {
                DemoMetrics.Reject("media_responses");
                return null;
            }
            _total++;
            _active[address] = count + 1;
            return new Lease(() =>
            {
                lock (_gate)
                {
                    _total--;
                    if (--_active[address] == 0) _active.Remove(address);
                }
            });
        }
    }

    public async ValueTask Pace(int bytes, CancellationToken cancel)
    {
        await _readGate.WaitAsync(cancel);
        try
        {
            var now = Stopwatch.GetTimestamp();
            if (_nextRead > now)
                await Task.Delay(TimeSpan.FromSeconds((double)(_nextRead - now) / Stopwatch.Frequency), cancel);
            _nextRead = Stopwatch.GetTimestamp() + (long)((double)bytes / Math.Max(1, options.Demo.MediaBytesPerSecond) * Stopwatch.Frequency);
        }
        finally { _readGate.Release(); }
    }

    private sealed class Lease(Action release) : IDisposable
    {
        private Action? _release = release;
        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }
}

/// <summary>Preserves file seek/range semantics while limiting read buffers and aggregate throughput.</summary>
public sealed class BudgetedMediaStream(Stream inner, DemoMediaBudget budget) : Stream
{
    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => inner.CanSeek;
    public override bool CanWrite => false;
    public override long Length => inner.Length;
    public override long Position { get => inner.Position; set => inner.Position = value; }
    public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
    public override void Flush() => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException("Use asynchronous reads.");
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var size = Math.Min(buffer.Length, 32 * 1024);
        await budget.Pace(size, cancellationToken);
        return await inner.ReadAsync(buffer[..size], cancellationToken);
    }
    protected override void Dispose(bool disposing)
    {
        if (disposing) inner.Dispose();
        base.Dispose(disposing);
    }
}
