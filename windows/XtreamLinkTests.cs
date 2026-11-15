using System.Text;
using System.Text.Json;
namespace SwirStreamAI;

public static class XtreamLinkTests
{
    public static int Run(string report)
    {
        var checks=new List<object>();bool success=true;void Check(string name,bool ok){checks.Add(new{name,ok});success&=ok;}
        bool Refused(string value,bool explicitMode=false){try{XtreamLink.Parse(value,explicitMode);return false;}catch(InvalidDataException e){return !e.Message.Contains("fixture-secret");}}
        async Task Tests()
        {
            var account=XtreamLink.Parse(" \"https://provider.example:8443/tenant%2Fblue/get.php?username=za%C5%BC%C3%B3%C5%82%C4%87+%2B&amp;password=fixture-secret%252B%26x&amp;type=m3u_plus&amp;output=M3U8\" ")!;
            Check("Forum copy preserves escaped base path, port and UTF8 credentials decoded once",account.Server=="https://provider.example:8443/tenant%2Fblue/"&&account.Username=="zażółć +"&&account.Password=="fixture-secret%2B&x"&&account.PlaylistLink);
            Check("Safe account string representation never includes login or password",!account.ToString().Contains("fixture-secret")&&!account.ToString().Contains("zażółć"));
            foreach(var endpoint in new[]{"get.php","player_api.php","xmltv.php"})Check("Recognizes account endpoint "+endpoint,XtreamLink.Parse("https://provider.example/sub/"+endpoint+"?username=user&password=fixture-secret")?.Server=="https://provider.example/sub/");
            var path=XtreamLink.Parse("https://provider.example:8080/prefix/live/a+b/fixture-secret%2B/23.m3u8")!;Check("Live path keeps literal plus and escaped plus",path.Username=="a+b"&&path.Password=="fixture-secret+"&&path.Server=="https://provider.example:8080/prefix/");
            Check("Explicit bare account URL supported without generic guessing",Refused("https://provider.example/base/?username=user&password=fixture-secret")&&XtreamLink.Parse("https://provider.example/base/?username=user&password=fixture-secret",true)?.Server=="https://provider.example/base/");
            foreach(var query in new[]{"username=u","password=fixture-secret","username=&password=fixture-secret","username=u&password=","username=u&username=u&password=fixture-secret","username=u&Password=fixture-secret&password=fixture-secret","username=u&password=fixture-secret&token=routing","username=u&password=fixture-secret&output=wrong","username=u;password=fixture-secret","username=u&password=%GGfixture-secret","username=u&password=%C3%28fixture-secret","username=u&password=%00fixture-secret"})Check("Rejects ambiguous/invalid account case "+checks.Count,Refused("https://provider.example/get.php?"+query));
            Check("Generic own playlist query flags and repeated tags are not account parameters",XtreamLink.Parse("https://provider.example/list.m3u?download&tag=one&tag=two")==null);
            Check("Unknown account endpoint is not silently rewritten",Refused("https://provider.example/custom.php?username=u&password=fixture-secret",true));
            Check("Mixed path credentials and query are not silently discarded",Refused("https://provider.example/live/user/fixture-secret/42.ts?token=routing"));
            Check("Userinfo, fragment and nonpublic host cannot become account imports",Refused("https://user:fixture-secret@provider.example/get.php?username=u&password=p")&&Refused("https://provider.example/get.php?username=u&password=fixture-secret#tail")&&Refused("http://127.0.0.1/get.php?username=u&password=fixture-secret"));
            string id=Guid.NewGuid().ToString("N");var raw=new ProviderSource(id,"Moje konto",PrivateSourceKind.Xtream,"https://provider.example:8080/base/get.php?username=u%2B&password=fixture-secret+value");var normalized=XtreamLink.Normalize(raw);
            Check("Full URL in server field fills account and preserves source ID",normalized.Id==id&&normalized.Url=="https://provider.example:8080/base/"&&normalized.Username=="u+"&&normalized.Password=="fixture-secret value");
            bool conflict=false;try{XtreamLink.Normalize(raw with{Password="different"});}catch(InvalidDataException){conflict=true;}Check("Conflicting manually supplied credential is never overwritten silently",conflict);
            foreach(var input in new[]{raw.Url,"https://provider.example/list.m3u?token=fixture-secret","https://provider.example/get.php?%75sername%00=u&%70assword%00=fixture-secret","https%3A%2F%2Fprovider.example%2Fget.php%3Fusername%3Du%26password%3Dfixture-secret","provider.example/get.php?username=u&password=fixture-secret","https://broadcaster.example/live/news/hls/master.m3u8"})Check("Search intercept keeps pasted link local "+checks.Count,XtreamLink.PrivateInput(input));
            Check("Ordinary channel names remain local search queries",!XtreamLink.PrivateInput("CANAL+ Sport")&&!XtreamLink.PrivateInput("TVP1")&&!XtreamLink.PrivateInput("Polskie Radio"));
            string folder=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(report))!,"transaction-fixture-"+Guid.NewGuid().ToString("N"));bool fail=false,block=false;var started=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var calls=new List<string>();
            var client=new PrivateProviderClient(async(request,ct)=>
            {
                calls.Add(request.Url);if(block){started.TrySetResult();await release.Task.WaitAsync(ct);}if(fail)throw new IOException("controlled");
                string body=request.Url.Contains("get_live_categories")?"[]":request.Url.Contains("get_live_streams")?"[{\"stream_id\":1,\"name\":\"Kanał Test\"}]":request.Url.Contains("xmltv.php")?"<tv/>":"{\"user_info\":{\"auth\":1,\"status\":\"Active\"}}";
                return new ProviderDocument(Encoding.UTF8.GetBytes(body),request.Url);
            });
            var store=new PrivateSourceStore(folder,client);var first=await store.Connect(raw,CancellationToken.None);var station=store.Stations(id).Single();var date=first.Catalog!.DownloadedAt;
            Check("Paste account requests preserve prefix/port and encode decoded credentials once",calls.All(u=>u.StartsWith("https://provider.example:8080/base/"))&&calls.All(u=>u.Contains("username=u%2B&password=fixture-secret%20value")));
            var bad=normalized with{Password="changed-bad"};fail=true;var failed=await store.Connect(bad,CancellationToken.None);var oldPlayback=await store.Resolve(station,CancellationToken.None);
            Check("Failed edit retains working old credentials and playable reference",failed.Source==normalized&&failed.Catalog?.DownloadedAt==date&&oldPlayback.Station.Url.Contains("fixture-secret%20value"));fail=false;
            block=true;using(var cancel=new CancellationTokenSource()){var pending=store.Connect(bad,cancel.Token);await started.Task;cancel.Cancel();try{await pending;}catch(OperationCanceledException){}Check("Cancelled edit retains old config and catalogue",store.Sources.Single().Source==normalized&&store.Sources.Single().Catalog?.DownloadedAt==date);}block=false;
            var changed=await store.Connect(normalized with{Password="new-valid"},CancellationToken.None);Check("Successful edit commits new data under original source and channel IDs",changed.Source.Id==id&&changed.Source.Password=="new-valid"&&store.Stations(id).Single().PrivateStreamId==station.PrivateStreamId);
            started=new(TaskCreationOptions.RunContinuationsAsynchronously);release=new(TaskCreationOptions.RunContinuationsAsynchronously);block=true;var late=store.Refresh(id,CancellationToken.None);await started.Task;block=false;var newer=await store.Connect(normalized with{Password="newest"},CancellationToken.None);release.TrySetResult();bool rejected=false;try{await late;}catch(OperationCanceledException){rejected=true;}
            Check("A late refresh cannot overwrite a newer successful edit",rejected&&store.Sources.Single().Source.Password=="newest"&&store.Sources.Single().Catalog==newer.Catalog);
            Check("Vault reopen preserves committed settings without plaintext fixture credentials",new PrivateSourceStore(folder,client).Sources.Single().Source.Password=="newest"&&!Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(folder,"private-sources.dpapi"))).Contains("fixture-secret"));
            using var cancelled=new CancellationTokenSource();cancelled.Cancel();try{await store.Connect(normalized with{Id=Guid.NewGuid().ToString("N")},cancelled.Token);}catch(OperationCanceledException){}Check("Cancelled new import does not leave a saved empty source",store.Sources.Count==1);
        }
        try{Tests().GetAwaiter().GetResult();}catch(Exception ex){checks.Add(new{name="Unexpected failure",ok=false,detail=ex.GetType().Name});success=false;}
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(report))!);File.WriteAllText(report,JsonSerializer.Serialize(new{success,checks},new JsonSerializerOptions{WriteIndented=true}));return success?0:1;
    }
}
