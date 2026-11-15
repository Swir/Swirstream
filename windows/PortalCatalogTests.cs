using System.Text;
using System.Text.Json;

namespace SwirStreamAI;

public static class PortalCatalogTests
{
    const string ValidRow="{\"id\":1,\"name\":\"Fixture\",\"cmd\":\"command\"}";
    sealed class Failure(string message):Exception(message);
    record Result(string Name,bool Passed,string Detail);
    // Generates each row on demand; large-fixture tests never construct a large document.
    sealed class PartsStream(IEnumerable<byte[]> parts,int chunk=32768,Action<long>? progress=null):Stream
    {
        readonly IEnumerator<byte[]> partsIterator=parts.GetEnumerator();byte[] current=[];int offset;long consumed;
        public override bool CanRead=>true;public override bool CanSeek=>false;public override bool CanWrite=>false;
        public override long Length=>throw new NotSupportedException();public override long Position{get=>consumed;set=>throw new NotSupportedException();}
        public override int Read(byte[] buffer,int start,int count)=>Read(buffer.AsSpan(start,count));
        public override int Read(Span<byte> buffer)
        {
            while(offset==current.Length){if(!partsIterator.MoveNext())return 0;current=partsIterator.Current;offset=0;}
            int size=Math.Min(Math.Min(chunk,buffer.Length),current.Length-offset);current.AsSpan(offset,size).CopyTo(buffer);offset+=size;consumed+=size;progress?.Invoke(consumed);return size;
        }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken ct=default){ct.ThrowIfCancellationRequested();return ValueTask.FromResult(Read(buffer.Span));}
        protected override void Dispose(bool disposing){if(disposing)partsIterator.Dispose();base.Dispose(disposing);}
        public override void Flush()=>throw new NotSupportedException();public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException();public override void SetLength(long length)=>throw new NotSupportedException();public override void Write(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
    }
    static void Assert(bool ok,string text){if(!ok)throw new Failure(text);}
    static byte[] Bytes(string text)=>Encoding.UTF8.GetBytes(text);
    static IEnumerable<byte[]> Catalog(int count,int padding=0,Func<int,string>? id=null,int commandPadding=0,string tail="")
    {
        yield return Bytes("{\"ignored\":{\"data\":[]},\"js\":{\"data\":[");
        string extra=new('x',padding),command="ffmpeg http://localhost/ch/"+new string('c',commandPadding);
        for(int i=0;i<count;i++)yield return Bytes((i==0?"":",")+JsonSerializer.Serialize(new{id=id?.Invoke(i)??i.ToString(),name="Fixture "+i,cmd=command,xmltv_id="fixture",ignored=new{extra,nested=new[]{1,2,3}}}));
        yield return Bytes("]}"+tail+"}");
    }
    static async Task<T> Reject<T>(Func<Task> run)where T:Exception
    {try{await run();}catch(T ex){return ex;}throw new Failure("Expected "+typeof(T).Name);}
    public static int Run(string report)=>RunAsync(report).GetAwaiter().GetResult();
    static async Task<int> RunAsync(string report)
    {
        var results=new List<Result>();
        async Task Check(string name,Func<Task> run){try{await run().WaitAsync(TimeSpan.FromSeconds(40));results.Add(new(name,true,"Passed"));}catch(Exception ex){results.Add(new(name,false,ex is Failure?ex.Message:ex.GetType().Name));}}
        await Check("link_flags_preserve_only_explicit_boolean_evidence",async()=>
        {
            foreach(var sample in new[]{("false",(bool?)false),("0",false),("\"0\"",false),("\"false\"",false),("true",true),("1",true),("\"1\"",true),("\"true\"",true),("null",null),("0.0",null),("0e0",null),("-0",null),("2",null),("\"\"",null),("\" false \"",null),("\"falſe\"",null),("\"FALSE\"",null),("{}",null),("[]",null)})
            {
                string row=ValidRow[..^1]+",\"use_http_tmp_link\":"+sample.Item1+",\"use_load_balancing\":"+sample.Item1+"}";
                using var stream=new PartsStream([Bytes("{\"js\":{\"data\":["+row+","+ValidRow.Replace("\"id\":1","\"id\":2")+"]}}")],1);
                var batch=await PortalChannelReader.ReadAsync(stream,CancellationToken.None);
                Assert(batch.Channels[0].UseHttpTmpLink==sample.Item2&&batch.Channels[0].UseLoadBalancing==sample.Item2,"Flag coerced or lost");
                Assert(batch.Channels[1].UseHttpTmpLink==null&&batch.Channels[1].UseLoadBalancing==null,"Flags leaked into next channel");
            }
        });
        await Check("duplicate_link_flag_is_not_accepted_as_static_evidence",async()=>
        {
            foreach(string field in new[]{"use_http_tmp_link","use_load_balancing"})
            {
                string row=ValidRow[..^1]+",\""+field+"\":true,\""+field+"\":false}";
                using var stream=new MemoryStream(Bytes("{\"js\":{\"data\":["+row+"]}}"));
                await Reject<InvalidDataException>(()=>PortalChannelReader.ReadAsync(stream,CancellationToken.None));
            }
        });
        await Check("large_catalog_streams_beyond_old_24MB_without_retaining_unknown_fields",async()=>
        {
            using var stream=new PartsStream(Catalog(25000,1250));var batch=await PortalChannelReader.ReadAsync(stream,CancellationToken.None);
            Assert(batch.RawBytes>24_000_000&&batch.RawBytes<64_000_000&&batch.Channels.Count==25000&&!batch.Truncated,"Large bounded catalogue not read fully");
            Assert(batch.RetainedBytes<2_000_000&&batch.Info.UniqueSeen==25000&&batch.Info.RowsRead==25000,"Unknown response fields retained or counts invented");
        });
        await Check("unique_channel_limit_is_explicit_and_remainder_is_consumed",async()=>
        {
            using var stream=new PartsStream(Catalog(30003));var batch=await PortalChannelReader.ReadAsync(stream,CancellationToken.None);
            Assert(batch.Channels.Count==30000&&batch.Truncated&&batch.RowsRead==30003&&batch.Info.UniqueSeen==null&&batch.Info.UniqueSeenAtLeast==30001,"Truncation or exact-count metadata is wrong");
        });
        await Check("duplicate_rows_do_not_spend_unique_channel_budget",async()=>
        {
            using var stream=new PartsStream(Catalog(30005,id:i=>i<30004?"duplicate":"last"));var batch=await PortalChannelReader.ReadAsync(stream,CancellationToken.None);
            Assert(batch.Channels.Count==2&&!batch.Truncated&&batch.Info.UniqueSeen==2&&batch.RowsRead==30005,"Duplicates exhausted retained channel budget");
        });
        await Check("retained_byte_budget_stops_storage_but_not_validation",async()=>
        {
            using var stream=new PartsStream(Catalog(4500,commandPadding:2000));var batch=await PortalChannelReader.ReadAsync(stream,CancellationToken.None);
            Assert(batch.Truncated&&batch.Channels.Count<4500&&batch.RetainedBytes<=PortalChannelReader.RetainedByteLimit&&batch.RowsRead==4500,"Retained budget not bounded independently of raw bytes");
        });
        await Check("denial_after_channel_cap_fails_entire_catalog",async()=>
        {
            using var stream=new PartsStream(Catalog(30001,tail:",\"blocked\":1"));await Reject<UnauthorizedAccessException>(()=>PortalChannelReader.ReadAsync(stream,CancellationToken.None));
        });
        await Check("malformed_tail_after_channel_cap_fails_entire_catalog",async()=>
        {
            using var stream=new PartsStream(Catalog(30001).Append(Bytes("{bad")));await Reject<InvalidDataException>(()=>PortalChannelReader.ReadAsync(stream,CancellationToken.None));
        });
        await Check("status_and_errors_before_or_after_data_are_not_hidden",async()=>
        {
            const string row="{\"id\":1,\"name\":\"Fixture\",\"cmd\":\"command\"}";
            foreach(string body in new[]{"{\"status\":1,\"js\":{\"data\":["+row+"]}}","{\"js\":{\"blocked\":true,\"data\":["+row+"]}}","{\"js\":{\"data\":["+row+"],\"auth\":0}}","{\"js\":{\"data\":["+row+"],\"status\":\"falſe\"}}","{\"js\":{\"data\":["+row+"]},\"error\":\"Account blocked\"}"})
            {using var stream=new MemoryStream(Bytes(body));await Reject<UnauthorizedAccessException>(()=>PortalChannelReader.ReadAsync(stream,CancellationToken.None));}
            using var failed=new MemoryStream(Bytes("{\"js\":{\"data\":["+row+"],\"error\":\"Unknown action\"}}"));await Reject<InvalidDataException>(()=>PortalChannelReader.ReadAsync(failed,CancellationToken.None));
        });
        await Check("json_utf8_escape_and_bom_can_cross_every_read_boundary",async()=>
        {
            string body="\uFEFF{\"js\":{\"data\":[{\"id\":\"a\",\"name\":\"Żółć \\u0142 \\uD83D\\uDE03\",\"cmd\":\"ffmpeg https://fixture.example/ż\",\"ignored\":[{\"name\":\"unused\"}]}]}}";
            using var stream=new PartsStream([Bytes(body)],1);var batch=await PortalChannelReader.ReadAsync(stream,CancellationToken.None);
            Assert(batch.Channels.Single().Station.Name=="Żółć ł 😃","Split multibyte or escaped string damaged");
        });
        await Check("catalog_denial_messages_share_protocol_normalization",async()=>
        {
            const string row="{\"id\":1,\"name\":\"Fixture\",\"cmd\":\"command\"}";
            foreach(string field in new[]{"msg","message","block_msg"})foreach(string text in new[]{"Account blocked.","Unauthorized request.","Authorization failed. 75"})foreach(bool after in new[]{true,false})
            {
                string flag=JsonSerializer.Serialize(field)+":"+JsonSerializer.Serialize(text),data="\"data\":["+row+"]";
                using var stream=new MemoryStream(Bytes("{\"js\":{"+(after?data+","+flag:flag+","+data)+"}}"));
                await Reject<UnauthorizedAccessException>(()=>PortalChannelReader.ReadAsync(stream,CancellationToken.None));
            }
            using(var stream=new MemoryStream(Bytes("{\"js\":{\"data\":["+row+"],\"error\":{}}}")))await Reject<InvalidDataException>(()=>PortalChannelReader.ReadAsync(stream,CancellationToken.None));
            using(var stream=new MemoryStream(Bytes("{\"js\":{\"data\":["+row+"],\"errors\":[]}}")))Assert((await PortalChannelReader.ReadAsync(stream,CancellationToken.None)).Channels.Count==1,"Empty errors array rejected");
        });
        await Check("invalid_utf8_in_ignored_data_and_incomplete_tail_are_rejected",async()=>
        {
            string suffix="\",\"js\":{\"data\":["+ValidRow+"]}}";
            foreach(byte[] body in new[]{Bytes("{\"ignored\":\"").Concat(new byte[]{0xff}).Concat(Bytes(suffix)).ToArray(),Bytes("{\"ignored\":\"").Concat(new byte[]{0xc4}).Concat(Bytes(suffix)).ToArray()})
            {using var stream=new PartsStream([body],1);await Reject<InvalidDataException>(()=>PortalChannelReader.ReadAsync(stream,CancellationToken.None));}
            using var valid=new PartsStream([Bytes("{\"ignored\":\"ą"+suffix)],1);Assert((await PortalChannelReader.ReadAsync(valid,CancellationToken.None)).Channels.Count==1,"Valid UTF8 counterpart failed");
        });
        await Check("raw_byte_limit_counts_trailing_bytes_without_unbounded_buffer",async()=>
        {
            byte[] whitespace=Bytes(new string(' ',16384));var parts=Catalog(1).Concat(Enumerable.Repeat(whitespace,4000));
            using var stream=new PartsStream(parts);await Reject<InvalidDataException>(()=>PortalChannelReader.ReadAsync(stream,CancellationToken.None));
            Assert(stream.Position<=PortalChannelReader.RawByteLimit+32768,"Reader exceeded raw byte budget by more than one chunk");
        });
        await Check("oversized_unknown_token_and_deep_nesting_are_bounded",async()=>
        {
            string suffix=",\"js\":{\"data\":["+ValidRow+"]}}";
            using(var stream=new PartsStream([Bytes("{\"extra\":\""+new string('x',PortalChannelReader.TokenByteLimit+1)+"\""+suffix)]))await Reject<InvalidDataException>(()=>PortalChannelReader.ReadAsync(stream,CancellationToken.None));
            using(var stream=new MemoryStream(Bytes("{\"extra\":"+new string('[',70)+"0"+new string(']',70)+suffix)))await Reject<InvalidDataException>(()=>PortalChannelReader.ReadAsync(stream,CancellationToken.None));
            using(var stream=new MemoryStream(Bytes("{\"extra\":"+new string('[',60)+"0"+new string(']',60)+suffix)))Assert((await PortalChannelReader.ReadAsync(stream,CancellationToken.None)).Channels.Count==1,"Valid nesting counterpart failed");
        });
        await Check("cancellation_during_large_response_does_not_return_partial_catalog",async()=>
        {
            using var cancel=new CancellationTokenSource();using var stream=new PartsStream(Catalog(5000,1000),progress:bytes=>{if(bytes>100000)cancel.Cancel();});
            await Reject<OperationCanceledException>(()=>PortalChannelReader.ReadAsync(stream,cancel.Token));
        });
        await Check("wrong_shape_empty_list_duplicate_envelopes_and_long_selected_fields_fail",async()=>
        {
            foreach(string body in new[]{"null","[]","{\"js\":[]}","{\"js\":{\"data\":[]}}","{\"ignored\":{\"js\":{\"data\":[]}}}","{\"js\":{\"data\":[]},\"js\":{\"data\":[]}}","{\"js\":{\"data\":[{\"id\":\"x\",\"name\":\"x\",\"cmd\":\""+new string('x',8193)+"\"}]}}"})
            {using var stream=new MemoryStream(Bytes(body));await Reject<InvalidDataException>(()=>PortalChannelReader.ReadAsync(stream,CancellationToken.None));}
        });
        await Check("production_refresh_exposes_truncation_and_preserves_previous_catalog_on_denial",async()=>
        {
            using var assembled=new MemoryStream();foreach(var part in Catalog(30001))assembled.Write(part);byte[] catalog=assembled.ToArray();bool denied=false;
            var client=new PrivateProviderClient((request,ct)=>Task.FromResult(new ProviderDocument(request.Url.Contains("action=get_genres")?Bytes("{\"js\":[{\"id\":1,\"title\":\"Fixture\"}]}"):denied?Bytes("{\"js\":{\"data\":[],\"blocked\":1}}"):catalog,request.Url)));
            string folder=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(report))!,"catalog-state-"+Guid.NewGuid().ToString("N"));
            var source=new ProviderSource(Guid.NewGuid().ToString("N"),"Offline large portal",PrivateSourceKind.Portal,"https://fixture.example/c/",Mac:"02:00:00:00:00:97");
            var store=new PrivateSourceStore(folder,client);var good=await store.Connect(source,CancellationToken.None);
            Assert(good.Catalog is {Channels.Count:30000}&&good.Catalog.Notice.Contains("Lista ograniczona do pierwszych 30000"),"Saved catalog hid truncation");
            denied=true;var failed=await store.Refresh(source.Id,CancellationToken.None);
            Assert(failed.Catalog?.DownloadedAt==good.Catalog!.DownloadedAt&&failed.Catalog?.Channels.Count==30000&&failed.CatalogFailure is{Stage:"portal_channels",Code:"access_denied"},"Failed refresh replaced previous catalog or stage");
        });
        string destination=Path.GetFullPath(report);Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.WriteAllText(destination,JsonSerializer.Serialize(new{verifiedAtUtc=DateTimeOffset.UtcNow,success=results.All(r=>r.Passed),total=results.Count,mode="offline bounded streaming fixtures",operatorAccountsUsed=0,rawByteLimit=PortalChannelReader.RawByteLimit,retainedByteLimit=PortalChannelReader.RetainedByteLimit,channelLimit=PortalChannelReader.ChannelLimit,checks=results},new JsonSerializerOptions{WriteIndented=true}));
        return results.All(r=>r.Passed)?0:1;
    }
}
