using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SwirStreamAI;

/** Controlled provider fixtures. No operator accounts, public log credentials, or actual TV services. */
public static class PrivateProviderTests
{
    const string Secret="FIXTURE-ONLY-PRIVATE-TOKEN-090";
    const string KidHex="feedf00deedeadbeeff0baadf00dd00d",Kid64="_u3wDe7erb7v8Lqt8A3QDQ";
    const string Key64="ABEiM0RVZneImaq7zN3u_w";
    const string ListBody="#EXTM3U\n#EXTINF:-1 tvg-id=\"fixture.pl\" group-title=\"Test\",Private Fixture Channel\nhttps://media.example/live.m3u8?token="+Secret+"\n";
    const string Manifest="<MPD xmlns=\"urn:mpeg:dash:schema:mpd:2011\" xmlns:cenc=\"urn:mpeg:cenc:2013\"><Period><AdaptationSet mimeType=\"video/mp4\" codecs=\"avc1.4d401e\"><ContentProtection schemeIdUri=\"urn:mpeg:dash:mp4protection:2011\" value=\"cenc\" cenc:default_KID=\"feedf00d-eede-adbe-eff0-baadf00dd00d\" /></AdaptationSet></Period></MPD>";
    record Result(string Name,bool Passed,string Detail){public bool ok=>Passed;}
    sealed class FixtureFailure(string detail):Exception(detail);
    static void Assert(bool value,string detail){if(!value)throw new FixtureFailure(detail);}
    static async Task Reject<T>(Func<Task> operation) where T:Exception
    {try{await operation();}catch(T){return;}throw new FixtureFailure("Expected rejection did not occur: "+typeof(T).Name);}
    static Task RejectSync<T>(Action operation) where T:Exception=>Reject<T>(()=>{operation();return Task.CompletedTask;});
    static ProviderSource Source(PrivateSourceKind kind=PrivateSourceKind.M3u,string url="https://provider.example/catalog?token="+Secret)=>new(Guid.NewGuid().ToString("N"),"Fixture source",kind,url);
    static ProviderDocument Doc(ProviderRequest r,string text,int status=200,string redirect="")=>new(Encoding.UTF8.GetBytes(text),r.Url,"text/plain",DateTimeOffset.UtcNow.AddMinutes(-1),status,redirect);
    static PrivateProviderClient Client(Func<ProviderRequest,ProviderDocument> fixture)=>new((r,ct)=>{ct.ThrowIfCancellationRequested();return Task.FromResult(fixture(r));});
    static Dictionary<string,string> Query(string value)=>new Uri(value).Query.TrimStart('?').Split('&',StringSplitOptions.RemoveEmptyEntries).Select(x=>x.Split('=',2)).ToDictionary(x=>Uri.UnescapeDataString(x[0]),x=>x.Length>1?Uri.UnescapeDataString(x[1]):"");
    static string Action(ProviderRequest request)=>Query(request.Url).GetValueOrDefault("action","");
    static string Utf8(string path)=>Encoding.UTF8.GetString(File.ReadAllBytes(path));
    static void NoSecret(string path){Assert(!Utf8(path).Contains(Secret,StringComparison.Ordinal),"Raw private fixture token found in persisted file.");}

    public static int Run(string report,bool network=false)=>RunAsync(report,network).GetAwaiter().GetResult();
    static async Task<int> RunAsync(string report,bool network)
    {
        string fullReport=Path.GetFullPath(report),root=Path.Combine(Path.GetDirectoryName(fullReport)!,"provider-fixtures-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);var results=new List<Result>();int folders=0;
        string Folder()=>Path.Combine(root,(++folders).ToString("D3"));
        async Task Check(string name,Func<Task> test){try{await test();results.Add(new(name,true,"Passed"));}catch(Exception error){results.Add(new(name,false,error is FixtureFailure?error.Message:error.GetType().Name));}}

        await Check("reject_non_http_and_embedded_credentials",async()=>{
            foreach(string url in new[]{"file:///C:/Windows/win.ini","ftp://provider.example/list","https://user:pass@provider.example/list","https://provider.example/list#fragment"})
                await RejectSync<InvalidDataException>(()=>PrivateProviderClient.Url(url));
        });
        await Check("reject_private_literal_and_local_hosts",async()=>{
            foreach(string url in new[]{"http://127.0.0.1/list","http://10.0.0.1/list","http://169.254.169.254/list","http://192.168.1.1/list","http://localhost/list","http://router.lan/list"})
                await RejectSync<InvalidDataException>(()=>PrivateProviderClient.Url(url));
        });
        await Check("explicit_public_nonstandard_port_allowed",()=>{Assert(PrivateProviderClient.Url("https://provider.example:8443/list").Port==8443,"Public provider port rejected.");return Task.CompletedTask;});
        await Check("m3u_extensionless_and_query_source",async()=>{
            var source=Source();var catalog=await Client(r=>Doc(r,ListBody)).Refresh(source,CancellationToken.None);
            Assert(catalog.Channels.Count==1&&catalog.Channels[0].Station.Name=="Private Fixture Channel","Extensionless M3U body not parsed.");
            Assert(catalog.Channels[0].Station.Url.Contains(Secret),"Playback token lost inside protected provider data.");
        });
        await Check("m3u_relative_urls_and_playback_headers",async()=>{
            var source=Source(url:"https://provider.example/base/catalog");
            string text="#EXTM3U\n#EXTINF:-1,Relative\n#EXTVLCOPT:http-user-agent=FixtureAgent\n#EXTVLCOPT:http-referrer=https://provider.example/watch\nvideo/live.m3u8\n";
            var s=(await Client(r=>Doc(r,text)).Refresh(source,CancellationToken.None)).Channels.Single().Station;
            Assert(s.Url=="https://provider.example/base/video/live.m3u8"&&s.UserAgent=="FixtureAgent"&&s.Referrer=="https://provider.example/watch","Relative stream or permitted playback header lost.");
        });
        await Check("m3u_header_epg_preserved_in_private_catalog",async()=>{
            string text=ListBody.Replace("#EXTM3U","#EXTM3U x-tvg-url=\"https://provider.example/guide.xml?token="+Secret+"\"");
            var c=await Client(r=>Doc(r,text)).Refresh(Source(),CancellationToken.None);Assert(c.EpgUrl.EndsWith(Secret),"Own EPG URL missing.");
        });
        await Check("private_cookie_auth_and_kodi_playlist_headers_rejected",async()=>{
            foreach(string directive in new[]{"#EXTHTTP:{\"Cookie\":\"fixture\"}","#EXTVLCOPT:http-cookie=fixture","#EXTVLCOPT:http-authorization=Bearer fixture","#EXTVLCOPT:http-header=Authorization: fixture","#KODIPROP:inputstream.adaptive.license_key=https://license.example/key"})
                await Reject<InvalidDataException>(()=>Client(r=>Doc(r,ListBody.Replace("#EXTINF:",directive+"\n#EXTINF:"))).Refresh(Source(),CancellationToken.None));
            string pipe=ListBody.Replace("?token="+Secret,"|Cookie=fixture");
            await Reject<InvalidDataException>(()=>Client(r=>Doc(r,pipe)).Refresh(Source(),CancellationToken.None));
        });
        await Check("secret_referrer_rejected_but_public_referrer_preserved",async()=>{
            var source=Source();var client=Client(r=>Doc(r,ListBody));var c=await client.Refresh(source,CancellationToken.None);var channel=c.Channels.Single();
            foreach(string referer in new[]{"https://provider.example/watch?token="+Secret,"https://provider.example/live/user/pass/12.m3u8"})
                await Reject<InvalidDataException>(()=>client.Resolve(source,channel with{Station=channel.Station with{Referrer=referer}},CancellationToken.None));
            var safe=await client.Resolve(source,channel with{Station=channel.Station with{Referrer="https://provider.example/watch"}},CancellationToken.None);
            Assert(safe.Station.Referrer=="https://provider.example/watch","Public noncredential Referer was discarded.");
        });
        await Check("hls_is_single_transmission_not_segment_playlist",async()=>{
            var s=Source(url:"https://provider.example/live.m3u8");
            var c=await Client(r=>Doc(r,"#EXTM3U\n#EXT-X-TARGETDURATION:4\n#EXTINF:4,\npart.ts\n")).Refresh(s,CancellationToken.None);
            Assert(c.Channels.Count==1&&c.Channels[0].Station.Url==s.Url,"HLS segments incorrectly imported as channels.");
        });
        await Check("html_and_empty_m3u_rejected",async()=>{
            foreach(string body in new[]{"<html>Sign in</html>","#EXTM3U\n"})await Reject<InvalidDataException>(()=>Client(r=>Doc(r,body)).Refresh(Source(),CancellationToken.None));
        });
        await Check("future_modified_date_rejected",()=>Reject<InvalidDataException>(()=>Client(r=>Doc(r,ListBody) with{Modified=DateTimeOffset.UtcNow.AddHours(1)}).Refresh(Source(),CancellationToken.None)));
        await Check("query_token_cross_origin_redirect_blocked",async()=>{
            int requests=0;var c=Client(r=>{requests++;return Doc(r,"",302,"https://different.example/list");});
            await Reject<InvalidDataException>(()=>c.Refresh(Source(),CancellationToken.None));Assert(requests==1,"Credential redirect reached a second host.");
        });
        await Check("public_redirect_allowed_and_resolves_relative_channels",async()=>{
            int requests=0;var c=Client(r=>++requests==1?Doc(r,"",302,"https://cdn.example/folder/list"):Doc(r,"#EXTM3U\n#EXTINF:-1,Relative\nstream.m3u8\n"));
            var catalog=await c.Refresh(Source(url:"https://provider.example/list"),CancellationToken.None);
            Assert(requests==2&&catalog.Channels[0].Station.Url=="https://cdn.example/folder/stream.m3u8","Redirect base was not honored.");
        });
        await Check("private_redirect_and_tls_downgrade_blocked",async()=>{
            foreach(string destination in new[]{"https://127.0.0.1/private","http://provider.example/list"}){
                int requests=0;var c=Client(r=>{requests++;return Doc(r,"",302,destination);});
                await Reject<InvalidDataException>(()=>c.Refresh(Source(url:"https://provider.example/list"),CancellationToken.None));Assert(requests==1,"Unsafe second request occurred.");
            }
        });
        await Check("redirect_loop_bounded",async()=>{
            int count=0;var c=Client(r=>{count++;return Doc(r,"",302,"/loop");});
            await Reject<InvalidDataException>(()=>c.Refresh(Source(url:"https://provider.example/list"),CancellationToken.None));Assert(count<=5,"Unbounded redirect loop.");
        });

        PrivateProviderClient XtreamFixture(string auth="1",string status="Active",long expiry=0,bool categoriesFail=false)=>Client(r=>{
            string action=Action(r);
            if(action=="get_live_categories")return categoriesFail?Doc(r,"temporary failure",500):Doc(r,"[{\"category_id\":\"7\",\"category_name\":\"News\"}]");
            if(action=="get_live_streams")return Doc(r,"[{\"stream_id\":12,\"name\":\"Fixture TV\",\"category_id\":\"7\",\"epg_channel_id\":\"fixture.pl\"}]");
            return Doc(r,JsonSerializer.Serialize(new{user_info=new{auth,status,exp_date=expiry,allowed_output_formats=new[]{"m3u8","ts"}}}));
        });
        ProviderSource XtreamSource()=>Source(PrivateSourceKind.Xtream,"https://provider.example:8080/prefix/player_api.php") with{Username="fixture user",Password=Secret};
        await Check("xtream_auth_categories_streams_and_xmltv",async()=>{
            var c=await XtreamFixture().Refresh(XtreamSource(),CancellationToken.None);var s=c.Channels.Single().Station;
            Assert(s.Group=="News"&&s.ChannelId=="fixture.pl"&&s.Url.Contains("fixture%20user/")&&s.Url.EndsWith("/12.m3u8"),"Xtream stream/category/encoding mapping failed.");
            Assert(c.EpgUrl.Contains("/prefix/xmltv.php?")&&Query(c.EpgUrl)["password"]==Secret,"Xtream EPG credentials were not scoped to own provider.");
        });
        await Check("xtream_inactive_and_expired_accounts_rejected",async()=>{
            await Reject<UnauthorizedAccessException>(()=>XtreamFixture("0").Refresh(XtreamSource(),CancellationToken.None));
            await Reject<UnauthorizedAccessException>(()=>XtreamFixture(status:"Disabled").Refresh(XtreamSource(),CancellationToken.None));
            await Reject<UnauthorizedAccessException>(()=>XtreamFixture(expiry:DateTimeOffset.UtcNow.AddDays(-1).ToUnixTimeSeconds()).Refresh(XtreamSource(),CancellationToken.None));
        });
        await Check("xtream_optional_category_failure_keeps_live_channels",async()=>{
            var c=await XtreamFixture(categoriesFail:true).Refresh(XtreamSource(),CancellationToken.None);Assert(c.Channels.Count==1&&c.Notice.Length>0,"Optional category outage removed valid live channels.");
        });

        await Check("registered_mac_handshake_profile_channels_create_link",async()=>{
            var requests=new List<ProviderRequest>();string token="fixture-session-only";
            var source=Source(PrivateSourceKind.Portal,"https://portal.example/stalker_portal/c/") with{Mac="02:00:00:00:00:01"};
            var client=Client(r=>{
                requests.Add(r);
                if(new Uri(r.Url).AbsolutePath=="/stalker_portal/portal.php")return Doc(r,"not found",404);
                Assert(new Uri(r.Url).AbsolutePath=="/stalker_portal/server/load.php","Portal endpoint not normalized.");
                Assert(r.Headers?.GetValueOrDefault("Cookie","").Contains("02:00:00:00:00:01")==true,"User-supplied MAC not sent.");
                if(Action(r)=="get_genres")return Doc(r,"Authorization failed.",403);
                if(Action(r)=="handshake")return Doc(r,JsonSerializer.Serialize(new{js=new{token}}));
                Assert(r.Headers?.GetValueOrDefault("Authorization")=="Bearer "+token,"Session token missing after handshake.");
                return Action(r) switch{
                    "get_profile"=>Doc(r,"{\"js\":{\"id\":1,\"status\":0,\"blocked\":0}}"),
                    "get_all_channels"=>Doc(r,"{\"js\":{\"data\":[{\"id\":\"11\",\"name\":\"Portal TV\",\"cmd\":\"ffmpeg http://localhost/ch/11\",\"xmltv_id\":\"portal.pl\"}]}}"),
                    "create_link"=>Doc(r,"{\"js\":{\"cmd\":\"ffmpeg https://media.example/portal.m3u8?token="+Secret+"\"}}"),
                    _=>throw new FixtureFailure("Unexpected portal action.")};
            });
            var catalog=await client.Refresh(source,CancellationToken.None);var play=await client.Resolve(source,catalog.Channels.Single(),CancellationToken.None);
            Assert(play.Station.Url.Contains(Secret)&&requests.Count==10,"Portal fresh-session playback flow failed.");
            Assert(Query(requests.Last().Url)["cmd"]=="ffmpeg http://localhost/ch/11","Portal command was altered or executed locally.");
        });
        await Check("mac_invalid_format_and_blocked_profile_rejected",async()=>{
            var source=Source(PrivateSourceKind.Portal,"https://portal.example/c/") with{Mac="02:00:00:00:00:01"};
            await RejectSync<InvalidDataException>(()=>PrivateProviderClient.Validate(source with{Mac="scan-random-devices"}));
            var c=Client(r=>Action(r)=="get_genres"?Doc(r,"",401):Doc(r,Action(r)=="handshake"?"{\"js\":{\"token\":\"fixture\"}}":"{\"js\":{\"id\":1,\"blocked\":1}}"));
            await Reject<UnauthorizedAccessException>(()=>c.Refresh(source,CancellationToken.None));
        });
        await Check("portal_profile_accepts_positive_integer_id_and_explicit_inactive_flags",()=>{
            foreach(string body in new[]{"{\"id\":1}","{\"id\":9223372036854775807,\"status\":0,\"blocked\":false}","{\"id\":\" 001 \",\"status\":\"0\",\"blocked\":\" FALSE \"}","{\"id\":2,\"status\":null,\"blocked\":\" \"}","{\"id\":2,\"status\":false,\"blocked\":-0}","{\"id\":\" \\t1\\r\\n\",\"status\":\"\\tfalse\\r\\n\",\"blocked\":\" \\t\\r\\n\"}"})
            {using var doc=JsonDocument.Parse(body);PrivateProviderClient.ValidatePortalProfile(doc.RootElement);}return Task.CompletedTask;
        });
        await Check("portal_profile_rejects_missing_invalid_fractional_or_overflow_id",async()=>{
            foreach(string body in new[]{"{}","null","[]","true","{\"status\":0}"}){using var doc=JsonDocument.Parse(body);await RejectSync<UnauthorizedAccessException>(()=>PrivateProviderClient.ValidatePortalProfile(doc.RootElement));}
            foreach(string id in new[]{"null","false","true","0","-1","1.5","1.0","1e0","1e-999","9223372036854775808","\"0\"","\"-1\"","\"1.0\"","\"1e0\"","\"9223372036854775808\"","\"abc\"","\"\"","\"١\"","{}","[]"})
            {using var doc=JsonDocument.Parse("{\"id\":"+id+",\"status\":0}");await RejectSync<UnauthorizedAccessException>(()=>PrivateProviderClient.ValidatePortalProfile(doc.RootElement));}
        });
        await Check("portal_profile_rejects_active_unknown_and_lossy_flag_values",async()=>{
            foreach(string name in new[]{"status","blocked"})foreach(string flag in new[]{"true","1","-1","0.0","0e0","1e-999","9223372036854775808","\"true\"","\"1\"","\"00\"","\"0.0\"","\"unknown\"","\"NaN\"","{}","[]"})
            {using var doc=JsonDocument.Parse("{\"id\":1,\""+name+"\":"+flag+"}");await RejectSync<UnauthorizedAccessException>(()=>PrivateProviderClient.ValidatePortalProfile(doc.RootElement));}
        });
        await Check("portal_profile_rejects_control_and_unicode_whitespace_padding",async()=>{
            foreach(char padding in new[]{'\u0000','\u001f','\u00a0','\u2003'})
            {
                using var id=JsonDocument.Parse(JsonSerializer.Serialize(new{id=padding+"1"+padding}));await RejectSync<UnauthorizedAccessException>(()=>PrivateProviderClient.ValidatePortalProfile(id.RootElement));
                foreach(string name in new[]{"status","blocked"}){using var flag=JsonDocument.Parse("{\"id\":1,\""+name+"\":"+JsonSerializer.Serialize(padding+"false"+padding)+"}");await RejectSync<UnauthorizedAccessException>(()=>PrivateProviderClient.ValidatePortalProfile(flag.RootElement));}
            }
        });
        await Check("portal_rejected_profile_stops_before_channel_list_and_playback_link",async()=>{
            var source=Source(PrivateSourceKind.Portal,"https://example.test/stalker_portal/c/") with{Mac="02:00:00:00:00:01"};
            foreach(string profile in new[]{"{\"id\":0}","{\"id\":1,\"status\":true}","{\"id\":1,\"blocked\":true}","{\"id\":1,\"blocked\":\"unknown\"}"})
            {
                var actions=new List<string>();var client=Client(r=>{string action=Action(r);actions.Add(action);return action=="get_genres"?Doc(r,"",401):Doc(r,action=="handshake"?"{\"js\":{\"token\":\"fixture-session\"}}":"{\"js\":"+profile+"}");});
                await Reject<UnauthorizedAccessException>(()=>client.Refresh(source,CancellationToken.None));Assert(actions.SequenceEqual(new[]{"get_genres","handshake","get_profile"}),"Rejected profile reached channel listing.");actions.Clear();
                await Reject<UnauthorizedAccessException>(()=>client.Resolve(source,new("1",new("Fixture channel","","TV"),"fixture command"),CancellationToken.None));Assert(actions.SequenceEqual(new[]{"get_genres","handshake","get_profile"}),"Rejected profile reached playback-link API.");
            }
        });

        ProviderSource Drm()=>Source(PrivateSourceKind.ClearKeyDash,"https://media.example/video.mpd") with{LicenseUrl="https://license.example/key",KeyId=KidHex};
        await Check("clearkey_license_post_matches_kid_and_extracts_key",async()=>{
            bool posted=false;var client=Client(r=>{
                if(r.Method=="GET")return Doc(r,"\uFEFF"+Manifest); // Published MPDs may include a UTF-8 BOM.
                using var payload=JsonDocument.Parse(r.Body);Assert(payload.RootElement.GetProperty("kids")[0].GetString()==Kid64,"Wrong ClearKey KID request.");
                posted=true;return Doc(r,"{\"keys\":[{\"kty\":\"oct\",\"kid\":\""+Kid64+"\",\"k\":\""+Key64+"\"}],\"type\":\"temporary\"}");
            });
            var source=Drm();var c=await client.Refresh(source,CancellationToken.None);var p=await client.Resolve(source,c.Channels.Single(),CancellationToken.None);
            Assert(posted&&p.ClearKeyHex=="00112233445566778899aabbccddeeff","License key not decoded correctly.");
        });
        await Check("clearkey_wrong_kid_and_mixed_keys_rejected",async()=>{
            var source=Drm();var wrong=Client(r=>Doc(r,r.Method=="GET"?Manifest:"{\"keys\":[{\"kty\":\"oct\",\"kid\":\"different\",\"k\":\""+Key64+"\"}]}"));
            var c=await wrong.Refresh(source,CancellationToken.None);await Reject<InvalidDataException>(()=>wrong.Resolve(source,c.Channels.Single(),CancellationToken.None));
            string multi=Manifest.Replace("</Period>","<AdaptationSet xmlns:cenc=\"urn:mpeg:cenc:2013\" cenc:default_KID=\"00000000-0000-0000-0000-000000000001\" /></Period>");
            await Reject<InvalidDataException>(()=>Client(r=>Doc(r,multi)).Refresh(source,CancellationToken.None));
        });
        await Check("clearkey_explicit_raw_key_resolves_without_license_request",async()=>{
            int requests=0;var client=Client(r=>{requests++;Assert(r.Method=="GET","Raw key unexpectedly requested a license.");return Doc(r,Manifest);});
            var source=Drm() with{LicenseUrl="",KeyHex="00112233445566778899AABBCCDDEEFF"};
            var c=await client.Refresh(source,CancellationToken.None);var resolved=await client.Resolve(source,c.Channels.Single(),CancellationToken.None);
            Assert(resolved.ClearKeyHex=="00112233445566778899aabbccddeeff"&&requests==1,"Raw key resolution or normalization failed.");
            await RejectSync<InvalidDataException>(()=>PrivateProviderClient.Validate(source with{KeyHex="invalid"}));
        });
        await Check("license_post_cross_origin_redirect_blocked",async()=>{
            int posts=0;var client=Client(r=>r.Method=="GET"?Doc(r,Manifest):(++posts>0?Doc(r,"",307,"https://other-license.example/key"):Doc(r,"")));
            var source=Drm();var c=await client.Refresh(source,CancellationToken.None);await Reject<InvalidDataException>(()=>client.Resolve(source,c.Channels.Single(),CancellationToken.None));Assert(posts==1,"License body crossed origin.");
        });
        await Check("dpapi_round_trip_and_purpose_binding",async()=>{
            byte[] plain=Encoding.UTF8.GetBytes(Secret),cipher=PrivateData.Protect(plain,"fixture-purpose");Assert(!Encoding.UTF8.GetString(cipher).Contains(Secret),"DPAPI ciphertext exposes plaintext.");
            Assert(Encoding.UTF8.GetString(PrivateData.Unprotect(cipher,"fixture-purpose"))==Secret,"DPAPI round trip failed.");
            await RejectSync<CryptographicException>(()=>PrivateData.Unprotect(cipher,"other-purpose"));
        });
        await Check("vault_retains_previous_catalog_and_timestamp_after_failure",async()=>{
            bool fail=false;var client=Client(r=>fail?Doc(r,"outage",503):Doc(r,ListBody));var source=Source();string folder=Folder();var store=new PrivateSourceStore(folder,client);store.Put(source);
            var first=await store.Refresh(source.Id,CancellationToken.None);fail=true;var failed=await store.Refresh(source.Id,CancellationToken.None);
            Assert(failed.Catalog?.DownloadedAt==first.Catalog?.DownloadedAt&&store.Stations(source.Id).Count==1,"Outage replaced last good catalog or timestamp.");
            foreach(string file in Directory.GetFiles(folder))NoSecret(file);
            Assert(new PrivateSourceStore(folder,client).Stations(source.Id).Count==1,"Protected catalog did not survive reload.");
        });
        await Check("profile_isolation_and_removed_source_reference",async()=>{
            var client=Client(r=>Doc(r,ListBody));var source=Source();var a=new PrivateSourceStore(Folder(),client);a.Put(source);await a.Refresh(source.Id,CancellationToken.None);var reference=a.Stations(source.Id).Single();
            var b=new PrivateSourceStore(Folder(),client);Assert(b.Stations(source.Id).Count==0,"Other profile saw private catalog.");
            await Reject<InvalidOperationException>(()=>b.Resolve(reference,CancellationToken.None));a.Remove(source.Id);await Reject<InvalidOperationException>(()=>a.Resolve(reference,CancellationToken.None));
        });
        await Check("config_change_requires_refresh_before_private_playback",async()=>{
            var client=Client(r=>Doc(r,ListBody));var source=Source();var store=new PrivateSourceStore(Folder(),client);store.Put(source);await store.Refresh(source.Id,CancellationToken.None);var reference=store.Stations(source.Id).Single();
            store.Put(source with{Url="https://provider.example/new-list?token="+Secret});await Reject<InvalidOperationException>(()=>store.Resolve(reference,CancellationToken.None));
        });
        await Check("plain_library_history_favorites_and_export_hide_private_data",()=>{
            var raw=new Station("Sensitive Fixture Title", "https://media.example/private?token="+Secret,Group:Secret,Homepage:"https://private.example/"+Secret,UserAgent:Secret,Referrer:"https://private.example/"+Secret);
            var reference=PrivateStation.Reference(Guid.NewGuid().ToString("N"),new ProviderChannel("fixture-channel",raw));string folder=Folder();var library=new LibraryStore(folder);
            library.Data.Playlist.Add(reference);library.Favorite(reference,true);library.Played(reference);library.Save();
            foreach(string file in Directory.GetFiles(folder))NoSecret(file);
            string json=File.ReadAllText(library.FilePath);Assert(!json.Contains("Sensitive Fixture Title")&&json.Contains("swir-private://"),"Plain library metadata is not masked.");
            string exported=M3u.Export(new[]{reference,new Station("Public","https://public.example/live.m3u8")});
            Assert(!exported.Contains(Secret)&&!exported.Contains("swir-private")&&exported.Contains("https://public.example/live.m3u8"),"Export revealed private source or omitted public source.");return Task.CompletedTask;
        });
        await Check("recovered_backup_survives_next_save",async()=>{
            string folder=Folder();var client=Client(r=>Doc(r,ListBody));var source=Source();var original=new PrivateSourceStore(folder,client);original.Put(source);await original.Refresh(source.Id,CancellationToken.None);
            string main=Path.Combine(folder,"private-sources.dpapi"),backup=main+".bak";File.Copy(main,backup,true);File.WriteAllBytes(main,new byte[]{0,1,2,3});
            var recovered=new PrivateSourceStore(folder,client);Assert(recovered.Stations(source.Id).Count==1&&recovered.Notice.Length>0,"Backup was not recovered.");
            recovered.Put(source with{Name="Renamed fixture"});var savedBackup=PrivateData.Read<ProviderVault>(backup,"provider-vault-v1");
            Assert(savedBackup.Sources.Count==1&&savedBackup.Sources[0].Catalog?.Channels.Count==1,"Good backup was replaced by corrupt main.");NoSecret(main);NoSecret(backup);
            recovered.Put(source with{Name="Second fixture change"});
            var secondBackup=PrivateData.Read<ProviderVault>(backup,"provider-vault-v1");
            Assert(secondBackup.Sources.Single().Source.Name=="Renamed fixture","Normal backup rotation did not resume after recovery save.");
            Assert(new PrivateSourceStore(folder,client).Sources.Single().Source.Name=="Second fixture change","Second save after recovery was not durable.");
        });
        await Check("cancelled_refresh_preserves_catalog",async()=>{
            var source=Source();var store=new PrivateSourceStore(Folder(),Client(r=>Doc(r,ListBody)));store.Put(source);await store.Refresh(source.Id,CancellationToken.None);var before=store.Sources.Single().Catalog!.DownloadedAt;
            using var cancel=new CancellationTokenSource();cancel.Cancel();await Reject<OperationCanceledException>(()=>store.Refresh(source.Id,cancel.Token));
            Assert(store.Sources.Single().Catalog!.DownloadedAt==before&&store.Stations(source.Id).Count==1,"Cancellation changed the cached library.");
        });
        await Check("file_playlist_and_direct_url_are_explicit_private_sources",async()=>{
            string folder=Folder();Directory.CreateDirectory(folder);string file=Path.Combine(folder,"own-list.m3u");File.WriteAllText(file,ListBody);int networkCalls=0;var client=Client(r=>{networkCalls++;throw new FixtureFailure("Unexpected network request.");});
            var local=await client.Refresh(Source(PrivateSourceKind.M3uFile,file),CancellationToken.None);var direct=await client.Refresh(Source(PrivateSourceKind.DirectUrl,"https://media.example/direct?token="+Secret),CancellationToken.None);
            Assert(local.Channels.Count==1&&direct.Channels.Count==1&&networkCalls==0,"File/direct source unexpectedly crawled the network.");
        });
        await Check("source_limit_prevents_unbounded_private_catalogs",async()=>{
            var store=new PrivateSourceStore(Folder());for(int i=0;i<12;i++)store.Put(Source());
            await RejectSync<InvalidOperationException>(()=>store.Put(Source()));Assert(store.Sources.Count==12,"Source limit mutated the saved set.");
        });
        if(network)await Check("fresh_public_m3u_document_only",async()=>{
            var source=Source(url:"https://iptv-org.github.io/iptv/countries/pl.m3u");var catalog=await new PrivateProviderClient().Refresh(source,CancellationToken.None);
            Assert(catalog.Channels.Count>0,"Public M3U returned no entries; this check does not prove playback.");
        });
        Directory.CreateDirectory(Path.GetDirectoryName(fullReport)!);
        File.WriteAllText(fullReport,JsonSerializer.Serialize(new{verifiedAtUtc=DateTimeOffset.UtcNow,mode=network?"controlled fixtures + public document":"controlled offline fixtures",fixtureOnlyCredentials=true,success=results.All(r=>r.Passed),passed=results.All(r=>r.Passed),total=results.Count,checks=results},new JsonSerializerOptions{WriteIndented=true}));
        return results.All(r=>r.Passed)?0:1;
    }
}
