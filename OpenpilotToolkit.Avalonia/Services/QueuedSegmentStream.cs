using System.Collections.Generic;

namespace OpenpilotToolkit.Avalonia.Services;

internal sealed class QueuedSegmentStream : Stream
{
    private readonly Queue<string> _queuedSegmentPaths = new();
    private readonly object _syncLock = new();

    private FileStream? _currentStream;
    private bool _isCompleted;
    private bool _isDisposed;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public void AppendSegment(string segmentPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(segmentPath);

        lock (_syncLock)
        {
            ThrowIfDisposed();

            if (_isCompleted)
            {
                throw new InvalidOperationException("Cannot append segments after playback input has been completed.");
            }

            _queuedSegmentPaths.Enqueue(segmentPath);
            Monitor.PulseAll(_syncLock);
        }
    }

    public void Complete()
    {
        lock (_syncLock)
        {
            if (_isDisposed)
            {
                return;
            }

            _isCompleted = true;
            Monitor.PulseAll(_syncLock);
        }
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(offset + count, buffer.Length);

        if (count == 0)
        {
            return 0;
        }

        lock (_syncLock)
        {
            while (true)
            {
                ThrowIfDisposed();

                if (_currentStream is not null)
                {
                    var bytesRead = _currentStream.Read(buffer, offset, count);
                    if (bytesRead > 0)
                    {
                        return bytesRead;
                    }

                    _currentStream.Dispose();
                    _currentStream = null;
                    continue;
                }

                if (_queuedSegmentPaths.Count > 0)
                {
                    _currentStream = File.Open(
                        _queuedSegmentPaths.Dequeue(),
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.Read);
                    continue;
                }

                if (_isCompleted)
                {
                    return 0;
                }

                Monitor.Wait(_syncLock);
            }
        }
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotSupportedException();
    }

    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }

    protected override void Dispose(bool disposing)
    {
        lock (_syncLock)
        {
            _isDisposed = true;
            _isCompleted = true;

            _currentStream?.Dispose();
            _currentStream = null;
            _queuedSegmentPaths.Clear();

            Monitor.PulseAll(_syncLock);
        }

        base.Dispose(disposing);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
    }
}
