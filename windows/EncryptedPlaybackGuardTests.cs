using System.Text.Json;
using Track=SwirStreamAI.EncryptedPlaybackGuard.Track;

namespace SwirStreamAI;

internal static class EncryptedPlaybackGuardTests
{
    internal static int Run(string report)
    {
        var checks=new List<object>();bool success=true;
        void Check(string name,Func<bool> test){bool ok;try{ok=test();}catch{ok=false;}checks.Add(new{name,ok});success&=ok;}
        Check("only exact native audio/video decoder modules and fatal/error levels qualify",()=>
            EncryptedPlaybackGuard.Classify("ffmpeg/audio",20)==Track.Audio&&EncryptedPlaybackGuard.Classify("ad",10)==Track.Audio&&
            EncryptedPlaybackGuard.Classify("ffmpeg/video",20)==Track.Video&&EncryptedPlaybackGuard.Classify("vd",10)==Track.Video&&
            new[]{"http","demuxer","ffmpeg","ffmpeg/audio/network","other/ad","overflow"}.All(p=>EncryptedPlaybackGuard.Classify(p,20)==Track.None)&&
            new[]{0,30,40,50}.All(level=>EncryptedPlaybackGuard.Classify("ffmpeg/audio",level)==Track.None));
        bool Healthy(bool audio,bool video,bool videoMoves=true)
        {
            var guard=new EncryptedPlaybackGuard();guard.Reset(0);
            for(long t=0;t<30000;t+=400)if(guard.Observe(t,false,audio?1:0,audio?t/1000d:null,video?1:0,video?(videoMoves?t/1000d:0):null))return false;
            return true;
        }
        Check("normal CENC AV with no decoder errors",()=>Healthy(true,true));
        Check("black CENC AV is independent of image pixels",()=>Healthy(true,true));
        Check("static CENC video with audio and no decoder errors",()=>Healthy(true,true,false));
        Check("muted CENC retains normal audio timestamps",()=>Healthy(true,true));
        Check("CENC audio-only does not require video",()=>Healthy(true,false));
        Check("CENC video-only does not require audio",()=>Healthy(false,true));
        Check("stalled CENC track without decoder errors is not this failure",()=>Healthy(false,true,false));
        Check("single huge corrupt-packet burst does not become a persistent failure",()=>
        {
            var g=new EncryptedPlaybackGuard();g.Reset(0);g.Observe(0,false,1,null,1,0);
            for(int i=0;i<100000;i++)g.Error(Track.Audio,400);
            for(long t=400;t<15000;t+=400)if(g.Observe(t,false,1,null,1,t/1000d))return false;return true;
        });
        Check("sustained audio errors plus missing selected audio fail after startup grace",()=>
        {
            var g=new EncryptedPlaybackGuard();g.Reset(0);g.Observe(0,false,1,null,1,0);long failed=0;
            for(long t=400;t<=6000;t+=400){for(int i=0;i<8;i++)g.Error(Track.Audio,t);if(g.Observe(t,false,1,null,1,t/1000d)){failed=t;break;}}
            return failed is >=3000 and <=4000;
        });
        Check("sustained video decode failures reject concealed advancing timestamps",()=>
        {
            var g=new EncryptedPlaybackGuard();g.Reset(0);g.Observe(0,false,0,null,1,0);long failed=0;
            for(long t=400;t<=7000;t+=400){for(int i=0;i<10;i++)g.Error(Track.Video,t);if(g.Observe(t,false,0,null,1,t/1000d)){failed=t;break;}}
            return failed is >=3000 and <=4400;
        });
        Check("sparse recoverable errors with AV progress do not trip",()=>
        {
            var g=new EncryptedPlaybackGuard();g.Reset(0);
            for(long t=0;t<30000;t+=400){if(t%2000==0){g.Error(Track.Audio,t);g.Error(Track.Video,t);}if(g.Observe(t,false,1,t/1000d,1,t/1000d))return false;}return true;
        });
        Check("unselected audio error queue never requires audio on a video-only file",()=>
        {
            var g=new EncryptedPlaybackGuard();g.Reset(0);
            for(long t=0;t<15000;t+=400){for(int i=0;i<30;i++)g.Error(Track.Audio,t);if(g.Observe(t,false,0,null,1,t/1000d))return false;}return true;
        });
        bool Suspended()
        {
            var g=new EncryptedPlaybackGuard();g.Reset(0);g.Observe(0,false,1,null,1,0);
            for(long t=400;t<30000;t+=400){for(int i=0;i<20;i++)g.Error(Track.Audio,t);if(g.Observe(t,true,1,null,1,0))return false;}
            for(long t=30000;t<40000;t+=400)if(g.Observe(t,false,1,t/1000d,1,t/1000d))return false;return true;
        }
        Check("pause clears error duration and cannot accumulate a failure",Suspended);
        Check("cache buffering clears error duration and resumes cleanly",Suspended);
        Check("seek reset removes prior corrupt-packet history",()=>
        {
            var g=new EncryptedPlaybackGuard();g.Reset(0);g.Observe(0,false,1,null,1,0);
            for(long t=400;t<=2400;t+=400){for(int i=0;i<20;i++)g.Error(Track.Audio,t);if(g.Observe(t,false,1,null,1,t/1000d))return false;}
            g.Reset(2600);for(long t=2800;t<14000;t+=400)if(g.Observe(t,false,1,t/1000d,1,t/1000d))return false;return true;
        });
        Check("source replacement resets error and selected-track state",()=>
        {
            var g=new EncryptedPlaybackGuard();g.Reset(0);g.Observe(0,false,1,null,1,0);
            for(long t=400;t<=4000;t+=400){for(int i=0;i<20;i++)g.Error(Track.Audio,t);g.Observe(t,false,1,null,1,t/1000d);}
            g.Reset(4100);for(long t=4200;t<15000;t+=400)if(g.Observe(t,false,1,t/1000d,1,t/1000d))return false;return true;
        });
        Check("a newly selected track receives its own grace period",()=>
        {
            var g=new EncryptedPlaybackGuard();g.Reset(0);g.Observe(0,false,1,null,1,0);
            for(long t=400;t<=2400;t+=400){g.Error(Track.Audio,t);g.Observe(t,false,1,null,1,t/1000d);}
            for(int i=0;i<40;i++)g.Error(Track.Audio,2800);
            if(g.Observe(2800,false,2,null,1,2.8))return false;
            return !g.Observe(6000,false,2,1,1,6);
        });
        Check("old decoder errors expire instead of failing a later network stall",()=>
        {
            var g=new EncryptedPlaybackGuard();g.Reset(0);g.Observe(0,false,1,0,1,0);
            for(long t=400;t<=2400;t+=400){for(int i=0;i<8;i++)g.Error(Track.Audio,t);if(g.Observe(t,false,1,t/1000d,1,t/1000d))return false;}
            for(long t=2800;t<15000;t+=400)if(g.Observe(t,false,1,2.4,1,t/1000d))return false;return true;
        });
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(report))!);
        File.WriteAllText(report,JsonSerializer.Serialize(new{atUtc=DateTimeOffset.UtcNow,success,scope="CENC decoder error policy; no media or accounts accessed",checks},new JsonSerializerOptions{WriteIndented=true}));
        return success?0:1;
    }
}
