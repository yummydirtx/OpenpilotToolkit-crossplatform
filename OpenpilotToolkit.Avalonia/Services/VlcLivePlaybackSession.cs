using LibVLCSharp.Shared;

namespace OpenpilotToolkit.Avalonia.Services;

internal sealed class VlcLivePlaybackSession : IAsyncDisposable
{
    private readonly LibVLC _libVlc;
    private readonly Media _media;
    private readonly StreamMediaInput _mediaInput;
    private readonly QueuedSegmentStream _segmentStream;
    private bool _isDisposing;

    static VlcLivePlaybackSession()
    {
        Core.Initialize();
    }

    private VlcLivePlaybackSession(
        LibVLC libVlc,
        Media media,
        MediaPlayer mediaPlayer,
        StreamMediaInput mediaInput,
        QueuedSegmentStream segmentStream)
    {
        _libVlc = libVlc;
        _media = media;
        MediaPlayer = mediaPlayer;
        _mediaInput = mediaInput;
        _segmentStream = segmentStream;

        MediaPlayer.EndReached += MediaPlayerOnEndReached;
        MediaPlayer.EncounteredError += MediaPlayerOnEncounteredError;
    }

    public MediaPlayer MediaPlayer { get; }

    public event Action? PlaybackEnded;

    public event Action? PlaybackFailed;

    public static VlcLivePlaybackSession Start(string firstSegmentPath, bool isMuted)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(firstSegmentPath);

        var segmentStream = new QueuedSegmentStream();
        segmentStream.AppendSegment(firstSegmentPath);

        try
        {
            var libVlc = new LibVLC("--no-video-title-show");
            var mediaInput = new StreamMediaInput(segmentStream);
            var media = new Media(libVlc, mediaInput, [":demux=ts", ":file-caching=150"]);
            var mediaPlayer = new MediaPlayer(libVlc)
            {
                Mute = isMuted
            };

            if (!mediaPlayer.Play(media))
            {
                mediaPlayer.Dispose();
                media.Dispose();
                mediaInput.Dispose();
                libVlc.Dispose();
                throw new InvalidOperationException("LibVLC failed to start the embedded live playback session.");
            }

            return new VlcLivePlaybackSession(libVlc, media, mediaPlayer, mediaInput, segmentStream);
        }
        catch
        {
            segmentStream.Dispose();
            throw;
        }
    }

    public void AppendSegment(string segmentPath)
    {
        _segmentStream.AppendSegment(segmentPath);
    }

    public void Complete()
    {
        _segmentStream.Complete();
    }

    public void SetMute(bool isMuted)
    {
        MediaPlayer.Mute = isMuted;
    }

    public ValueTask DisposeAsync()
    {
        _isDisposing = true;

        MediaPlayer.EndReached -= MediaPlayerOnEndReached;
        MediaPlayer.EncounteredError -= MediaPlayerOnEncounteredError;

        try
        {
            MediaPlayer.Stop();
        }
        catch
        {
        }

        MediaPlayer.Dispose();
        _media.Dispose();
        _mediaInput.Dispose();
        _segmentStream.Dispose();
        _libVlc.Dispose();

        return ValueTask.CompletedTask;
    }

    private void MediaPlayerOnEndReached(object? sender, EventArgs e)
    {
        if (!_isDisposing)
        {
            PlaybackEnded?.Invoke();
        }
    }

    private void MediaPlayerOnEncounteredError(object? sender, EventArgs e)
    {
        if (!_isDisposing)
        {
            PlaybackFailed?.Invoke();
        }
    }
}
