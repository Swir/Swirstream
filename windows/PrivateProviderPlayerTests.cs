using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace SwirStreamAI;

/** End-to-end public CENC fixture: real AV with the correct key and an explicit runtime failure with the wrong key. */
public static class PrivateProviderPlayerTests
{
    const string Manifest="https://media.axprod.net/TestVectors/Dash/protected_dash_1080p_h264_singlekey/manifest.mpd";
    const string Kid="4060a865887842679cbf91ae5bae1e72";
    // Published by the primary Google/Shaka demo; these are public fixture values, not user credentials.
    const string PublicDemoKey="fc35340837310cc0fb53de97e22a69e0";
    const int ProofMs=12000,DeadlineMs=35000,NegativeDeadlineMs=25000;
    record Observation(bool Success,long ElapsedMs,long MaximumPositionMs,long MaximumDecodedVideoPtsMs,int VideoProgressSamples,int AudioProgressSamples,int FinalState,SwirMediaStats LastStats);
    record FailureObservation(bool Passed,bool DecodeFailureMessageMatched,bool KeyClearedBeforeManualStop,bool NativeIdleReached,bool ErrorRemainedSticky,bool StatisticsRemainedCleared,int StableObservations,long ElapsedMs);
    static string Digest(string path)=>Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    static string Property(SwirPlayer player,string name)=>(string)typeof(SwirPlayer).GetMethod("String",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(player,new object[]{name})!;
    static bool KeyCleared(SwirPlayer player)=>!Property(player,"options/demuxer-lavf-o").Contains("cenc_decryption_key",StringComparison.Ordinal);
    static FailureObservation ObserveFailure(SwirPlayer player,Action<string,SwirMediaStats?> checkpoint)
    {
        // idle-active proves that the native player unloaded the source; core-idle
        // alone also means pause/buffering and would not prove error handling.
        var timer=Stopwatch.StartNew();
        bool message=player.LastError==SwirPlayer.EncryptedDecodeFailureMessage;
        bool cleared=KeyCleared(player),idle=false,sticky=true,statisticsCleared=true;
        while(timer.ElapsedMilliseconds<2500)
        {
            checkpoint("before-failure-settle",null);
            if(player.State!=7||player.LastError!=SwirPlayer.EncryptedDecodeFailureMessage)sticky=false;
            if(player.Stats!=default)statisticsCleared=false;
            idle=Property(player,"idle-active")=="yes";
            checkpoint("after-failure-settle",null);
            if(idle)break;
            Thread.Sleep(100);
        }
        int observations=0;
        // Do not call Stop here: the production failure path must stop media,
        // clear the key, and keep its error visible on its own.
        for(int i=0;i<10;i++)
        {
            Thread.Sleep(100);checkpoint("before-sticky-error-sample",null);
            bool state=player.State==7&&player.LastError==SwirPlayer.EncryptedDecodeFailureMessage;
            bool empty=player.Stats==default,stillIdle=Property(player,"idle-active")=="yes",noKey=KeyCleared(player);
            sticky&=state;statisticsCleared&=empty;cleared&=noKey;idle&=stillIdle;
            if(state&&empty&&stillIdle&&noKey)observations++;
            checkpoint("after-sticky-error-sample",null);
        }
        bool passed=message&&cleared&&idle&&sticky&&statisticsCleared&&observations==10;
        return new(passed,message,cleared,idle,sticky,statisticsCleared,observations,timer.ElapsedMilliseconds);
    }
    static Observation Observe(SwirPlayer player,Action<string,SwirMediaStats?> checkpoint,int proofMs=ProofMs,int deadlineMs=DeadlineMs)
    {
        var timer=Stopwatch.StartNew();long maximum=0,decodedMaximum=0,lastVideoAdvance=0,lastAudioAdvance=0;int video=0,audio=0,state=0;SwirMediaStats stats=default;
        while(timer.ElapsedMilliseconds<deadlineMs)
        {
            Thread.Sleep(200);
            checkpoint("before-stats",stats);stats=player.Stats;checkpoint("after-stats",stats);maximum=Math.Max(maximum,stats.TimeMs);
            if(stats.VideoProgressSamples>video)lastVideoAdvance=timer.ElapsedMilliseconds;
            if(stats.AudioProgressSamples>audio)lastAudioAdvance=timer.ElapsedMilliseconds;
            checkpoint("before-decoded-frame-time",stats);
            object? frameTime=typeof(SwirPlayer).GetMethod("DecodedFrameTime",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(player,null);
            checkpoint("after-decoded-frame-time",stats);
            if(frameTime is double pts&&double.IsFinite(pts))decodedMaximum=Math.Max(decodedMaximum,(long)(pts*1000));
            video=Math.Max(video,stats.VideoProgressSamples);audio=Math.Max(audio,stats.AudioProgressSamples);
            checkpoint("before-state",stats);state=player.State;checkpoint("after-state",stats);
            // The native getters above are synchronous. This loop deadline cannot
            // interrupt a blocked P/Invoke; the independent process watchdog can.
            if(state==3&&timer.ElapsedMilliseconds>=proofMs&&stats.TimeMs>=proofMs&&decodedMaximum>=proofMs&&stats.VideoReady&&stats.AudioReady
                    &&timer.ElapsedMilliseconds-lastVideoAdvance<1000&&timer.ElapsedMilliseconds-lastAudioAdvance<1000
                    &&video>=(proofMs>=ProofMs?25:3)&&audio>=(proofMs>=ProofMs?25:3))
                return new(true,timer.ElapsedMilliseconds,maximum,decodedMaximum,video,audio,state,stats);
            if(state is 6 or 7)break;
        }
        return new(false,timer.ElapsedMilliseconds,maximum,decodedMaximum,video,audio,state,stats);
    }

    // This class is reached only by the dedicated --provider-player-test CLI
    // process. No application runtime or interactive player timeout is changed.
    sealed class EvidenceLog
    {
        readonly string path;readonly object gate=new();readonly Stopwatch elapsed=Stopwatch.StartNew();
        readonly ManualResetEventSlim finished=new(false);readonly Thread watchdog;
        readonly Dictionary<string,object?> values;int expired;bool writeOk=true;
        public EvidenceLog(string path,Dictionary<string,object?> initial)
        {
            this.path=path;values=initial;values["watchdogSeconds"]=170;values["testProcessId"]=Environment.ProcessId;
            watchdog=new Thread(()=>
            {
                if(finished.Wait(TimeSpan.FromSeconds(170)))return;
                Interlocked.Exchange(ref expired,1);
                try
                {
                    // Native calls never hold this gate. A bounded wait also
                    // keeps a failed report write from defeating process exit.
                    if(Monitor.TryEnter(gate,TimeSpan.FromSeconds(2)))
                    {
                        try{values["success"]=false;values["passed"]=false;values["timedOut"]=true;values["completed"]=false;values["elapsedMs"]=elapsed.ElapsedMilliseconds;values["failureReason"]="The dedicated native test exceeded its process deadline. The last checkpoint identifies the unfinished call; this is a failure, not a successful playback proof.";Write();}
                        finally{Monitor.Exit(gate);}
                    }
                    else
                    {
                        string last=File.Exists(path)?File.ReadAllText(path):"{}";
                        File.WriteAllText(path+".timeout.json",JsonSerializer.Serialize(new{success=false,passed=false,timedOut=true,completed=false,elapsedMs=elapsed.ElapsedMilliseconds,lastCheckpointReport=Path.GetFileName(path),previousReportAvailable=last.Length>2}));
                    }
                }
                catch{ /* Environment.Exit below must also run if evidence storage fails. */ }
                finally{Environment.Exit(124);}
            }){IsBackground=true,Name="Swir native test process watchdog"};
            watchdog.Start();Checkpoint("started");
        }
        void Write()
        {
            string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try{File.WriteAllText(temp,JsonSerializer.Serialize(values,new JsonSerializerOptions{WriteIndented=true}));File.Move(temp,path,true);}
            catch{writeOk=false;values["reportWriteFailed"]=true;}
            finally{try{if(File.Exists(temp))File.Delete(temp);}catch{}}
        }
        public void Checkpoint(string stage,object? update=null,string? key=null)
        {
            lock(gate)
            {
                if(Volatile.Read(ref expired)!=0)return;
                values["stage"]=stage;values["checkpointAtUtc"]=DateTimeOffset.UtcNow;values["elapsedMs"]=elapsed.ElapsedMilliseconds;
                if(key!=null)values[key]=update;Write();
            }
        }
        public void Set(string key,object? value){lock(gate){if(Volatile.Read(ref expired)==0)values[key]=value;}}
        public int Complete(bool success)
        {
            lock(gate)
            {
                if(Volatile.Read(ref expired)!=0)return 124;
                values["success"]=success&&writeOk;values["passed"]=success&&writeOk;values["completed"]=true;values["stage"]="complete";values["elapsedMs"]=elapsed.ElapsedMilliseconds;Write();finished.Set();
            }
            watchdog.Join(1000);return success&&writeOk?0:1;
        }
    }

    public static int Run(string report)=>Task.Run(()=>RunCore(report)).GetAwaiter().GetResult();
    static int RunCore(string report)
    {
        string reportPath=Path.GetFullPath(report);Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
        string folder=Path.Combine(Path.GetDirectoryName(reportPath)!,"native-provider-"+Guid.NewGuid().ToString("N"));
        var evidence=new EvidenceLog(reportPath,new Dictionary<string,object?>
        {
            ["schema"]=3,["atUtc"]=DateTimeOffset.UtcNow,["success"]=false,["passed"]=false,["completed"]=false,["timedOut"]=false,
            ["fixture"]="Official public AXINOM single-key H264 DASH CENC fixture",["requiredDecodedMs"]=ProofMs,["positiveObservationDeadlineMs"]=DeadlineMs,["negativeObservationDeadlineMs"]=NegativeDeadlineMs,
            ["negativeCriterion"]="Specific encrypted-decode error before the 25-second deadline, no successful AV proof, native source unloaded, key cleared before manual Stop, sticky error and empty statistics for ten observations",
            ["output"]="mpv null A/V output; decoded-frame fingerprint PTS and audio-pts",["credentialsLogged"]=false,["licensePayloadLogged"]=false
        });
        SwirPlayer? player=null;bool criteriaPassed=false,failed=false;
        try
        {
            evidence.Checkpoint("hash-application");
            evidence.Set("applicationExeSha256",Environment.ProcessPath is {} exe&&File.Exists(exe)?Digest(exe):null);
            evidence.Set("entryAssemblySha256",Assembly.GetEntryAssembly()?.Location is {} assembly&&File.Exists(assembly)?Digest(assembly):null);
            var source=new ProviderSource(Guid.NewGuid().ToString("N"),"Public AXINOM CENC fixture",PrivateSourceKind.ClearKeyDash,Manifest,KeyId:Kid,KeyHex:PublicDemoKey);
            var store=new PrivateSourceStore(folder);store.Put(source);
            using var cancel=new CancellationTokenSource(TimeSpan.FromSeconds(150));
            evidence.Checkpoint("correct/before-provider-refresh");
            var refreshed=store.Refresh(source.Id,cancel.Token).GetAwaiter().GetResult();
            evidence.Checkpoint("correct/after-provider-refresh");
            if(refreshed.Catalog?.Channels.Count!=1)throw new InvalidOperationException("Fixture manifest refresh failed.");
            var reference=store.Stations(source.Id).Single();
            evidence.Checkpoint("correct/before-provider-resolve");var resolved=store.Resolve(reference,cancel.Token).GetAwaiter().GetResult();evidence.Checkpoint("correct/after-provider-resolve");
            if(resolved.ClearKeyHex!=PublicDemoKey)throw new InvalidOperationException("Fixture key resolution failed.");
            evidence.Set("providerStoreRefreshAndResolvePassed",true);evidence.Set("engineSha256",Digest(Path.Combine(AppContext.BaseDirectory,"player/libmpv-2.dll")));
            evidence.Checkpoint("before-player-construction");player=new SwirPlayer(IntPtr.Zero,true);evidence.Checkpoint("after-player-construction");
            evidence.Checkpoint("before-engine-version");evidence.Set("engineVersion",player.Version);evidence.Checkpoint("after-engine-version");
            void Stage(string phase,string stage,SwirMediaStats? stats)=>evidence.Checkpoint(phase+"/"+stage,stats,"lastNativeStats");
            bool Reset(string phase)
            {evidence.Checkpoint(phase+"/before-key-property");bool cleared=KeyCleared(player!);evidence.Checkpoint(phase+"/after-key-property");return cleared;}
            evidence.Checkpoint("correct/before-play");player.Play(resolved.Station.Url,resolved.Station.UserAgent,resolved.Station.Referrer,clearKeyHex:resolved.ClearKeyHex);evidence.Checkpoint("correct/after-play");
            var correct=Observe(player,(stage,stats)=>Stage("correct",stage,stats));evidence.Checkpoint("correct/observation-complete",correct,"correctKeyPlayback");
            evidence.Checkpoint("correct/before-stop");player.Stop();evidence.Checkpoint("correct/after-stop");
            bool correctReset=Reset("correct");evidence.Set("keyClearedAfterCorrectStop",correctReset);

            var wrongSource=source with{KeyHex=new string('0',32)};store.Put(wrongSource);
            evidence.Checkpoint("wrong/before-provider-refresh");var wrongRefresh=store.Refresh(source.Id,cancel.Token).GetAwaiter().GetResult();evidence.Checkpoint("wrong/after-provider-refresh");
            if(wrongRefresh.Catalog?.Channels.Count!=1)throw new InvalidOperationException("Negative-control manifest refresh failed.");
            evidence.Checkpoint("wrong/before-provider-resolve");var wrong=store.Resolve(store.Stations(source.Id).Single(),cancel.Token).GetAwaiter().GetResult();evidence.Checkpoint("wrong/after-provider-resolve");
            evidence.Checkpoint("wrong/before-play");player.Play(wrong.Station.Url,wrong.Station.UserAgent,wrong.Station.Referrer,clearKeyHex:wrong.ClearKeyHex);evidence.Checkpoint("wrong/after-play");
            var negative=Observe(player,(stage,stats)=>Stage("wrong",stage,stats),ProofMs,NegativeDeadlineMs);evidence.Checkpoint("wrong/observation-complete",negative,"wrongKeyPlayback");
            // Retain the previous timeline criterion as evidence. Corrupted
            // frames can advance PTS, so it is not evidence of valid decryption
            // or a sufficient runtime failure contract on its own.
            evidence.Set("wrongKeyRejectedBeyondClearLead",!negative.Success&&negative.MaximumPositionMs<ProofMs);
            var failure=ObserveFailure(player,(stage,stats)=>Stage("wrong",stage,stats));evidence.Checkpoint("wrong/runtime-failure-observed",failure,"wrongKeyRuntimeFailure");
            bool negativeRejected=!negative.Success&&negative.FinalState==7&&negative.ElapsedMs<NegativeDeadlineMs&&failure.Passed;
            evidence.Set("wrongKeyRejectedByRuntime",negativeRejected);
            evidence.Checkpoint("wrong/before-stop");player.Stop();evidence.Checkpoint("wrong/after-stop");bool wrongReset=Reset("wrong");evidence.Set("keyClearedAfterWrongStop",wrongReset);

            store.Put(source);evidence.Checkpoint("repeat/before-provider-refresh");var recovered=store.Refresh(source.Id,cancel.Token).GetAwaiter().GetResult();evidence.Checkpoint("repeat/after-provider-refresh");
            if(recovered.Catalog?.Channels.Count!=1)throw new InvalidOperationException("Recovery-control manifest refresh failed.");
            evidence.Checkpoint("repeat/before-provider-resolve");var goodAgain=store.Resolve(store.Stations(source.Id).Single(),cancel.Token).GetAwaiter().GetResult();evidence.Checkpoint("repeat/after-provider-resolve");
            evidence.Checkpoint("repeat/before-play");player.Play(goodAgain.Station.Url,goodAgain.Station.UserAgent,goodAgain.Station.Referrer,clearKeyHex:goodAgain.ClearKeyHex);evidence.Checkpoint("repeat/after-play");
            var repeat=Observe(player,(stage,stats)=>Stage("repeat",stage,stats));evidence.Checkpoint("repeat/observation-complete",repeat,"correctKeyPlaybackAfterNegative");
            evidence.Checkpoint("repeat/before-stop");player.Stop();evidence.Checkpoint("repeat/after-stop");bool repeatReset=Reset("repeat");evidence.Set("keyClearedBeforePlainPlayback",repeatReset);
            evidence.Checkpoint("plain/before-play");player.Play(Path.Combine(AppContext.BaseDirectory,"assets/local-test.mp4"));evidence.Checkpoint("plain/after-play");
            bool plainReset=Reset("plain");evidence.Set("keyAbsentDuringPlainPlayback",plainReset);
            var plain=Observe(player,(stage,stats)=>Stage("plain",stage,stats),800,10000);evidence.Checkpoint("plain/observation-complete",plain,"plainPlayback");
            evidence.Checkpoint("plain/before-stop");player.Stop();evidence.Checkpoint("plain/after-stop");
            criteriaPassed=correct.Success&&repeat.Success&&plain.Success&&negativeRejected&&correctReset&&wrongReset&&repeatReset&&plainReset;
            evidence.Checkpoint("playback-criteria-complete",criteriaPassed,"playbackCriteriaPassed");
        }
        catch(Exception error)
        {
            failed=true;evidence.Set("errorType",error.GetType().Name);evidence.Set("errorMessage","Public fixture fetch, provider resolution, native decode or control test failed; sensitive exception content was not logged.");evidence.Checkpoint("caught-test-exception");
        }
        finally
        {
            if(player!=null)
            {
                evidence.Checkpoint("before-player-dispose");
                try{player.Dispose();evidence.Checkpoint("after-player-dispose");}
                catch(Exception error){failed=true;evidence.Set("disposeErrorType",error.GetType().Name);evidence.Checkpoint("player-dispose-failed");}
            }
        }
        return evidence.Complete(criteriaPassed&&!failed);
    }
}
