using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SwirStreamAI;

// Offline diagnostic-state regressions. No account credentials or media player.
public static class ProviderFailureTests
{
    const string Secret="SYNTHETIC-FAILURE-096-SECRET";
    const string Host="portal.fixture.example";
    sealed class Failure(string message):Exception(message);
    record CheckResult(string name,bool ok,string detail);
    static void Assert(bool ok,string detail){if(!ok)throw new Failure(detail);}
    static string Action(ProviderRequest r)=>new Uri(r.Url).Query.TrimStart('?').Split('&').Select(v=>v.Split('=',2)).Where(v=>v[0]=="action").Select(v=>Uri.UnescapeDataString(v.ElementAtOrDefault(1)??"")).Single();
    static ProviderDocument Document(ProviderRequest r,string text,int status=200)=>new(Encoding.UTF8.GetBytes(text),r.Url,"application/json",Status:status);
    static ProviderDocument Good(ProviderRequest r)=>Action(r)=="get_genres"?Document(r,"Authorization failed.",403):Document(r,Action(r) switch
    {
        "handshake"=>"{\"js\":{\"token\":\""+Secret+"\"}}",
        "get_profile"=>"{\"js\":{\"id\":1,\"status\":0,\"blocked\":0}}",
        "get_all_channels"=>"{\"js\":{\"data\":[{\"id\":\"11\",\"name\":\"Fixture channel\",\"cmd\":\"ffmpeg http://localhost/ch/11\",\"xmltv_id\":\"fixture.pl\"}]}}",
        "create_link"=>"{\"js\":{\"cmd\":\"ffmpeg https://media.fixture.example/live.mp4?token="+Secret+"\"}}",
        _=>throw new Failure("Unexpected fixture action")
    });
    sealed class Fixture
    {
        public readonly string Folder;
        public readonly ProviderSource Source=new(Guid.NewGuid().ToString("N"),"Offline portal",PrivateSourceKind.Portal,"https://"+Host+"/c/",Mac:"02:00:00:00:00:96");
        public readonly PrivateSourceStore Store;
        public Func<ProviderRequest,CancellationToken,Task<ProviderDocument>> Response=(r,c)=>Task.FromResult(Good(r));
        public Fixture(string folder)
        {
            Folder=folder;Store=new(folder,new PrivateProviderClient((r,c)=>Response(r,c)));
        }
        public ProviderState State=>Store.Sources.Single();
        public string Vault=>Path.Combine(Folder,"private-sources.dpapi");
        public async Task<Station> Ready(){await Store.Connect(Source,CancellationToken.None);return Store.Stations(Source.Id).Single();}
        public void FailAt(string action,int status=403,string body="")=>Response=(r,c)=>Task.FromResult(Action(r)==action?Document(r,body,status):Good(r));
        public void Succeed()=>Response=(r,c)=>Task.FromResult(Good(r));
        public string Checkpoint()=>Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(Store.Sources)))+"|"+
            string.Join('|',Directory.GetFiles(Folder).Order().Select(p=>Path.GetFileName(p)+":"+Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p)))));
        public ProviderState Reload()=>new PrivateSourceStore(Folder).Sources.Single();
    }
    static async Task<T> Reject<T>(Func<Task> run) where T:Exception
    {
        try{await run().WaitAsync(TimeSpan.FromSeconds(3));}
        catch(T ex){return ex;}
        throw new Failure("Expected "+typeof(T).Name);
    }
    static string Catalog(ProviderState s)=>JsonSerializer.Serialize(s.Catalog);
    static void Safe(string text)=>Assert(!text.Contains(Secret,StringComparison.Ordinal)&&!text.Contains(Host,StringComparison.OrdinalIgnoreCase)&&!text.Contains("https://",StringComparison.OrdinalIgnoreCase)&&!text.Contains("02:00:00",StringComparison.OrdinalIgnoreCase),"Diagnostic leaked synthetic private data");
    public static int Run(string report)=>RunAsync(report).GetAwaiter().GetResult();
    static async Task<int> RunAsync(string report)
    {
        string destination=Path.GetFullPath(report),root=Path.Combine(Path.GetDirectoryName(destination)!,"provider-failure-fixtures-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);var checks=new List<CheckResult>();int sequence=0;
        Fixture New()=>new(Path.Combine(root,(++sequence).ToString("D3")));
        async Task Check(string name,Func<Task> test)
        {
            try{await test().WaitAsync(TimeSpan.FromSeconds(6));checks.Add(new(name,true,"Passed"));}
            catch(Exception ex){checks.Add(new(name,false,ex is Failure?ex.Message:ex.GetType().Name));}
        }

        await Check("exception_metadata_preserves_type_and_omits_private_message",()=>
        {
            foreach(var pair in new (Exception Error,string Code)[]{(new UnauthorizedAccessException(Secret+" https://"+Host),"access_denied"),(new InvalidDataException(Secret),"invalid_response"),(new IOException(Secret),"network"),(new InvalidOperationException(Secret),"configuration"),(new Exception(Secret),"unknown")})
            {
                var tagged=ProviderFailures.Tag(pair.Error,"portal_link");Assert(ReferenceEquals(tagged,pair.Error),"Tag replaced original exception");
                var info=ProviderFailures.FromException(tagged);Assert(info.Stage=="portal_link"&&info.Code==pair.Code,"Wrong diagnostic classification");Safe(JsonSerializer.Serialize(info));Safe(ProviderFailures.Describe(info));
            }
            var unknown=ProviderFailures.FromException(new Exception(Secret));Assert(unknown.Stage=="unknown"&&unknown.Code=="unknown","Unknown exception misclassified");
            Safe(ProviderFailures.Describe(new("https://"+Host,Secret)));return Task.CompletedTask;
        });
        foreach(var step in new[]{(Action:"handshake",Stage:"portal_handshake"),(Action:"get_profile",Stage:"portal_profile"),(Action:"get_all_channels",Stage:"portal_channels")})
            await Check("catalog_access_denial_identifies_"+step.Stage,async()=>
            {
                var f=New();f.FailAt(step.Action);var result=await f.Store.Connect(f.Source,CancellationToken.None);
                Assert(result.CatalogFailure is{Code:"access_denied"}&&result.CatalogFailure.Stage==step.Stage,"Wrong catalog failure stage/code");
                Assert(result.Catalog==null&&f.Store.Stations(f.Source.Id).Count==0&&result.PlaybackFailure==null&&result.LastPlaybackAttempt==null,"Failed listing fabricated catalog or playback attempt");
                Assert(f.Reload().CatalogFailure==result.CatalogFailure,"Failure did not survive protected reload");Safe(result.Status);Safe(ProviderFailures.Describe(result.CatalogFailure!));
            });
        await Check("profile_rejection_is_not_a_channel_or_playback_failure",async()=>
        {
            var f=New();f.FailAt("get_profile",200,"{\"js\":{\"id\":0,\"blocked\":1}}");
            var failed=await f.Store.Connect(f.Source,CancellationToken.None);
            Assert(failed.CatalogFailure is{Stage:"portal_profile",Code:"access_denied"}&&failed.Catalog==null&&failed.PlaybackFailure==null,"Invalid profile stage or invented channels");
        });
        await Check("transport_failure_is_staged_without_persisting_exception_text",async()=>
        {
            var f=New();f.Response=(r,c)=>throw new IOException("Transport detail "+Secret+" "+r.Url+" 02:00:00:00:00:96");
            var failed=await f.Store.Connect(f.Source,CancellationToken.None);
            Assert(failed.CatalogFailure is{Stage:"portal_handshake",Code:"network"},"Transport failure lost handshake stage");
            Safe(failed.Status);Safe(JsonSerializer.Serialize(failed.CatalogFailure));Safe(ProviderFailures.Describe(failed.CatalogFailure!));
            Assert(f.Reload().CatalogFailure==failed.CatalogFailure,"Safe network diagnosis not persisted");
        });
        await Check("failed_catalog_refresh_preserves_last_success_and_channels",async()=>
        {
            var f=New();await f.Ready();var original=f.State;f.FailAt("get_all_channels",200,"{\"js\":{\"data\":[]}}");
            var failed=await f.Store.Refresh(f.Source.Id,CancellationToken.None);
            Assert(failed.CatalogFailure is{Stage:"portal_channels",Code:"invalid_response"},"Empty list not diagnosed as channel response failure");
            Assert(Catalog(failed)==Catalog(original)&&failed.Source==original.Source&&f.Store.Stations(f.Source.Id).Count==1,"Failed refresh replaced last good catalog/config");
            Assert(failed.LastAttempt!=null&&failed.LastPlaybackAttempt==null&&failed.PlaybackFailure==null,"Refresh changed playback diagnostics");Safe(failed.Status);
        });
        foreach(bool empty in new[]{false,true})await Check(empty?"empty_playback_link_preserves_catalog":"denied_playback_link_preserves_catalog",async()=>
        {
            var f=New();var station=await f.Ready();var original=f.State;
            f.FailAt("create_link",empty?200:403,empty?"{\"js\":{\"cmd\":\"\"}}":"");
            if(empty)await Reject<InvalidDataException>(()=>f.Store.Resolve(station,CancellationToken.None));
            else await Reject<UnauthorizedAccessException>(()=>f.Store.Resolve(station,CancellationToken.None));
            var failed=f.State;Assert(failed.PlaybackFailure?.Stage=="portal_link"&&failed.PlaybackFailure.Code==(empty?"invalid_response":"access_denied"),"Wrong playback stage/code");
            Assert(Catalog(failed)==Catalog(original)&&failed.LastAttempt==original.LastAttempt&&failed.Status==original.Status&&failed.CatalogFailure==original.CatalogFailure,"Playback failure changed catalog diagnostics");
            Assert(failed.LastPlaybackAttempt!=null&&f.Reload().PlaybackFailure==failed.PlaybackFailure,"Playback failure not persisted");Safe(ProviderFailures.Describe(failed.PlaybackFailure!));
        });
        await Check("successful_resolution_clears_only_playback_failure",async()=>
        {
            var f=New();var station=await f.Ready();f.FailAt("create_link");await Reject<UnauthorizedAccessException>(()=>f.Store.Resolve(station,CancellationToken.None));
            f.FailAt("get_all_channels");await f.Store.Refresh(f.Source.Id,CancellationToken.None);var before=f.State;Assert(before.PlaybackFailure!=null&&before.CatalogFailure!=null,"Fixture did not hold two independent failures");
            f.Succeed();await f.Store.Resolve(station,CancellationToken.None);var after=f.State;
            Assert(after.PlaybackFailure==null&&after.LastPlaybackAttempt>=before.LastPlaybackAttempt,"Successful resolution did not clear its own failure");
            Assert(after.CatalogFailure==before.CatalogFailure&&after.LastAttempt==before.LastAttempt&&after.Status==before.Status&&Catalog(after)==Catalog(before),"Successful link cleared/renewed catalog failure or data");
        });
        await Check("successful_catalog_refresh_clears_only_catalog_failure",async()=>
        {
            var f=New();var station=await f.Ready();f.FailAt("create_link");await Reject<UnauthorizedAccessException>(()=>f.Store.Resolve(station,CancellationToken.None));
            f.FailAt("get_all_channels");await f.Store.Refresh(f.Source.Id,CancellationToken.None);var before=f.State;
            f.Succeed();await f.Store.Refresh(f.Source.Id,CancellationToken.None);var after=f.State;
            Assert(after.CatalogFailure==null&&after.Catalog!=null,"Successful refresh did not clear own failure");
            Assert(after.PlaybackFailure==before.PlaybackFailure&&after.LastPlaybackAttempt==before.LastPlaybackAttempt,"Refresh fabricated playback success or erased playback failure");
        });
        foreach(bool playback in new[]{false,true})await Check(playback?"cancelled_link_cannot_write_late_error":"cancelled_catalog_cannot_write_late_error",async()=>
        {
            var f=New();var station=await f.Ready();string before=f.Checkpoint();using var cancel=new CancellationTokenSource();
            var reached=new TaskCompletionSource<ProviderRequest>(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource<ProviderDocument>(TaskCreationOptions.RunContinuationsAsynchronously);
            f.Response=(r,c)=>{if(Action(r)==(playback?"create_link":"get_all_channels")){reached.TrySetResult(r);return release.Task;}return Task.FromResult(Good(r));};
            Task pending=playback?f.Store.Resolve(station,cancel.Token):f.Store.Refresh(f.Source.Id,cancel.Token);
            try{var request=await reached.Task.WaitAsync(TimeSpan.FromSeconds(2));cancel.Cancel();release.TrySetResult(Document(request,"",403));await Reject<OperationCanceledException>(()=>pending);Assert(f.Checkpoint()==before,"Cancelled request wrote diagnostics or modified catalog");}
            finally{release.TrySetCanceled();await Task.WhenAny(pending,Task.Delay(250));}
        });
        foreach(string change in new[]{"configuration","removed","profile"})await Check("late_link_after_"+change+"_cannot_write_or_play",async()=>
        {
            var f=New();var station=await f.Ready();var reached=new TaskCompletionSource<ProviderRequest>(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource<ProviderDocument>(TaskCreationOptions.RunContinuationsAsynchronously);
            f.Response=(r,c)=>{if(Action(r)=="create_link"){reached.TrySetResult(r);return release.Task;}return Task.FromResult(Good(r));};
            var pending=f.Store.Resolve(station,CancellationToken.None);
            try
            {
                var request=await reached.Task.WaitAsync(TimeSpan.FromSeconds(2));
                if(change=="configuration")f.Store.Put(f.Source with{Name="Edited while linking"});else if(change=="removed")f.Store.Remove(f.Source.Id);else f.Store.InvalidatePending();
                string before=f.Checkpoint();release.TrySetResult(Good(request));await Reject<OperationCanceledException>(()=>pending);
                Assert(f.Checkpoint()==before,"Stale link wrote to the changed profile/source");
            }
            finally{release.TrySetCanceled();await Task.WhenAny(pending,Task.Delay(250));}
        });
        foreach(bool playback in new[]{false,true})await Check(playback?"newer_link_success_wins_over_late_denial":"newer_catalog_success_wins_over_late_denial",async()=>
        {
            var f=New();var station=await f.Ready();var reached=new TaskCompletionSource<ProviderRequest>(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource<ProviderDocument>(TaskCreationOptions.RunContinuationsAsynchronously);int calls=0;
            f.Response=(r,c)=>{if(Action(r)==(playback?"create_link":"get_all_channels")&&++calls==1){reached.TrySetResult(r);return release.Task;}return Task.FromResult(Good(r));};
            Task older=playback?f.Store.Resolve(station,CancellationToken.None):f.Store.Refresh(f.Source.Id,CancellationToken.None);
            try
            {
                var request=await reached.Task.WaitAsync(TimeSpan.FromSeconds(2));
                if(playback)await f.Store.Resolve(station,CancellationToken.None);else await f.Store.Refresh(f.Source.Id,CancellationToken.None);
                string before=f.Checkpoint();release.TrySetResult(Document(request,"",403));await Reject<OperationCanceledException>(()=>older);Assert(f.Checkpoint()==before,"Old failure overwrote newer success");
            }
            finally{release.TrySetCanceled();await Task.WhenAny(older,Task.Delay(250));}
        });
        await Check("catalog_refresh_during_link_preserves_both_independent_results",async()=>
        {
            var f=New();var station=await f.Ready();var reached=new TaskCompletionSource<ProviderRequest>(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource<ProviderDocument>(TaskCreationOptions.RunContinuationsAsynchronously);
            f.Response=(r,c)=>{if(Action(r)=="create_link"){reached.TrySetResult(r);return release.Task;}return Task.FromResult(Good(r));};
            var pending=f.Store.Resolve(station,CancellationToken.None);
            try
            {
                var request=await reached.Task.WaitAsync(TimeSpan.FromSeconds(2));await f.Store.Refresh(f.Source.Id,CancellationToken.None);var refreshed=f.State;
                release.TrySetResult(Document(request,"",403));await Reject<UnauthorizedAccessException>(()=>pending);var final=f.State;
                Assert(final.PlaybackFailure is{Stage:"portal_link",Code:"access_denied"}&&final.CatalogFailure==null,"Independent results overwritten or incorrectly cancelled");
                Assert(Catalog(final)==Catalog(refreshed)&&final.LastAttempt==refreshed.LastAttempt&&final.Status==refreshed.Status,"Link failure rolled back a newer catalog");
            }
            finally{release.TrySetCanceled();await Task.WhenAny(pending,Task.Delay(250));}
        });
        await Check("legacy_schema1_protected_vault_loads_without_new_diagnostic_fields",async()=>
        {
            var f=New();await f.Ready();var before=f.State;
            var json=JsonSerializer.SerializeToNode(new ProviderVault(1,[before]))!;var old=json["Sources"]![0]!.AsObject();
            old.Remove("CatalogFailure");old.Remove("PlaybackFailure");old.Remove("LastPlaybackAttempt");
            File.WriteAllBytes(f.Vault,PrivateData.Protect(Encoding.UTF8.GetBytes(json.ToJsonString()),"provider-vault-v1"));
            var restored=new PrivateSourceStore(f.Folder);var state=restored.Sources.Single();
            Assert(state.CatalogFailure==null&&state.PlaybackFailure==null&&state.LastPlaybackAttempt==null,"Legacy missing fields not defaulted to null");
            Assert(state.Source==before.Source&&Catalog(state)==Catalog(before)&&state.CatalogConfigKey==before.CatalogConfigKey&&restored.Stations(f.Source.Id).Count==1,"Legacy catalog/source compatibility broken");
            restored.Put(state.Source with{Name="Legacy source renamed"});var reopened=new PrivateSourceStore(f.Folder).Sources.Single();
            Assert(reopened.CatalogFailure==null&&reopened.PlaybackFailure==null&&Catalog(reopened)==Catalog(before),"Saving legacy vault lost catalog or fabricated diagnostics");
            foreach(string file in Directory.GetFiles(f.Folder)){string bytes=Encoding.UTF8.GetString(File.ReadAllBytes(file));Assert(!bytes.Contains(Secret)&&!bytes.Contains(Host)&&!bytes.Contains(f.Source.Mac),"Protected fixture file exposes source data");}
        });

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.WriteAllText(destination,JsonSerializer.Serialize(new{schema=1,verifiedAtUtc=DateTimeOffset.UtcNow,mode="offline synthetic diagnostic state",operatorAccountsUsed=0,nativePlaybackTested=false,success=checks.Count>0&&checks.All(c=>c.ok),total=checks.Count,checks},new JsonSerializerOptions{WriteIndented=true}));
        return checks.Count>0&&checks.All(c=>c.ok)?0:1;
    }
}
