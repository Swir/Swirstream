using System.Text;
using System.Text.Json;

namespace SwirStreamAI;

public static class PortalScannerTests
{
    const string Url="https://portal.fixture.example/c/",Mac="02:00:00:00:00:98";
    const string Genres="{\"js\":[{\"id\":1,\"title\":\"Fixture\"}]}";
    const string Channels="{\"js\":{\"data\":[{\"id\":1,\"name\":\"Fixture\",\"cmd\":\"ffmpeg http://localhost/ch/1\"}]}}";
    sealed class Failure(string message):Exception(message);
    static void Assert(bool value,string detail){if(!value)throw new Failure(detail);}
    static PortalTxtImport Parse(string text)=>PortalTxt.Parse(Encoding.UTF8.GetBytes(text));
    static ProviderDocument Reply(ProviderRequest request,string body)=>new(Encoding.UTF8.GetBytes(body),request.Url,"application/json");
    static PrivateProviderClient Client(Action<ProviderRequest>? observe=null)=>new((request,ct)=>{ct.ThrowIfCancellationRequested();observe?.Invoke(request);return Task.FromResult(Reply(request,request.Url.Contains("action=get_genres")?Genres:Channels));});
    public static int Run(string report,string fixtures)=>RunAsync(report,fixtures).GetAwaiter().GetResult();
    static async Task<int> RunAsync(string report,string fixtures)
    {
        var checks=new List<object>();bool success=true;
        async Task Check(string name,Func<Task> test)
        {try{await test().WaitAsync(TimeSpan.FromSeconds(8));checks.Add(new{name,ok=true});}catch(Exception error){success=false;checks.Add(new{name,ok=false,reason=error is Failure?error.Message:error.GetType().Name});}}
        if(File.Exists(fixtures))
        {
            using var doc=JsonDocument.Parse(File.ReadAllBytes(fixtures));
            foreach(var fixture in doc.RootElement.GetProperty("cases").EnumerateArray())
            {
                string name=fixture.GetProperty("name").GetString()!;byte[] input=Convert.FromBase64String(fixture.GetProperty("inputBase64").GetString()!);var expected=fixture.GetProperty("expected").Clone();
                await Check("parity_"+name,()=>
                {
                    PortalTxtImport? actual=null;bool failed=false;try{actual=PortalTxt.Parse(input);}catch(InvalidDataException){failed=true;}
                    Assert(failed==expected.GetProperty("fatal").GetBoolean(),"Fatal mismatch");
                    if(!failed)
                    {
                        var entries=expected.GetProperty("entries").EnumerateArray().ToArray();
                        Assert(actual!.Entries.Count==entries.Length,"Entry count mismatch");
                        for(int i=0;i<entries.Length;i++)Assert(actual.Entries[i].Url==entries[i].GetProperty("url").GetString()&&actual.Entries[i].Mac==entries[i].GetProperty("mac").GetString(),"Entry content mismatch");
                        Assert(actual.InvalidLines==expected.GetProperty("invalidLines").GetInt32()&&actual.Duplicates==expected.GetProperty("duplicates").GetInt32(),"Counter mismatch");
                    }
                    return Task.CompletedTask;
                });
            }
        }
        else await Check("shared_parity_fixtures_present",()=>throw new Failure("Fixture file missing"));
        await Check("raw_byte_limit_checked_before_decode",async()=>
        {
            using var input=new MemoryStream(new byte[PortalTxt.MaxBytes+8192]);bool failed=false;
            try{await PortalTxt.Read(input,CancellationToken.None);}catch(InvalidDataException){failed=true;}
            Assert(failed&&input.Position<=PortalTxt.MaxBytes+8192,"Unbounded input read");
        });
        await Check("inline_pair_preserves_following_mac_and_encoded_percent",()=>
        {
            var parsed=Parse("https://portal.fixture.example/my%25portal/c/|"+Mac+"\n02:00:00:00:00:99");
            Assert(parsed.InvalidLines==0&&parsed.Entries.Count==2&&parsed.Entries.All(e=>e.Url=="https://portal.fixture.example/my%25portal/c/"),"Inline block or encoded path lost");return Task.CompletedTask;
        });
        await Check("entry_limit_is_atomic_and_duplicates_do_not_consume_limit",()=>
        {
            string text=string.Join('\n',Enumerable.Range(0,200).Select(i=>$"https://portal{i}.fixture.example/c/"));
            Assert(Parse(text+"\nhttps://portal1.fixture.example/c/").Entries.Count==200,"Duplicate consumed slot");
            bool failed=false;try{Parse(text+"\nhttps://overflow.fixture.example/c/");}catch(InvalidDataException){failed=true;}Assert(failed,"201 entries accepted");return Task.CompletedTask;
        });
        await Check("invalid_line_details_bounded_and_secret_free",()=>
        {
            var result=Parse(string.Join('\n',Enumerable.Repeat("synthetic-password=never-export-this",5000)));
            Assert(result.InvalidLines==5000&&result.Issues.Count==50&&result.Entries.Count==0,"Invalid detail cap failed");
            Assert(!JsonSerializer.Serialize(result.Issues).Contains("synthetic"),"Raw input in errors");return Task.CompletedTask;
        });
        await Check("url_only_uses_public_page_without_private_client",async()=>
        {
            int calls=0;var forbidden=new PrivateProviderClient((request,ct)=>throw new Failure("Unexpected authenticated request"));
            var scanner=new PortalScanner(forbidden,(url,ct)=>{calls++;Assert(url==Url,"Changed supplied URL");return Task.FromResult(new WebDocument(url,"<title>Stalker portal</title><script src='xpcom.common.js'></script>","text/html"));});
            var row=await scanner.Check(Parse(Url).Entries.Single(),CancellationToken.None);
            Assert(calls==1&&row.Status==PortalScanStatus.RecognizedPortal&&row.Channels==0,"Public page incorrectly authenticated or counted");
            Assert(row.Description.Contains("niesprawdzony")&&!row.Description.Contains("działa"),"Public page presented as playback");
        });
        await Check("generic_http_page_is_not_portal_access",async()=>
        {
            var row=await new PortalScanner(page:(url,ct)=>Task.FromResult(new WebDocument(url,"<html>Hello</html>","text/html"))).Check(Parse(Url).Entries[0],CancellationToken.None);
            Assert(row.Status==PortalScanStatus.PublicPage&&row.Channels==0,"HTTP success became portal access");
        });
        await Check("api_and_custom_port_not_probed_publicly",async()=>
        {
            int calls=0;var scanner=new PortalScanner(page:(u,c)=>{calls++;throw new Failure("Unexpected API request");});
            foreach(string address in new[]{"https://portal.fixture.example/portal.php","https://portal.fixture.example:8080/c/"})
                Assert((await scanner.Check(Parse(address).Entries[0],CancellationToken.None)).Status==PortalScanStatus.UnsupportedPublicCheck,"Unsupported public endpoint accepted");
            Assert(calls==0,"Public API performed authentication");
        });
        await Check("exact_supplied_mac_production_catalog_without_media_or_save",async()=>
        {
            var requests=new List<ProviderRequest>();var scanner=new PortalScanner(Client(requests.Add));var row=await scanner.Check(Parse(Url+"|"+Mac).Entries[0],CancellationToken.None);
            Assert(row.Status==PortalScanStatus.Catalog&&row.Channels==1&&requests.Count==2,"Catalogue not acquired");
            Assert(requests.All(r=>r.Headers!["Cookie"].Contains(Mac))&&!requests.Any(r=>r.Url.Contains("create_link")),"Changed MAC or played during scan");
            Assert(row.Description.Contains("odtwarzanie niesprawdzone"),"Catalogue became playback claim");
        });
        await Check("denial_stops_without_other_mac_or_fallback",async()=>
        {
            int calls=0;var scanner=new PortalScanner(new PrivateProviderClient((r,c)=>{calls++;throw new UnauthorizedAccessException("synthetic-private-message");}));
            var row=await scanner.Check(Parse(Url+"|"+Mac).Entries[0],CancellationToken.None);
            Assert(row.Status==PortalScanStatus.Denied&&calls==1,"Denial retried or accepted");
        });
        await Check("export_has_no_address_mac_exception_or_formula",async()=>
        {
            var entry=Parse(Url+"|"+Mac).Entries[0];var scanner=new PortalScanner(new PrivateProviderClient((r,c)=>throw new IOException("synthetic-secret http://private.fixture.example/")));
            string csv=PortalScanner.Export([await scanner.Check(entry,CancellationToken.None)]);
            Assert(!csv.Contains("http")&&!csv.Contains(Mac)&&!csv.Contains("secret")&&!csv.Contains("private.fixture")&&csv.Contains("niesprawdzone"),"Sensitive diagnostics exported");
            Assert(entry.ToString()=="Portal 1","Entry ToString leaks source");
        });
        await Check("per_entry_timeout_does_not_claim_denial",async()=>
        {
            var scanner=new PortalScanner(page:async(u,c)=>{await Task.Delay(Timeout.Infinite,c);throw new Failure("Unreachable");},entryTimeout:TimeSpan.FromMilliseconds(40));
            var row=await scanner.Check(Parse(Url).Entries[0],CancellationToken.None);Assert(row.Status==PortalScanStatus.Timeout,"Timeout classified as valid/denied");
        });
        await Check("cancel_preserves_completed_results_and_unchecked_remainder",async()=>
        {
            using var cancel=new CancellationTokenSource();int calls=0;var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var scanner=new PortalScanner(page:async(u,c)=>{if(++calls==2){entered.TrySetResult();await Task.Delay(Timeout.Infinite,c);}return new(u,"<html>fixture</html>","text/html");},gap:TimeSpan.Zero);
            var entries=Parse(Url+"\nhttps://two.fixture.example/c/\nhttps://three.fixture.example/c/").Entries;
            var run=scanner.Scan(entries,cancel.Token);await entered.Task;cancel.Cancel();var result=await run;
            Assert(!result.Complete&&calls==2&&result.Rows[0].Status==PortalScanStatus.PublicPage&&result.Rows[1].Status==PortalScanStatus.Cancelled&&result.Rows[2].Status==PortalScanStatus.NotChecked,"Cancellation lost results or scanned remainder");
        });
        await Check("late_ignored_cancellation_cannot_publish_result",async()=>
        {
            var reply=new TaskCompletionSource<WebDocument>(TaskCreationOptions.RunContinuationsAsynchronously);using var cancel=new CancellationTokenSource();int events=0;
            var scanner=new PortalScanner(page:(u,c)=>reply.Task,gap:TimeSpan.Zero);var run=scanner.Scan(Parse(Url).Entries,cancel.Token,_=>events++);cancel.Cancel();var result=await run;int before=events;
            reply.SetResult(new(Url,"<title>stalker</title><script src='xpcom.common.js'></script>","text/html"));await Task.Delay(25);
            Assert(!result.Complete&&events==before&&result.Rows[0].Status==PortalScanStatus.Cancelled,"Late response overwrote cancellation");
        });
        await Check("pre_cancel_and_cancel_in_gap_leave_unstarted_rows_unchecked",async()=>
        {
            var entries=Parse(Url+"\nhttps://two.fixture.example/c/").Entries;int calls=0;
            var scanner=new PortalScanner(page:(u,c)=>{calls++;return Task.FromResult(new WebDocument(u,"<html>fixture</html>","text/html"));},gap:TimeSpan.FromSeconds(3));
            using var before=new CancellationTokenSource();before.Cancel();var none=await scanner.Scan(entries,before.Token);
            Assert(!none.Complete&&calls==0&&none.Rows.All(r=>r.Status==PortalScanStatus.NotChecked),"Precancel changed unchecked status");
            using var cancel=new CancellationTokenSource();var result=await scanner.Scan(entries,cancel.Token,row=>{if(row.Status==PortalScanStatus.PublicPage)cancel.Cancel();});
            Assert(!result.Complete&&calls==1&&result.Rows[0].Status==PortalScanStatus.PublicPage&&result.Rows[1].Status==PortalScanStatus.NotChecked,"Gap cancellation changed unchecked status");
        });
        await Check("overall_budget_leaves_remaining_rows_unchecked",async()=>
        {
            int calls=0;var scanner=new PortalScanner(page:async(u,c)=>{calls++;await Task.Delay(Timeout.Infinite,c);throw new Failure("Unreachable");},totalTimeout:TimeSpan.FromMilliseconds(40),gap:TimeSpan.Zero);
            var result=await scanner.Scan(Parse(Url+"\nhttps://two.fixture.example/c/").Entries,CancellationToken.None);
            Assert(!result.Complete&&calls==1&&result.Rows[1].Status==PortalScanStatus.NotChecked,"Overall budget ignored");
        });
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(report))!);
        File.WriteAllText(report,JsonSerializer.Serialize(new{version="0.9.8",mode="offline synthetic parser and scanner fixtures",success,total=checks.Count,checks},new JsonSerializerOptions{WriteIndented=true}));return success?0:1;
    }
}
