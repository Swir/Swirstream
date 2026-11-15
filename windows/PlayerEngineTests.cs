using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Net;
using System.Net.Sockets;
using System.Collections.Concurrent;

namespace SwirStreamAI;

// Native integration tests: actual decoded screenshots, output configuration and
// progressing timestamps. Progress samples are observations, never frame counts.
static class PlayerEngineTests
{
    public static int Run(string output)
    {
        var checks=new List<object>();bool success=true;
        void Check(string name,bool ok,object? detail=null){checks.Add(new{name,ok,detail});success&=ok;}
        string artifacts=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output))!,"player-engine-artifacts");
        Directory.CreateDirectory(artifacts);
        try
        {
            string clip=Path.Combine(AppContext.BaseDirectory,"assets","local-test.mp4");
            using(var player=new SwirPlayer(IntPtr.Zero,true))
            {
                player.Play(clip);
                bool ready=Wait(()=>{var s=player.Stats;return s.VideoReady&&s.AudioReady&&s.VideoProgressSamples>=4&&s.AudioProgressSamples>=4;},10000);
                var first=player.Stats;
                Check("Local AV output configured with progressing timestamps",ready,new{player.Version,stats=first});
                Check("Quiet probe reports no displayed window",!first.VideoOutputPresent);
                string a=Path.Combine(artifacts,"decoded-a.png"),b=Path.Combine(artifacts,"decoded-b.png");
                player.SaveFrame(a);Thread.Sleep(500);player.SaveFrame(b);
                using(var frame=new Bitmap(a))
                    Check("Decoded PNG pixels have expected dimensions and colors",frame.Width==960&&frame.Height==540&&ColorVariation(frame)>8,new{frame.Width,frame.Height,colors=ColorVariation(frame)});
                Check("Two decoded pictures differ",!SHA256.HashData(File.ReadAllBytes(a)).SequenceEqual(SHA256.HashData(File.ReadAllBytes(b))));
                player.Pause();Thread.Sleep(200);long paused=player.Time;var pausedStats=player.Stats;Thread.Sleep(300);
                Check("Pause freezes timeline and progress observations",player.State==4&&Math.Abs(player.Time-paused)<=80&&player.Stats.VideoProgressSamples==pausedStats.VideoProgressSamples,new{paused,now=player.Time,state=player.State});
                player.Resume();Check("Resume advances timeline",Wait(()=>player.State==3&&player.Time>paused+100,3000));
                Check("Local file is seekable with measured duration",player.Seekable&&player.Length is >=19500 and <=20500,player.Length);
                player.Position=.4f;Check("Precise percent seek does not snap to distant keyframe",Stable(()=>player.Time is >=7800 and <12000,3000),player.Time);
                player.Position=.65f;Check("Forward seek reaches stable target",Stable(()=>player.Time is >=12800 and <17000,3000),player.Time);
                player.Position=.1f;Check("Backward seek reaches stable target",Stable(()=>player.Time is >=1800 and <5000,3000),player.Time);
                int samples=player.Stats.VideoProgressSamples;
                Check("Progress resumes after backward seek",Wait(()=>player.Stats.VideoProgressSamples>=samples+3,3000));
                player.Volume=37;player.Muted=true;Check("Volume and mute roundtrip",player.Volume==37&&player.Muted);
                player.Muted=false;Check("Unmute roundtrip",!player.Muted);
                player.Stop();Check("Stop state",player.State==5);
                bool unsafeRejected=false;try{player.Play("vlc://quit");}catch(ArgumentException){unsafeRejected=true;}
                Check("Non-web special protocol rejected",unsafeRejected);
                using(var hls=new TestMediaServer(Path.Combine(AppContext.BaseDirectory,"assets","test-hls")))
                {
                    player.Play(hls.Url);
                    Check("Loopback HTTP HLS demux and AV playback",Wait(()=>{var s=player.Stats;return s.VideoReady&&s.AudioReady&&s.VideoProgressSamples>=4&&s.AudioProgressSamples>=4;},12000),player.Stats);
                    player.Stop();
                    player.Play(new Uri(new Uri(hls.Url),"missing-stream.m3u8").AbsoluteUri);
                    Check("HTTP 404 becomes explicit playback error",Wait(()=>player.State==7,12000),new{state=player.State,error=player.LastError});
                }
                string wav=Path.Combine(artifacts,"audio-only.wav");WriteWave(wav);
                player.Play(wav);
                Check("Audio-only file has audio progress without invented video",Wait(()=>{var s=player.Stats;return s.AudioReady&&s.AudioProgressSamples>=4&&!s.VideoReady&&s.VideoProgressSamples==0;},6000),player.Stats);
                player.Stop();
                player.Play(Path.Combine(AppContext.BaseDirectory,"assets","player-stalled-video.mp4"));
                Wait(()=>player.Stats.VideoProgressSamples>=3,3000);Thread.Sleep(1500);
                var stalled=player.Stats;Thread.Sleep(500);var later=player.Stats;
                Check("Ended video cannot be mistaken for advancing audio",stalled.VideoProgressSamples>=3&&later.VideoProgressSamples==stalled.VideoProgressSamples&&later.AudioProgressSamples>stalled.AudioProgressSamples,new{stalled,later});
                player.Stop();Check("Stop clears stale statistics",player.Stats==default);
                string grouped="#EXTM3U\n#EXT-X-VERSION:4\n#EXT-X-MEDIA:TYPE=AUDIO,GROUP-ID=\"a\",DEFAULT=YES,NAME=\"Main\",URI=\"audio.m3u8\"\n#EXT-X-MEDIA:TYPE=AUDIO,GROUP-ID=\"a\",DEFAULT=NO,NAME=\"Other\",URI=\"other.m3u8\"\n#EXT-X-MEDIA:TYPE=SUBTITLES,GROUP-ID=\"s\",NAME=\"Subs\",URI=\"subs.m3u8\"\n#EXT-X-STREAM-INF:BANDWIDTH=2000000,RESOLUTION=1280x720,CODECS=\"avc1.4d401f,mp4a.40.2\",AUDIO=\"a\",SUBTITLES=\"s\"\nvideo.m3u8\n#EXT-X-STREAM-INF:BANDWIDTH=9000000,RESOLUTION=1920x1080\nhigh.m3u8\n";
                bool reduced=HlsPlaybackSource.Reduce(grouped,new Uri("https://example.com/live/master.m3u8"),out string master);
                Check("Master selection preserves default audio, subtitles and quoted codecs",reduced&&master.Contains("https://example.com/live/audio.m3u8")&&master.Contains("https://example.com/live/subs.m3u8")&&master.Contains("CODECS=\"avc1.4d401f,mp4a.40.2\"")&&!master.Contains("other.m3u8")&&!master.Contains("high.m3u8"));
                Check("Master cannot turn remote paths into local file reads",!HlsPlaybackSource.Reduce(grouped.Replace("audio.m3u8","file:///C:/secret"),new Uri("https://example.com/master.m3u8"),out _));
                Check("Variable-dependent masters safely retain original handling",!HlsPlaybackSource.Reduce(grouped+"#EXT-X-DEFINE:NAME=\"a\",VALUE=\"x\"",new Uri("https://example.com/master.m3u8"),out _));
                string qualityMaster=grouped+"#EXT-X-STREAM-INF:BANDWIDTH=500000,RESOLUTION=640x360,CODECS=\"avc1.4d401e,mp4a.40.2\"\nlow.m3u8\n#EXT-X-STREAM-INF:BANDWIDTH=90000,CODECS=\"mp4a.40.2\"\nradio.m3u8\n";
                HlsPlaybackSource.Reduce(qualityMaster,new Uri("https://example.com/live/master.m3u8"),out var stableQuality,"Stabilność");
                HlsPlaybackSource.Reduce(qualityMaster,new Uri("https://example.com/live/master.m3u8"),out var highestQuality,"Najwyższa jakość");
                HlsPlaybackSource.Reduce(qualityMaster,new Uri("https://example.com/live/master.m3u8"),out var lowQuality,"Oszczędzaj transfer");
                Check("Stability quality picks bounded 720p rendition",stableQuality.EndsWith("/video.m3u8\n")&&stableQuality.Contains("RESOLUTION=1280x720"));
                Check("Highest quality keeps highest available 1080p rendition",highestQuality.EndsWith("/high.m3u8\n")&&highestQuality.Contains("RESOLUTION=1920x1080"));
                Check("Low transfer picks low video, never audio-only variant",lowQuality.EndsWith("/low.m3u8\n")&&lowQuality.Contains("RESOLUTION=640x360")&&!lowQuality.Contains("radio.m3u8"));
                using(var masters=new HlsFixtureServer(Path.Combine(AppContext.BaseDirectory,"assets","test-hls")))
                {
                    var opening=Stopwatch.StartNew();player.Play(masters.Url,"Swir-Master-Test","https://example.com/player");
                    Check("Master preparation does not block Play caller",opening.ElapsedMilliseconds<150,opening.ElapsedMilliseconds);
                    Check("Reduced master really decodes AV",Wait(()=>{var s=player.Stats;return s.VideoProgressSamples>=4&&s.AudioProgressSamples>=4;},8000),player.Stats);
                    Check("Unselected master variants never fetched",masters.Requests.Any(r=>r.StartsWith("/low/index.m3u8|"))&&!masters.Requests.Any(r=>r.StartsWith("/high/")),masters.Requests.ToArray());
                    Check("Master request retains caller UA and referrer",masters.Requests.Any(r=>r.StartsWith("/master.m3u8|")&&r.Contains("Swir-Master-Test")&&r.Contains("https://example.com/player")));
                    player.Play(clip);Check("Local playback works after forced HLS demux",Wait(()=>player.Stats.VideoProgressSamples>=3,5000));
                    player.Play(masters.SlowUrl);Thread.Sleep(50);player.Stop();Thread.Sleep(1100);
                    Check("Stop cancels master preparation without late restart",player.State==5&&player.Stats==default,new{player.State});
                    var cancelPlayer=new SwirPlayer(IntPtr.Zero,true);cancelPlayer.Play(masters.SlowUrl);var disposeTime=Stopwatch.StartNew();cancelPlayer.Dispose();Thread.Sleep(1100);
                    Check("Dispose cancels pending master safely",disposeTime.ElapsedMilliseconds<2000,disposeTime.ElapsedMilliseconds);
                }
                Check("No legacy VLC DLL loaded",!Process.GetCurrentProcess().Modules.Cast<ProcessModule>().Any(m=>m.ModuleName.Contains("vlc",StringComparison.OrdinalIgnoreCase)));
            }
            var closing=Stopwatch.StartNew();
            for(int i=0;i<4;i++)
            {
                var player=new SwirPlayer(IntPtr.Zero,true);player.Play(clip);
                Wait(()=>player.State==3,3000);player.Dispose();player.Dispose();
            }
            Check("Repeated playing instances dispose without crash",closing.ElapsedMilliseconds<10000,closing.ElapsedMilliseconds);
            string corrupt=Path.Combine(artifacts,"corrupt-engine");Directory.CreateDirectory(corrupt);
            File.WriteAllText(Path.Combine(corrupt,"libmpv-2.dll"),"not a native library");
            File.WriteAllText(Path.Combine(corrupt,"libmpv-2.dll.sha256"),new string('0',64));
            bool rejected=false;try{SwirPlayer.VerifyEngine(corrupt);}catch(InvalidDataException){rejected=true;}
            Check("Corrupt engine rejected before loading",rejected);
            SwirPlayer.VerifyEngine(Path.Combine(AppContext.BaseDirectory,"player"));Check("Shipped engine checksum verified",true);
        }
        catch(Exception ex){Check("Unhandled native integration failure",false,ex.ToString());}
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output,JsonSerializer.Serialize(new{success,utc=DateTimeOffset.UtcNow,checks},new JsonSerializerOptions{WriteIndented=true}));
        return success?0:1;
    }
    static bool Wait(Func<bool> predicate,int timeout)
    {
        var watch=Stopwatch.StartNew();while(watch.ElapsedMilliseconds<timeout){if(predicate())return true;Thread.Sleep(100);}return predicate();
    }
    static bool Stable(Func<bool> predicate,int timeout)
    {
        int consecutive=0;return Wait(()=>{consecutive=predicate()?consecutive+1:0;return consecutive>=5;},timeout);
    }
    static int ColorVariation(Bitmap frame)
    {
        var colors=new HashSet<int>();for(int y=0;y<frame.Height;y+=23)for(int x=0;x<frame.Width;x+=23)colors.Add(frame.GetPixel(x,y).ToArgb());return colors.Count;
    }
    static void WriteWave(string path)
    {
        const int rate=24000,samples=rate*8;using var writer=new BinaryWriter(File.Create(path),Encoding.ASCII);
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));writer.Write(36+samples*2);writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16);writer.Write((short)1);writer.Write((short)1);writer.Write(rate);writer.Write(rate*2);writer.Write((short)2);writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data"));writer.Write(samples*2);for(int i=0;i<samples;i++)writer.Write((short)(Math.Sin(i*2*Math.PI*330/rate)*5000));
    }
    sealed class HlsFixtureServer:IDisposable
    {
        readonly TcpListener listener=new(IPAddress.Loopback,0);readonly CancellationTokenSource cancellation=new();readonly string folder;readonly Task loop;
        readonly ConcurrentBag<Task> connections=new();public ConcurrentQueue<string> Requests{get;}=new();public string Url{get;}public string SlowUrl=>Url.Replace("/master.m3u8","/slow.m3u8");
        public HlsFixtureServer(string folder){this.folder=folder;listener.Start();Url="http://127.0.0.1:"+((IPEndPoint)listener.LocalEndpoint).Port+"/master.m3u8";loop=Serve();}
        async Task Serve()
        {
            try{while(!cancellation.IsCancellationRequested){var client=await listener.AcceptTcpClientAsync(cancellation.Token);connections.Add(Reply(client));}}
            catch(Exception ex)when(ex is OperationCanceledException or SocketException or ObjectDisposedException){}
        }
        async Task Reply(TcpClient client)
        {
            try
            {
                using(client){await using var stream=client.GetStream();using var reader=new StreamReader(stream,Encoding.ASCII,false,4096,true);
                string first=await reader.ReadLineAsync(cancellation.Token)??"",path=first.Split(' ').ElementAtOrDefault(1)??"";var headers=new StringBuilder();string? h;
                while(!string.IsNullOrEmpty(h=await reader.ReadLineAsync(cancellation.Token)))headers.Append(h).Append('|');Requests.Enqueue(path+"|"+headers);
                byte[] body=[];bool exists=true;
                if(path is "/master.m3u8" or "/slow.m3u8"){
                    if(path=="/slow.m3u8")await Task.Delay(900,cancellation.Token);
                    body=Encoding.UTF8.GetBytes("#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=400000,RESOLUTION=640x360\nlow/index.m3u8\n#EXT-X-STREAM-INF:BANDWIDTH=9000000,RESOLUTION=1920x1080\nhigh/index.m3u8\n");
                }else if(System.Text.RegularExpressions.Regex.IsMatch(path,@"^/(low|high)/(index\.m3u8|segment\d+\.ts)$")){string file=Path.Combine(folder,Path.GetFileName(path));exists=File.Exists(file);if(exists)body=await File.ReadAllBytesAsync(file,cancellation.Token);}else exists=false;
                var response=Encoding.ASCII.GetBytes($"HTTP/1.1 {(exists?"200 OK":"404 Not Found")}\r\nContent-Type: {(path.EndsWith(".m3u8")?"application/vnd.apple.mpegurl":"video/mp2t")}\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(response,cancellation.Token);await stream.WriteAsync(body,cancellation.Token);}
            }
            catch(Exception ex)when(ex is OperationCanceledException or IOException or SocketException or ObjectDisposedException){}
        }
        public void Dispose(){cancellation.Cancel();listener.Stop();loop.GetAwaiter().GetResult();Task.WhenAll(connections).GetAwaiter().GetResult();cancellation.Dispose();}
    }
}
