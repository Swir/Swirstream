using System.Text;
using System.Text.Json;
using System.Net;

namespace SwirStreamAI;

// Offline protocol tests. Every domain, MAC and token below is synthetic.
public static class PortalProtocolTests
{
    const string Origin="https://portal.fixture.example:8443";
    const string Genres="{\"js\":[{\"id\":\"*\",\"title\":\"All\"},{\"id\":1,\"title\":\"News\"}]}";
    const string Channels="{\"js\":{\"data\":[{\"id\":\"11\",\"name\":\"Fixture News\",\"cmd\":\"ffmpeg http://localhost/ch/11\"}]}}";
    const string Mac="02:00:00:00:00:97";
    sealed class Failure(string detail):Exception(detail);
    record Result(string Name,bool Passed,string Detail);
    static void Assert(bool value,string detail){if(!value)throw new Failure(detail);}
    static ProviderSource Source(string path="/tenant/c/")=>new(Guid.NewGuid().ToString("N"),"Offline portal",PrivateSourceKind.Portal,Origin+path,Mac:Mac);
    static ProviderDocument Doc(ProviderRequest r,string body,int status=200,string redirect="")=>new(Encoding.UTF8.GetBytes(body),r.Url,"application/json",Status:status,Redirect:redirect);
    static Dictionary<string,string> Query(ProviderRequest r)=>new Uri(r.Url).Query.TrimStart('?').Split('&',StringSplitOptions.RemoveEmptyEntries).Select(s=>s.Split('=',2)).ToDictionary(p=>Uri.UnescapeDataString(p[0]),p=>Uri.UnescapeDataString(p.ElementAtOrDefault(1)??""));
    static string Action(ProviderRequest r)=>Query(r).GetValueOrDefault("action","");
    static PrivateProviderClient Client(Func<ProviderRequest,ProviderDocument> response)=>new((r,c)=>{c.ThrowIfCancellationRequested();return Task.FromResult(response(r));},(host,ct)=>Task.FromResult(new[]{IPAddress.Parse("8.8.8.8")}));
    static async Task<T> Reject<T>(Func<Task> operation)where T:Exception
    {try{await operation();}catch(T ex){return ex;}throw new Failure("Expected "+typeof(T).Name);}
    public static int Run(string report)=>RunAsync(report).GetAwaiter().GetResult();
    static async Task<int> RunAsync(string report)
    {
        var results=new List<Result>();
        async Task Check(string name,Func<Task> body)
        {try{await body().WaitAsync(TimeSpan.FromSeconds(8));results.Add(new(name,true,"Passed"));}catch(Exception ex){results.Add(new(name,false,ex is Failure?ex.Message:ex.GetType().Name));}}

        await Check("explicit_static_flags_survive_protected_storage_and_skip_only_create_link",async()=>
        {
            string original="ffmpeg https://cdn.fixture.example/play.php?ticket=synthetic";
            string body=JsonSerializer.Serialize(new{js=new{data=new[]{new{id="static",name="Fixture",cmd=original,use_http_tmp_link="0",use_load_balancing=0}}}});
            var requests=new List<string>();var source=Source();
            var client=Client(r=>{requests.Add(Action(r));return Action(r) switch{"get_genres"=>Doc(r,Genres),"get_all_channels"=>Doc(r,body),_=>throw new Failure("Static channel invoked create_link")};});
            var catalog=await client.Refresh(source,CancellationToken.None);
            string path=Path.Combine(Path.GetTempPath(),"swir-static-fixture-"+Guid.NewGuid().ToString("N")+".dat");
            try
            {
                PrivateData.Write(path,new ProviderVault(1,[new(source,catalog)]),"fixture-static-catalog",backup:false);
                Assert(!Encoding.UTF8.GetString(File.ReadAllBytes(path)).Contains("synthetic"),"Private command stored as plaintext");
                var stored=PrivateData.Read<ProviderVault>(path,"fixture-static-catalog").Sources.Single().Catalog!.Channels.Single();
                Assert(stored.UseHttpTmpLink==false&&stored.UseLoadBalancing==false,"Flags lost in protected catalogue");
                var playback=await client.Resolve(source,stored,CancellationToken.None);
                Assert(playback.Station.Url==original[7..]&&playback.Station.UserAgent==PortalProtocol.UserAgent,"Original URL or public media agent changed");
                Assert(requests.SequenceEqual(new[]{"get_genres","get_all_channels","get_genres"}),"Static playback skipped fresh session or called create_link");
                Assert(!JsonSerializer.Serialize(playback.Station).Contains(Mac),"Static playback leaked MAC");
            }
            finally{if(File.Exists(path))File.Delete(path);}
        });
        await Check("legacy_or_partial_or_enabled_flags_still_resolve_fresh_link",async()=>
        {
            var legacy=JsonSerializer.Deserialize<ProviderChannel>("{\"Id\":\"old\",\"Station\":{\"Name\":\"Fixture\",\"Url\":\"\"},\"Command\":\"https://cdn.fixture.example/static\"}")!;
            Assert(legacy.UseHttpTmpLink==null&&legacy.UseLoadBalancing==null,"Older catalogue guessed static metadata");
            foreach(var channel in new[]{legacy,legacy with{UseHttpTmpLink=false},legacy with{UseLoadBalancing=false},legacy with{UseHttpTmpLink=true,UseLoadBalancing=false},legacy with{UseHttpTmpLink=false,UseLoadBalancing=true}})
            {
                var requests=new List<string>();var client=Client(r=>{requests.Add(Action(r));return Doc(r,Action(r)=="get_genres"?Genres:"{\"js\":{\"cmd\":\"https://cdn.fixture.example/fresh\"}}");});
                var play=await client.Resolve(Source(),channel,CancellationToken.None);
                Assert(requests.SequenceEqual(new[]{"get_genres","create_link"})&&play.Station.Url.EndsWith("/fresh"),"Unknown/enabled flags bypassed create_link");
            }
        });
        await Check("static_command_accepts_only_safe_absolute_media_and_known_prefix",async()=>
        {
            foreach(string command in new[]{"ffmpeg http://localhost/ch/1","http://127.0.0.1/live","https://10.0.0.1/live","/live/1","//cdn.fixture.example/live","ffmpeg -i https://cdn.fixture.example/live","ffmpeg 'https://cdn.fixture.example/live'","https://cdn.fixture.example/live -option","https://name:secret@cdn.fixture.example/live","https://cdn.fixture.example/live#part","https://cdn.fixture.example/live\r\nCookie: x","http://cdn.fixture.example\\private"})
            {
                var requests=new List<string>();var client=Client(r=>{requests.Add(Action(r));return Doc(r,Action(r)=="get_genres"?Genres:"{\"js\":{\"cmd\":\"https://cdn.fixture.example/fresh\"}}");});
                var channel=new ProviderChannel("x",new("Fixture",""),command){UseHttpTmpLink=false,UseLoadBalancing=false};
                await client.Resolve(Source(),channel,CancellationToken.None);
                Assert(requests.SequenceEqual(new[]{"get_genres","create_link"}),"Unsafe/ambiguous command selected directly");
            }
            foreach(string prefix in new[]{"","ffmpeg ","ffrt "})
            {
                int calls=0;var client=Client(r=>{calls++;Assert(Action(r)=="get_genres","Safe static command unnecessarily created link");return Doc(r,Genres);});
                var play=await client.Resolve(Source(),new("x",new("Fixture",""),prefix+"https://cdn.fixture.example/live"){UseHttpTmpLink=false,UseLoadBalancing=false},CancellationToken.None);
                Assert(calls==1&&play.Station.Url=="https://cdn.fixture.example/live","Safe prefix did not preserve URL");
            }
        });
        await Check("static_command_never_bypasses_fresh_full_profile_denial",async()=>
        {
            var actions=new List<string>();int dns=0;
            var client=new PrivateProviderClient((r,c)=>{actions.Add(Action(r));return Task.FromResult(Action(r) switch{"get_genres"=>Doc(r,"",403),"handshake"=>Doc(r,"{\"js\":{\"token\":\"fixture-session\"}}"),"get_profile"=>Doc(r,"{\"js\":{\"id\":0,\"status\":0}}"),_=>throw new Failure("Denied profile reached media resolution")});},(host,c)=>{dns++;return Task.FromResult(new[]{IPAddress.Parse("8.8.8.8")});});
            var ex=await Reject<UnauthorizedAccessException>(()=>client.Resolve(Source(),new("x",new("Fixture",""),"https://cdn.fixture.example/live"){UseHttpTmpLink=false,UseLoadBalancing=false},CancellationToken.None));
            Assert(actions.SequenceEqual(new[]{"get_genres","handshake","get_profile"})&&dns==0&&ProviderFailures.FromException(ex).Stage=="portal_profile","Static metadata masked profile denial");
        });
        await Check("static_media_dns_must_be_public_and_cancellation_is_preserved",async()=>
        {
            var channel=new ProviderChannel("x",new("Fixture",""),"https://cdn.fixture.example/live"){UseHttpTmpLink=false,UseLoadBalancing=false};
            foreach(var addresses in new[]{Array.Empty<IPAddress>(),new[]{IPAddress.Loopback},new[]{IPAddress.Parse("8.8.8.8"),IPAddress.Parse("10.0.0.1")}})
            {
                var actions=new List<string>();var client=new PrivateProviderClient((r,c)=>{actions.Add(Action(r));return Task.FromResult(Doc(r,Action(r)=="get_genres"?Genres:"{\"js\":{\"cmd\":\"https://cdn.fixture.example/fresh\"}}"));},(h,c)=>Task.FromResult(addresses));
                var play=await client.Resolve(Source(),channel,CancellationToken.None);
                Assert(actions.SequenceEqual(new[]{"get_genres","create_link"})&&play.Station.Url.EndsWith("/fresh"),"Private/empty DNS selected direct URL");
            }
            using var cancel=new CancellationTokenSource();int calls=0;
            var cancelled=new PrivateProviderClient((r,c)=>{calls++;return Task.FromResult(Doc(r,Genres));},(h,c)=>{cancel.Cancel();c.ThrowIfCancellationRequested();return Task.FromResult(Array.Empty<IPAddress>());});
            await Reject<OperationCanceledException>(()=>cancelled.Resolve(Source(),channel,cancel.Token));Assert(calls==1,"Cancellation caused additional API request");
        });

        await Check("direct_mac_catalog_and_fresh_link_use_real_wire_user_agent",async()=>
        {
            var requests=new List<ProviderRequest>();var source=Source();string original=source.Url;
            var client=Client(r=>
            {
                requests.Add(r);Assert(new Uri(r.Url).AbsolutePath=="/tenant/portal.php","Unexpected candidate");
                Assert(!r.Headers!.ContainsKey("Authorization"),"Direct mode sent Bearer");
                Assert(r.Headers["Cookie"]=="mac="+Mac+"; stb_lang=en; timezone=UTC","MAC changed or cookie not raw");
                using var wire=PrivateProviderClient.BuildRequest(r);
                Assert(wire.Headers.UserAgent.ToString()==PortalProtocol.UserAgent,"Wire User-Agent concatenated with default");
                Assert(!Query(r).Keys.Any(k=>k is "sn" or "device_id" or "device_id2"),"Generated device identity");
                return Action(r) switch
                {
                    "get_genres"=>Doc(r,Genres),"get_all_channels"=>Doc(r,Channels),
                    "create_link"=>Doc(r,"{\"js\":{\"cmd\":\"ffmpeg https://cdn.fixture.example/live.m3u8?ticket=fixture\"}}"),
                    _=>throw new Failure("Direct mode attempted profile or handshake")
                };
            });
            var catalog=await client.Refresh(source,CancellationToken.None);
            Assert(catalog.Channels.Count==1&&catalog.Notice.Contains("nie potwierdza odtwarzania"),"Catalog overclaims playback");
            var playback=await client.Resolve(source,catalog.Channels.Single(),CancellationToken.None);
            Assert(requests.Select(Action).SequenceEqual(new[]{"get_genres","get_all_channels","get_genres","create_link"}),"Resolve reused stale negotiation");
            Assert(Query(requests.Last())["cmd"]=="ffmpeg http://localhost/ch/11","Provider command modified");
            Assert(source.Url==original&&playback.Station.UserAgent==PortalProtocol.UserAgent&&!JsonSerializer.Serialize(playback.Station).Contains(Mac),"Config changed or media credentials leaked");
        });
        await Check("valid_data_envelope_and_bom_are_supported",()=>
        {
            var r=new ProviderRequest(Origin);
            foreach(string body in new[]{Genres,"\uFEFF"+Genres,"{\"js\":{\"data\":[{\"id\":\"1\",\"title\":\"Fixture\"}]}}"})
                Assert(PortalProtocol.Classify(Doc(r,body))==PortalProbeKind.DirectMac,"Valid genres not recognized");
            return Task.CompletedTask;
        });
        await Check("stock_authentication_messages_select_full_protocol",()=>
        {
            var r=new ProviderRequest(Origin);
            foreach(string body in new[]{"Authorization failed. 75","Unauthorized request.","Invalid token","{\"js\":\"Authorization failed.\"}","{\"js\":{\"error\":\"Invalid token\"}}"})
                Assert(PortalProtocol.Classify(Doc(r,body))==PortalProbeKind.AuthenticationRequired,"Known auth-required response skipped handshake");
            foreach(string body in new[]{"{\"js\":{\"auth\":0,\"data\":[{\"id\":1,\"title\":\"X\"}]}}","{\"js\":{\"msg\":\"Access denied\",\"data\":[{\"id\":1,\"title\":\"X\"}]}}","{\"block_msg\":\"Access denied\",\"js\":[{\"id\":1,\"title\":\"X\"}]}"})
                Assert(PortalProtocol.Classify(Doc(r,body))==PortalProbeKind.Denied,"Explicit denial masked by data");
            return Task.CompletedTask;
        });
        await Check("malformed_empty_wrong_shape_and_error_never_select_direct_mode",async()=>
        {
            foreach(string body in new[]{"not json","<html>portal</html>","null","[]","{\"js\":false}","{\"js\":[]}","{\"js\":{\"data\":[]}}","{\"js\":[{\"id\":1}]}","{\"js\":[{\"id\":{},\"title\":\"X\"}]}","{\"js\":[{\"id\":1,\"title\":\" \"}]}","{\"js\":{\"data\":[{\"id\":1,\"title\":\"X\"}],\"error\":\"Unknown action\"}}","{\"error\":\"Unknown action\",\"js\":[{\"id\":1,\"title\":\"X\"}]}"})
            {
                var requests=new List<ProviderRequest>();var client=Client(r=>{requests.Add(r);return Doc(r,body);});
                await Reject<InvalidDataException>(()=>client.Refresh(Source(),CancellationToken.None));
                Assert(requests.Count==3&&requests.All(r=>Action(r)=="get_genres"),"Bad response caused auth, channels, or unbounded retry");
            }
        });
        await Check("structured_status_denial_cannot_become_direct_or_try_other_endpoint",async()=>
        {
            foreach(string body in new[]{"{\"status\":1,\"js\":[{\"id\":1,\"title\":\"X\"}]}","{\"js\":{\"blocked\":true,\"data\":[{\"id\":1,\"title\":\"X\"}]}}","{\"js\":[{\"id\":1,\"title\":\"X\",\"status\":\"denied\"}]}"})
            {
                int count=0;var client=Client(r=>{count++;return Doc(r,body);});
                var ex=await Reject<UnauthorizedAccessException>(()=>client.Refresh(Source(),CancellationToken.None));
                Assert(count==1&&ProviderFailures.FromException(ex).Stage=="portal_handshake","Denial retried or stage lost");
            }
        });
        await Check("explicit_account_block_in_every_envelope_stops_candidates",async()=>
        {
            foreach(string body in new[]{"Account blocked","{\"js\":\"Account expired.\"}","{\"js\":{\"error\":\"Account disabled\"}}","{\"error\":\"Account blocked\",\"js\":[{\"id\":1,\"title\":\"X\"}]}"})
            {
                int count=0;var client=Client(r=>{count++;return Doc(r,body);});
                await Reject<UnauthorizedAccessException>(()=>client.Refresh(Source(),CancellationToken.None));Assert(count==1,"Explicit account block retried or authenticated");
            }
        });
        await Check("http_auth_or_plaintext_failure_enters_full_flow_and_profile_denial_stops",async()=>
        {
            foreach(int status in new[]{200,401,403})
            {
                var requests=new List<ProviderRequest>();var client=Client(r=>
                {
                    requests.Add(r);
                    if(new Uri(r.Url).AbsolutePath!="/tenant/portal.php")return Doc(r,Genres);
                    return Action(r) switch
                    {
                        "get_genres"=>Doc(r,"Authorization failed.",status),
                        "handshake"=>Doc(r,"{\"js\":{\"token\":\"fixture-session\"}}"),
                        "get_profile"=>Doc(r,"{\"js\":{\"id\":0,\"status\":0}}"),
                        _=>throw new Failure("Rejected profile reached channel/link API")
                    };
                });
                var ex=await Reject<UnauthorizedAccessException>(()=>client.Refresh(Source(),CancellationToken.None));
                Assert(requests.Select(Action).SequenceEqual(new[]{"get_genres","handshake","get_profile"}),"Profile denial downgraded to another endpoint");
                Assert(ProviderFailures.FromException(ex).Stage=="portal_profile","Profile denial lost stage");
                Assert(requests.Last().Headers!.GetValueOrDefault("Authorization")=="Bearer fixture-session","Full profile missing session token");
            }
        });
        await Check("explicit_endpoint_prefix_origin_and_three_candidate_bound",()=>
        {
            Assert(PortalAddress.Candidates(Origin+"/tenant/c/").SequenceEqual(new[]{Origin+"/tenant/portal.php",Origin+"/tenant/server/load.php",Origin+"/tenant/stalker_portal/server/load.php"}),"Frontend prefix lost");
            Assert(PortalAddress.Candidates(Origin+"/tenant/c/api.php").SequenceEqual(new[]{Origin+"/tenant/c/api.php",Origin+"/tenant/c/portal.php",Origin+"/tenant/c/server/load.php"}),"Explicit file lost first priority or real c directory stripped");
            Assert(PortalAddress.Candidates(Origin+"/prefix/stalker_portal/c/index.html").SequenceEqual(new[]{Origin+"/prefix/stalker_portal/portal.php",Origin+"/prefix/stalker_portal/server/load.php"}),"Stalker prefix nested or stripped");
            Assert(PortalAddress.Candidates(Origin+"/tenant/server/load.php")[0]==Origin+"/tenant/server/load.php","Explicit full endpoint not first");
            foreach(var uri in PortalAddress.Candidates(Origin+"/tenant/c/"))Assert(new Uri(uri).GetLeftPart(UriPartial.Authority)==Origin,"Candidate changed origin");
            return Task.CompletedTask;
        });
        await Check("404_can_negotiate_next_candidate_without_mutating_source",async()=>
        {
            var paths=new List<string>();var source=Source();var client=Client(r=>
            {
                string path=new Uri(r.Url).AbsolutePath;paths.Add(path);
                if(path.EndsWith("/portal.php"))return Doc(r,"missing",404);
                return Doc(r,Action(r)=="get_genres"?Genres:Channels);
            });
            Assert((await client.Refresh(source,CancellationToken.None)).Channels.Count==1&&paths.SequenceEqual(new[]{"/tenant/portal.php","/tenant/server/load.php","/tenant/server/load.php"})&&source.Url==Origin+"/tenant/c/","Fallback routing or source immutability failed");
        });
        await Check("cookie_cannot_cross_origin_port_or_https_downgrade_redirect",async()=>
        {
            foreach(string redirect in new[]{"https://other.fixture.example/portal.php","https://portal.fixture.example:9443/portal.php","http://portal.fixture.example:8443/portal.php","http://127.0.0.1/portal.php"})
            {
                int count=0;var client=Client(r=>{count++;return Doc(r,"",302,redirect);});
                await Reject<InvalidDataException>(()=>client.Refresh(Source(),CancellationToken.None));Assert(count==1,"MAC forwarded outside approved origin");
            }
        });
        await Check("full_session_bearer_cannot_cross_origin_redirect",async()=>
        {
            int count=0;var client=Client(r=>
            {
                count++;return Action(r) switch{"get_genres"=>Doc(r,"",403),"handshake"=>Doc(r,"{\"js\":{\"token\":\"fixture-session\"}}"),_=>Doc(r,"",302,"https://other.fixture.example/portal.php")};
            });
            var ex=await Reject<InvalidDataException>(()=>client.Refresh(Source(),CancellationToken.None));
            Assert(count==3&&ProviderFailures.FromException(ex).Stage=="portal_profile","Bearer redirect escaped or stage lost");
        });
        await Check("header_override_replaces_default_and_rejects_injection",async()=>
        {
            using(var request=PrivateProviderClient.BuildRequest(new(Origin,Headers:new(){{"user-agent",PortalProtocol.UserAgent}})))
                Assert(string.Join(' ',request.Headers.GetValues("User-Agent"))==PortalProtocol.UserAgent&&!request.Headers.ToString().Contains("SwirStreamAI/"),"Actual header is concatenated");
            using(var request=PrivateProviderClient.BuildRequest(new(Origin)))Assert(request.Headers.UserAgent.ToString().StartsWith("SwirStreamAI/"),"Default agent missing");
            foreach(var header in new[]{("User-Agent","valid\r\nCookie: leak"),("Cookie","mac=test\nOther: leak"),("Bad\r\nName","value")})
                await Reject<InvalidDataException>(()=>{using var request=PrivateProviderClient.BuildRequest(new(Origin,Headers:new(){{header.Item1,header.Item2}}));return Task.CompletedTask;});
        });
        await Check("session_parameters_and_local_input_rejected_before_transport",async()=>
        {
            foreach(string url in new[]{Origin+"/c/?token=fixture","http://127.0.0.1/c/","https://name:password@portal.fixture.example/c/"})
            {
                int count=0;var client=Client(r=>{count++;return Doc(r,Genres);});
                await Reject<InvalidDataException>(()=>client.Refresh(Source() with{Url=url},CancellationToken.None));Assert(count==0,"Invalid input reached transport");
            }
        });
        string destination=Path.GetFullPath(report);Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.WriteAllText(destination,JsonSerializer.Serialize(new{verifiedAtUtc=DateTimeOffset.UtcNow,mode="offline synthetic fixtures",success=results.All(x=>x.Passed),total=results.Count,checks=results},new JsonSerializerOptions{WriteIndented=true}));
        return results.All(x=>x.Passed)?0:1;
    }
}
