namespace SwirStreamAI;

public readonly record struct PlaybackProgressState(bool Confirmed, bool Advancing);

// Track progress, never pixel brightness or image changes. A static/black picture
// with advancing decoded timestamps is valid; an absent selected audio track is not.
public sealed class PlaybackProgress
{
    int previousVideo, previousAudio;
    long previousAt = -1, videoAt = -1, audioAt = -1, activeMs;
    bool previouslyReady;
    int previousState, requiredTracks;
    public long StartupElapsedMs { get; private set; }
    public void Reset()
    {
        previousVideo = previousAudio = 0;
        previousAt = videoAt = audioAt = -1;
        activeMs = StartupElapsedMs = 0; previousState = requiredTracks = 0; previouslyReady = false;
    }
    public PlaybackProgressState Observe(int state, SwirMediaStats stats, string kind, bool encrypted, long nowMs)
    {
        long elapsed = previousAt < 0 ? 0 : Math.Clamp(nowMs - previousAt, 0, 1500);
        if (previousAt >= 0 && state != 4 && previousState != 4) StartupElapsedMs += Math.Max(0, nowMs - previousAt);
        previousState = state;
        bool videoAdvanced = stats.VideoProgressSamples > previousVideo;
        bool audioAdvanced = stats.AudioProgressSamples > previousAudio;
        bool countersReset = stats.VideoProgressSamples < previousVideo || stats.AudioProgressSamples < previousAudio;
        previousVideo = stats.VideoProgressSamples; previousAudio = stats.AudioProgressSamples; previousAt = nowMs;
        if (countersReset) { videoAt = audioAt = -1; activeMs = 0; previouslyReady = false; }
        if (state != 3)
        {
            videoAt = audioAt = -1; previouslyReady = false;
            return default;
        }
        bool radio = kind.Equals("Radio", StringComparison.OrdinalIgnoreCase);
        bool requireVideo = kind.Equals("TV", StringComparison.OrdinalIgnoreCase) || !radio && stats.VideoReady;
        bool requireAudio = radio || stats.AudioTrackSelected;
        int tracks = (requireVideo ? 1 : 0) | (requireAudio ? 2 : 0);
        if (tracks != requiredTracks)
        {
            requiredTracks = tracks; activeMs = 0; previouslyReady = false;
            videoAt = audioAt = -1;
        }
        if (videoAdvanced) videoAt = nowMs;
        if (audioAdvanced) audioAt = nowMs;
        bool ready = (requireVideo || requireAudio)
            && (!requireVideo || stats.VideoReady && stats.VideoProgressSamples >= 2 && videoAt >= 0 && nowMs - videoAt <= 1500)
            && (!requireAudio || stats.AudioReady && stats.AudioProgressSamples >= 2 && audioAt >= 0 && nowMs - audioAt <= 1500);
        bool advancing = ready && (!requireVideo || videoAdvanced) && (!requireAudio || audioAdvanced);
        if (advancing && previouslyReady) activeMs += elapsed;
        previouslyReady = ready;
        // This conservative confirmation delay covers the bundled CENC fixture's
        // clear lead; it is not proof for arbitrarily long unencrypted prefixes.
        bool confirmed = ready && (!encrypted || activeMs >= 12000 && stats.TimeMs >= 12000);
        return new(confirmed, advancing);
    }
}
