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
        await Check("release_batch_does_not_connect_to_accounts_or_probe_hosts",async()=>{
            int requests=0;var scanner=new PortalScanner(Client(_=>requests++),(url,ct)=>{requests++;throw new InvalidOperationException();},gap:TimeSpan.Zero);
            var run=await scanner.Scan(Parse(Url+"|"+Mac+"\nhttps://two.fixture.example/c/").Entries,CancellationToken.None);
            Assert(requests==0&&run.Rows.All(r=>r.Status==PortalScanStatus.NotChecked),"Batch unexpectedly contacted a source");
        });
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(report))!);
        File.WriteAllText(report,JsonSerializer.Serialize(new{version="0.12.0",mode="offline synthetic parser and scanner fixtures",success,total=checks.Count,checks},new JsonSerializerOptions{WriteIndented=true}));return success?0:1;
    }
}
