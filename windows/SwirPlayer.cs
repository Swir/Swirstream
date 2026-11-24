using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace SwirStreamAI;

// ProgressSamples count observations, NOT decoded frame counts. Quiet probes
// observe actual decoded-frame PTS from the pinned mpv fingerprint filter. Visible
// playback observes mpv's video-master timeline while output is configured.
public readonly record struct SwirMediaStats(bool VideoReady, bool AudioReady, int OutputWidth, int OutputHeight,
    long TimeMs, int VideoProgressSamples, int AudioProgressSamples, long DroppedVideoFrames, bool VideoOutputPresent,
    bool AudioTrackSelected = false);

public sealed class SwirPlayer : IDisposable
{
    internal const string EncryptedDecodeFailureMessage = "Nie udało się odczytać szyfrowanej transmisji. Sprawdź klucz lub licencję i obsługiwany format.";
    readonly object gate = new();
    readonly bool quiet;
    readonly EncryptedPlaybackGuard decodeGuard = new();
    bool encryptedPlayback, decodeFault;
    IntPtr handle;
    int state, videoSamples, audioSamples;
    double lastVideoTime = double.NaN, lastAudioTime = double.NaN;
    string lastError = "";
    int playbackGeneration;
    CancellationTokenSource? preparing;
    Task preparation = Task.CompletedTask;
    static readonly object nativeGate = new();
    static IntPtr module, graphicsLifetime;
    static CreateFn create = null!;
    static InitializeFn initialize = null!;
    static DestroyFn destroy = null!;
    static SetStringFn option = null!, set = null!;
    static GetStringFn getString = null!;
    static GetDoubleFn getDouble = null!;
    static GetLongFn getLong = null!;
    static GetFlagFn getFlag = null!;
    static GetNodeFn getNode = null!;
    static FreeNodeFn freeNode = null!;
    static CommandFn command = null!;
    static WaitFn wait = null!;
    static FreeFn free = null!;
    static ErrorFn errorString = null!;
    static RequestLogsFn requestLogs = null!;

    public SwirPlayer(IntPtr hwnd, bool quiet = false)
    {
        this.quiet = quiet || hwnd == IntPtr.Zero;
        LoadNative();
        handle = create();
        if (handle == IntPtr.Zero) throw new InvalidOperationException("Nie udało się utworzyć Swir Player.");
        try
        {
            foreach (var pair in new Dictionary<string,string> {
                ["config"]="no", ["load-scripts"]="no", ["ytdl"]="no", ["osc"]="no", ["terminal"]="no",
                ["input-default-bindings"]="no", ["input-vo-keyboard"]="no", ["input-cursor"]="no", ["input-terminal"]="no",
                ["idle"]="yes", ["keep-open"]="no", ["force-window"]="no", ["audio-display"]="no",
                ["cache"]="yes", ["cache-secs"]="3", ["demuxer-max-bytes"]="33554432", ["demuxer-max-back-bytes"]="8388608",
                ["network-timeout"]="8", ["volume-max"]="100", ["volume"]="65", ["osd-level"]="0",
                ["user-agent"]="SwirStreamAI/0.11.0", ["hwdec"]=this.quiet?"no":"auto-safe"
            }) Check(option(handle, pair.Key, pair.Value), "Ustawienie odtwarzacza " + pair.Key);
            if (this.quiet)
            {
                Check(option(handle, "vo", "null"), "Wyjście obrazu testowego"); Check(option(handle, "ao", "null"), "Wyjście dźwięku testowego");
                Check(option(handle, "vf", "@swir_probe:fingerprint=type=gray-hex-8x8:clear-on-query=no"), "Pomiar zdekodowanych klatek");
            }
            else
            {
                Check(option(handle, "wid", hwnd.ToInt64().ToString(CultureInfo.InvariantCulture)), "Osadzenie obrazu");
                Check(option(handle, "vo", "gpu,direct3d"), "Wyjście obrazu");
                Check(option(handle, "gpu-api", "d3d11"), "Wyświetlanie Direct3D");
            }
            Check(initialize(handle), "Uruchomienie Swir Player");
        }
        catch { destroy(handle); handle = IntPtr.Zero; throw; }
    }
    static void LoadNative()
    {
        lock (nativeGate)
        {
            if (module != IntPtr.Zero) return;
            string folder = Path.Combine(AppContext.BaseDirectory,"player");
            string dll = Path.Combine(folder,"libmpv-2.dll");
            VerifyEngine(folder);
            // Process-owned references prevent late native callbacks into unloaded GPU/runtime code.
            graphicsLifetime = NativeLibrary.Load(Path.Combine(Environment.SystemDirectory,"d3d11.dll"));
            module = NativeLibrary.Load(dll);
            T Bind<T>(string name) where T:Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(module,name));
            create=Bind<CreateFn>("mpv_create"); initialize=Bind<InitializeFn>("mpv_initialize"); destroy=Bind<DestroyFn>("mpv_terminate_destroy");
            option=Bind<SetStringFn>("mpv_set_option_string"); set=Bind<SetStringFn>("mpv_set_property_string"); getString=Bind<GetStringFn>("mpv_get_property_string");
            getDouble=Bind<GetDoubleFn>("mpv_get_property"); getLong=Bind<GetLongFn>("mpv_get_property"); getFlag=Bind<GetFlagFn>("mpv_get_property");
            getNode=Bind<GetNodeFn>("mpv_get_property");freeNode=Bind<FreeNodeFn>("mpv_free_node_contents");
            command=Bind<CommandFn>("mpv_command"); wait=Bind<WaitFn>("mpv_wait_event"); free=Bind<FreeFn>("mpv_free"); errorString=Bind<ErrorFn>("mpv_error_string");
            requestLogs=Bind<RequestLogsFn>("mpv_request_log_messages");
        }
    }
    public static void VerifyEngine(string folder)
    {
        string dll = Path.Combine(folder,"libmpv-2.dll"), digestPath = dll + ".sha256";
        if (!File.Exists(dll) || !File.Exists(digestPath)) throw new FileNotFoundException("Brak silnika Swir Player. Rozpakuj pełną paczkę razem z folderem player.");
        string expected = File.ReadAllText(digestPath).Trim().Split(' ',StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        using var file = File.OpenRead(dll);
        if (expected.Length != 64 || !Convert.ToHexString(SHA256.HashData(file)).Equals(expected,StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Uszkodzona biblioteka Swir Player. Rozpakuj ponownie pełną paczkę.");
    }
    static void Check(int result, string operation)
    {
        if(result<0) throw new InvalidOperationException(operation + ": " + (errorString == null ? result.ToString() : Marshal.PtrToStringUTF8(errorString(result))));
    }
    string String(string name)
    {
        if(handle==IntPtr.Zero) return "";
        var p=getString(handle,name); if(p==IntPtr.Zero) return "";
        try { return Marshal.PtrToStringUTF8(p)??""; } finally {free(p);}
    }
    double? Number(string name) => handle!=IntPtr.Zero && getDouble(handle,name,5,out double value)>=0 && double.IsFinite(value)?value:null;
    long Integer(string name) => handle!=IntPtr.Zero && getLong(handle,name,4,out long value)>=0?value:0;
    bool Flag(string name) => handle!=IntPtr.Zero && getFlag(handle,name,3,out int value)>=0 && value!=0;
    double? DecodedFrameTime()
    {
        if(getNode(handle,"vf-metadata/swir_probe",6,out var root)<0)return null;
        try
        {
            if(root.Format!=8||root.Value==IntPtr.Zero)return null;
            var list=Marshal.PtrToStructure<MpvNodeList>(root.Value);double? latest=null;
            for(int i=0;i<Math.Min(list.Count,64);i++)
            {
                string key=Marshal.PtrToStringUTF8(Marshal.ReadIntPtr(list.Keys,i*IntPtr.Size))??"";
                var node=Marshal.PtrToStructure<MpvNode>(IntPtr.Add(list.Values,i*Marshal.SizeOf<MpvNode>()));
                if(key.EndsWith(".pts",StringComparison.Ordinal)&&node.Format==1&&double.TryParse(Marshal.PtrToStringUTF8(node.Value),NumberStyles.Float,CultureInfo.InvariantCulture,out double pts)&&double.IsFinite(pts))latest=latest.HasValue?Math.Max(latest.Value,pts):pts;
            }
            return latest;
        }
        finally{freeNode(ref root);}
    }
    void Set(string name,string value) { if(handle!=IntPtr.Zero) Check(set(handle,name,value),name); }
    void Command(params string[] args)
    {
        if(handle==IntPtr.Zero) throw new ObjectDisposedException(nameof(SwirPlayer));
        var native=args.Select(Marshal.StringToCoTaskMemUTF8).Append(IntPtr.Zero).ToArray();
        try {Check(command(handle,native),args[0]);} finally {foreach(var p in native) if(p!=IntPtr.Zero) Marshal.FreeCoTaskMem(p);}
    }
    void Pump()
    {
        if(handle==IntPtr.Zero) return;
        // The pinned client has a bounded 1000-message error buffer. Drain at
        // most that buffer plus normal events, so a decoder error flood cannot
        // starve lifecycle events or leave old errors across a source switch.
        for(int i=0;i<1152;i++)
        {
            var p=wait(handle,0); if(p==IntPtr.Zero)break; var e=Marshal.PtrToStructure<MpvEvent>(p); if(e.Id==0)break;
            if(e.Id==2&&e.Data!=IntPtr.Zero)
            {
                if(encryptedPlayback&&!decodeFault)
                {
                    var message=Marshal.PtrToStructure<MpvLogMessage>(e.Data);
                    // Read only the native module and severity. Message text can
                    // contain provider credentials and is never copied or logged.
                    var category=EncryptedPlaybackGuard.Classify(Marshal.PtrToStringUTF8(message.Prefix)??"",message.Level);
                    decodeGuard.Error(category,Environment.TickCount64);
                }
                continue;
            }
            if(decodeFault)continue;
            if(e.Id==6) state=1;
            else if(e.Id==8 || e.Id==21) state=3;
            else if(e.Id==7 && e.Data!=IntPtr.Zero)
            {
                int reason=Marshal.ReadInt32(e.Data), error=Marshal.ReadInt32(e.Data,4);
                if(reason==4) {state=7; lastError=Marshal.PtrToStringUTF8(errorString(error))??"Błąd transmisji";}
                else if(reason==0) state=6; else if(reason is 2 or 3)state=5;
            }
        }
        if(encryptedPlayback&&!decodeFault&&state is 1 or 2 or 3 or 4)
        {
            bool suspended=Flag("pause")||Flag("paused-for-cache")||Flag("seeking");
            if(decodeGuard.Observe(Environment.TickCount64,suspended,Integer("current-tracks/audio/id"),Number("audio-pts"),
                Integer("current-tracks/video/id"),Number("time-pos"))) FailEncryptedPlayback();
        }
    }
    void FailEncryptedPlayback()
    {
        decodeFault=true;encryptedPlayback=false;CancelPreparation();
        // Disabling log delivery destroys the pinned mpv client's log buffer.
        // Stop/end events must not overwrite this sticky failure with State 5.
        requestLogs(handle,"no");
        try{Set("options/demuxer-lavf-o","");Command("stop");}
        finally{state=7;lastError=EncryptedDecodeFailureMessage;videoSamples=audioSamples=0;lastVideoTime=lastAudioTime=double.NaN;}
    }
    public void Play(string url,string userAgent="",string referrer="",string qualityMode="Stabilność",string clearKeyHex="")
    {
        if(!TextTools.IsWeb(url) && !File.Exists(url))throw new ArgumentException("Podaj bezpośredni adres HTTP/HTTPS albo istniejący plik.");
        if(clearKeyHex.Length>0&&(clearKeyHex.Length!=32||clearKeyHex.Any(c=>!Uri.IsHexDigit(c))))throw new ArgumentException("Nieprawidłowy klucz CENC.");
        lock(gate)
        {
            Stop(); Pump(); videoSamples=audioSamples=0;lastVideoTime=lastAudioTime=double.NaN;lastError="";
            encryptedPlayback=clearKeyHex.Length>0;decodeFault=false;decodeGuard.Reset(Environment.TickCount64);
            Check(requestLogs(handle,encryptedPlayback?"error":"no"),"Kontrola dekodera");
            Set("options/demuxer-lavf-o",clearKeyHex.Length>0?"cenc_decryption_key="+clearKeyHex:"");
            string ua=userAgent.Length is >0 and <1024 && !userAgent.Contains('\r') && !userAgent.Contains('\n')?userAgent:"SwirStreamAI/0.11.0";
            string refer=TextTools.IsWeb(referrer)&&!referrer.Contains('\r')&&!referrer.Contains('\n')?referrer:"";
            Set("options/user-agent",ua);Set("options/referrer",refer);Set("pause","no");state=1;
            if(HlsPlaybackSource.Candidate(url))
            {
                preparing=new CancellationTokenSource();
                preparation=PrepareAndPlay(url,ua,refer,qualityMode,playbackGeneration,preparing);
            }
            else LoadSource(TextTools.IsWeb(url)?url:Path.GetFullPath(url));
        }
    }
    async Task PrepareAndPlay(string url,string userAgent,string referrer,string qualityMode,int generation,CancellationTokenSource cancellation)
    {
        try
        {
            string source=await HlsPlaybackSource.Prepare(url,userAgent,referrer,cancellation.Token,qualityMode).ConfigureAwait(false);
            lock(gate)
            {
                if(handle==IntPtr.Zero||generation!=playbackGeneration||cancellation.IsCancellationRequested)return;
                LoadSource(source);
            }
        }
        catch(OperationCanceledException){}
        catch(Exception ex){lock(gate){if(handle!=IntPtr.Zero&&generation==playbackGeneration){lastError=ex.Message;state=7;}}}
        finally{
            lock(gate){
                if(ReferenceEquals(preparing,cancellation)){preparing=null;cancellation.Dispose();}
            }
        }
    }
    void LoadSource(string source)
    {
        bool master=source.StartsWith("memory://#EXTM3U",StringComparison.Ordinal);
        // Otherwise memory:// is parsed as a plain playlist, losing external audio.
        Set("options/demuxer",master?"lavf":"");
        Set("options/demuxer-lavf-format",master?"hls":"");
        Set("options/demuxer-lavf-probe-info",master?"nostreams":"auto");
        Command("loadfile",source,"replace");
    }
    void CancelPreparation(){
        playbackGeneration++;
        var current=preparing;
        if(current==null)return;
        preparing=null;
        // HTTP cancellation can complete a continuation which needs the player lock.
        // CancelAsync signals immediately and runs those callbacks outside this lock.
        _=FinishCancelledPreparation(current,preparation);
    }
    static async Task FinishCancelledPreparation(CancellationTokenSource source,Task work){
        try{await source.CancelAsync().ConfigureAwait(false);}
        finally{
            try{await work.ConfigureAwait(false);}
            finally{source.Dispose();}
        }
    }
    public string Version {get{lock(gate)return "Swir Player / " + String("mpv-version") + " / FFmpeg " + String("ffmpeg-version");}}
    public string LastError {get{lock(gate){Pump();return lastError;}}}
    public int State {get{lock(gate){if(handle==IntPtr.Zero)return 0;Pump();if(state is 1 or 2 or 3 or 4){if(Flag("pause"))return 4;if(Flag("paused-for-cache"))return 2;}return state;}}}
    public long Time {get{lock(gate)return (long)Math.Round((Number("time-pos")??0)*1000);}}
    public long Length {get{lock(gate)return (long)Math.Round((Number("duration")??0)*1000);}}
    public float Position {get{lock(gate)return (float)Math.Clamp((Number("percent-pos")??0)/100,0,1);}set{lock(gate){if(!float.IsFinite(value)||handle==IntPtr.Zero)return;Command("seek",(Math.Clamp(value,0,1)*100).ToString(CultureInfo.InvariantCulture),"absolute-percent+exact");lastVideoTime=lastAudioTime=double.NaN;decodeGuard.Reset(Environment.TickCount64);}}}
    public bool Seekable {get{lock(gate)return Flag("seekable");}}
    public int Volume {get{lock(gate)return (int)Math.Round(Number("volume")??0);}set{lock(gate)Set("volume",Math.Clamp(value,0,100).ToString(CultureInfo.InvariantCulture));}}
    public bool Muted {get{lock(gate)return Flag("mute");}set{lock(gate)Set("mute",value?"yes":"no");}}
    public SwirMediaStats Stats
    {
        get
        {
            lock(gate)
            {
                if(handle==IntPtr.Zero)return default;Pump();if(state is 0 or 5 or 6 or 7)return default;
                double? timeline=Number("time-pos"),videoTime=quiet?DecodedFrameTime():timeline,audioTime=Number("audio-pts");
                int w=(int)Integer("video-out-params/w"),h=(int)Integer("video-out-params/h");
                bool video=w>0&&h>0&&String("video-dec-params/pixelformat").Length>0;
                bool audio=Integer("audio-out-params/samplerate")>0 && String("audio-params/format").Length>0;
                if(video && videoTime.HasValue && state==3 && !Flag("core-idle") && !Flag("pause") && (!double.IsFinite(lastVideoTime)||videoTime.Value>lastVideoTime+.02)) {if(double.IsFinite(lastVideoTime))videoSamples++;lastVideoTime=videoTime.Value;}
                if(audio && audioTime.HasValue && state==3 && !Flag("core-idle") && !Flag("pause") && (!double.IsFinite(lastAudioTime)||audioTime.Value>lastAudioTime+.02)) {if(double.IsFinite(lastAudioTime))audioSamples++;lastAudioTime=audioTime.Value;}
                return new(video,audio,w,h,(long)Math.Round((timeline??audioTime??0)*1000),videoSamples,audioSamples,Integer("frame-drop-count"),video&&!quiet&&String("current-vo").Length>0,Integer("current-tracks/audio/id")>0);
            }
        }
    }
    public void Pause(){lock(gate){Set("pause","yes");decodeGuard.Reset(Environment.TickCount64);}}
    public void Resume(){lock(gate){Set("pause","no");decodeGuard.Reset(Environment.TickCount64);}}
    public void Stop(){lock(gate){CancelPreparation();encryptedPlayback=false;decodeGuard.Reset(Environment.TickCount64);if(handle==IntPtr.Zero)return;requestLogs(handle,"no");Command("stop");Set("options/demuxer-lavf-o","");Pump();decodeFault=false;state=5;videoSamples=audioSamples=0;lastVideoTime=lastAudioTime=double.NaN;}}
    public void SaveFrame(string path){lock(gate)Command("screenshot-to-file",Path.GetFullPath(path),"video");}
    public void Dispose(){lock(gate){CancelPreparation();if(handle==IntPtr.Zero)return;var old=handle;handle=IntPtr.Zero;state=0;destroy(old);}}

    [StructLayout(LayoutKind.Sequential)] struct MpvEvent {public int Id,Error;public ulong Reply;public IntPtr Data;}
    [StructLayout(LayoutKind.Sequential)] struct MpvLogMessage {public IntPtr Prefix,LevelText,Text;public int Level;}
    [StructLayout(LayoutKind.Sequential)] struct MpvNode {public IntPtr Value;public int Format;}
    [StructLayout(LayoutKind.Sequential)] struct MpvNodeList {public int Count;public IntPtr Values,Keys;}
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr CreateFn();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int InitializeFn(IntPtr p);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void DestroyFn(IntPtr p);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int SetStringFn(IntPtr p,[MarshalAs(UnmanagedType.LPUTF8Str)]string name,[MarshalAs(UnmanagedType.LPUTF8Str)]string value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr GetStringFn(IntPtr p,[MarshalAs(UnmanagedType.LPUTF8Str)]string name);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int GetDoubleFn(IntPtr p,[MarshalAs(UnmanagedType.LPUTF8Str)]string name,int format,out double value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int GetLongFn(IntPtr p,[MarshalAs(UnmanagedType.LPUTF8Str)]string name,int format,out long value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int GetFlagFn(IntPtr p,[MarshalAs(UnmanagedType.LPUTF8Str)]string name,int format,out int value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int GetNodeFn(IntPtr p,[MarshalAs(UnmanagedType.LPUTF8Str)]string name,int format,out MpvNode value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void FreeNodeFn(ref MpvNode value);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int CommandFn(IntPtr p,[In]IntPtr[] args);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr WaitFn(IntPtr p,double timeout);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void FreeFn(IntPtr p);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate IntPtr ErrorFn(int error);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate int RequestLogsFn(IntPtr p,[MarshalAs(UnmanagedType.LPUTF8Str)]string level);
}
