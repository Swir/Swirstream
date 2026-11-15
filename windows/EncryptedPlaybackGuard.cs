namespace SwirStreamAI;

// CENC has no authenticated plaintext check. A decoder can conceal corrupt packets
// and emit advancing PTS, so neither timestamps nor black/static pixels establish
// successful decryption. This bounded policy needs sustained native decoder errors.
// It never retains log text, URLs, keys or packet data.
internal sealed class EncryptedPlaybackGuard
{
    internal enum Track { None, Audio, Video }
    sealed class ErrorBucket(long bucket) { internal readonly long Bucket=bucket; internal int Count=1; }
    sealed class TrackWindow
    {
        internal readonly Queue<ErrorBucket> Errors = new();
        internal ErrorBucket? Tail;
        internal long SelectedId, LastProgress, LastError;
        internal double? Position;
        internal void Reset(long now, long id = 0)
        { Errors.Clear(); Tail=null; SelectedId=id; LastProgress=now; LastError=long.MinValue; Position=null; }
    }
    readonly TrackWindow audio = new(), video = new();
    internal static Track Classify(string prefix, int level) => level is not (10 or 20) ? Track.None : prefix switch
    {
        "ad" or "ffmpeg/audio" => Track.Audio,
        "vd" or "ffmpeg/video" => Track.Video,
        _ => Track.None
    };
    internal void Reset(long now) { audio.Reset(now); video.Reset(now); }
    internal void Error(Track track, long now)
    {
        if(track==Track.None)return;
        var window=track==Track.Audio?audio:video;
        long bucket=now/250;
        // A maximum of 21 time buckets and 256 errors per bucket bounds memory
        // even if corrupt input produces thousands of native error messages.
        Prune(window,now);
        if(window.Errors.Count>0&&window.Tail?.Bucket==bucket)window.Tail.Count=Math.Min(256,window.Tail.Count+1);
        else{window.Tail=new(bucket);window.Errors.Enqueue(window.Tail);}
        window.LastError=now;
    }
    static void Prune(TrackWindow window,long now)
    {while(window.Errors.TryPeek(out var first)&&now-first.Bucket*250>5000)window.Errors.Dequeue();}
    static bool Failed(TrackWindow window,long now,long id,double? position)
    {
        if(id<=0){window.Reset(now);return false;}
        // Track changes get their own startup grace. Errors received before the
        // selection was observable do not count against the replacement track.
        if(window.SelectedId!=id){window.Reset(now,id);return false;}
        if(position.HasValue&&double.IsFinite(position.Value)&&(!window.Position.HasValue||position.Value>window.Position.Value+.02))
        {window.Position=position;window.LastProgress=now;}
        Prune(window,now);
        if(window.Errors.Count<4||now-window.LastError>1000)return false;
        int count=window.Errors.Sum(item=>item.Count);long span=(window.Tail!.Bucket-window.Errors.Peek().Bucket)*250;
        bool stalled=count>=12&&span>=2000&&now-window.LastProgress>=3000;
        // Persistent dense failures remain failures when error concealment emits
        // artificial frames/PTS. A single damaged packet or short burst is exempt.
        bool persistent=count>=60&&window.Errors.Count>=8&&span>=3000;
        return stalled||persistent;
    }
    internal bool Observe(long now,bool suspended,long audioId,double? audioPosition,long videoId,double? videoPosition)
    {
        if(suspended){Reset(now);return false;}
        bool audioFailed=Failed(audio,now,audioId,audioPosition);
        bool videoFailed=Failed(video,now,videoId,videoPosition);
        return audioFailed||videoFailed;
    }
}
