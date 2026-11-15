using System.Buffers;
using System.Text;
using System.Text.Json;

namespace SwirStreamAI;

public sealed record PortalCatalogReadInfo(long RawBytes,int RowsRead,int RetainedCount,int RetainedBytes,bool Truncated,int? UniqueSeen,int UniqueSeenAtLeast);
internal sealed record PortalChannelBatch(List<ProviderChannel> Channels,bool Truncated,long RawBytes,int RowsRead,int RetainedBytes)
{internal PortalCatalogReadInfo Info=>new(RawBytes,RowsRead,Channels.Count,RetainedBytes,Truncated,Truncated?null:Channels.Count,Channels.Count+(Truncated?1:0));}

// Only get_all_channels uses this reader. It validates the complete response,
// but retains only the fields needed for playback, even after the list fills.
internal static class PortalChannelReader
{
    internal const int RawByteLimit=64_000_000,RetainedByteLimit=8_000_000,ChannelLimit=30_000,TokenByteLimit=1_048_576;
    static readonly string[] PropertyNames=["js","data","id","name","cmd","xmltv_id","use_http_tmp_link","use_load_balancing","error","errors","status","blocked","auth","msg","message","block_msg"];
    enum Context { Root, Js, Data, Row, Ignore, EmptyControl }
    sealed class Frame(Context kind,string control="")
    {
        internal Context Kind=kind;internal string Property="",Control=control;
        internal HashSet<string>? Selected;
    }
    sealed class State
    {
        internal readonly Stack<Frame> Stack=[];
        internal readonly List<ProviderChannel> Channels=[];
        internal readonly HashSet<string> Ids=new(StringComparer.Ordinal);
        internal bool RootSeen,RootEnded,JsSeen,DataSeen,Truncated;
        internal int RowsRead,RetainedBytes;
        internal string Id="",Name="",Command="",Xmltv="";
        internal bool? UseHttpTmpLink,UseLoadBalancing;
    }
    internal static async Task<PortalChannelBatch> ReadAsync(Stream input,CancellationToken ct)
    {
        byte[] buffer=ArrayPool<byte>.Shared.Rent(32_768);char[] chars=ArrayPool<char>.Shared.Rent(32_768);int count=0;long total=0;bool bomChecked=false;
        var utf8=new UTF8Encoding(false,true).GetDecoder();
        var parser=new State();var jsonState=new JsonReaderState(new JsonReaderOptions{MaxDepth=64});
        try
        {
            while(true)
            {
                ct.ThrowIfCancellationRequested();
                int available=(int)Math.Min(buffer.Length-count,RawByteLimit-total+1);
                int read=await input.ReadAsync(buffer.AsMemory(count,available),ct).ConfigureAwait(false);
                total+=read;if(total>RawByteLimit)throw new InvalidDataException("Katalog portalu przekracza limit 64 MB. Zachowano poprzednią listę.");
                ValidateUtf8(utf8,buffer,count,read,chars,read==0);
                count+=read;bool final=read==0;
                if(!bomChecked)
                {
                    if(count<3&&!final)continue;
                    if(count>=3&&buffer[0]==0xef&&buffer[1]==0xbb&&buffer[2]==0xbf){Buffer.BlockCopy(buffer,3,buffer,0,count-3);count-=3;}
                    bomChecked=true;
                }
                int consumed=Consume(buffer,count,final,ref jsonState,parser,ct);
                count-=consumed;if(count>0)Buffer.BlockCopy(buffer,consumed,buffer,0,count);
                if(final)
                {
                    if(count!=0||!parser.RootEnded||!parser.JsSeen||!parser.DataSeen||parser.Stack.Count!=0||parser.Channels.Count==0)throw new InvalidDataException("Portal nie zwrócił poprawnej, niepustej listy kanałów.");
                    return new(parser.Channels,parser.Truncated,total,parser.RowsRead,parser.RetainedBytes);
                }
                if(count==buffer.Length)
                {
                    if(buffer.Length>=TokenByteLimit)throw new InvalidDataException("Pojedyncze pole katalogu portalu jest zbyt duże.");
                    var larger=ArrayPool<byte>.Shared.Rent(Math.Min(TokenByteLimit,buffer.Length*2));
                    Buffer.BlockCopy(buffer,0,larger,0,count);ArrayPool<byte>.Shared.Return(buffer,clearArray:true);buffer=larger;
                }
            }
        }
        catch(JsonException){throw new InvalidDataException("Portal zwrócił nieprawidłowy JSON katalogu.");}
        catch(DecoderFallbackException){throw new InvalidDataException("Portal zwrócił nieprawidłowy tekst UTF-8 katalogu.");}
        finally{ArrayPool<byte>.Shared.Return(buffer,clearArray:true);ArrayPool<char>.Shared.Return(chars,clearArray:true);}
    }
    static void ValidateUtf8(Decoder utf8,byte[] buffer,int offset,int count,char[] chars,bool final)
    {
        bool done;
        do{utf8.Convert(buffer,offset,count,chars,0,chars.Length,final,out int consumed,out _,out done);offset+=consumed;count-=consumed;}while(!done);
    }
    static int Consume(byte[] buffer,int count,bool final,ref JsonReaderState jsonState,State state,CancellationToken ct)
    {
        var reader=new Utf8JsonReader(buffer.AsSpan(0,count),final,jsonState);int tokens=0;
        while(reader.Read())
        {
            if((++tokens&1023)==0)ct.ThrowIfCancellationRequested();
            if(reader.HasValueSequence||reader.ValueSpan.Length>TokenByteLimit)throw new InvalidDataException("Pojedyncze pole katalogu portalu jest zbyt duże.");
            if(!state.RootSeen)
            {
                if(reader.TokenType!=JsonTokenType.StartObject)throw new InvalidDataException("Portal nie zwrócił obiektu katalogu.");
                state.RootSeen=true;state.Stack.Push(new(Context.Root));continue;
            }
            if(state.Stack.Count==0)throw new InvalidDataException("Portal zwrócił dodatkowe dane po katalogu.");
            var frame=state.Stack.Peek();
            if(reader.TokenType==JsonTokenType.PropertyName)
            {
                frame.Property=Property(ref reader);
                if(frame.Kind==Context.Row&&frame.Property is "id" or "name" or "cmd" or "xmltv_id" or "use_http_tmp_link" or "use_load_balancing")
                {
                    frame.Selected??=[];if(!frame.Selected.Add(frame.Property))throw new InvalidDataException("Kanał zawiera powtórzone pole.");
                }
                continue;
            }
            if(reader.TokenType is JsonTokenType.EndObject or JsonTokenType.EndArray)
            {
                state.Stack.Pop();
                if(frame.Kind==Context.Row)FinishRow(state);
                if(frame.Kind==Context.Root)state.RootEnded=true;
                continue;
            }
            if(frame.Kind==Context.EmptyControl)throw new InvalidDataException("Portal zwrócił błąd katalogu kanałów.");
            bool objectStart=reader.TokenType==JsonTokenType.StartObject,arrayStart=reader.TokenType==JsonTokenType.StartArray;
            string property=frame.Property;frame.Property="";
            if(frame.Kind==Context.Root&&property=="js")
            {
                if(state.JsSeen||!objectStart)throw new InvalidDataException("Nieprawidłowa odpowiedź katalogu portalu.");
                state.JsSeen=true;state.Stack.Push(new(Context.Js));continue;
            }
            if(frame.Kind==Context.Js&&property=="data")
            {
                if(state.DataSeen||!arrayStart)throw new InvalidDataException("Portal nie zwrócił tablicy kanałów.");
                state.DataSeen=true;state.Stack.Push(new(Context.Data));continue;
            }
            if(frame.Kind is Context.Root or Context.Js&&property is "error" or "errors" or "status" or "blocked" or "auth" or "msg" or "message" or "block_msg")
            {
                if(objectStart||arrayStart)
                {
                    if(property is "error" or "errors")
                    {
                        if(!arrayStart)throw new InvalidDataException("Portal zwrócił błąd katalogu kanałów.");
                        state.Stack.Push(new(Context.EmptyControl,property));
                    }
                    else throw new UnauthorizedAccessException("Portal odrzucił dostęp do katalogu.");
                }
                else CheckControl(property,ref reader);
                continue;
            }
            if(frame.Kind==Context.Data)
            {
                if(!objectStart)throw new InvalidDataException("Lista portalu zawiera nieprawidłowy wpis kanału.");
                state.Id=state.Name=state.Command=state.Xmltv="";state.UseHttpTmpLink=state.UseLoadBalancing=null;state.Stack.Push(new(Context.Row));continue;
            }
            if(frame.Kind==Context.Row&&property is "use_http_tmp_link" or "use_load_balancing")
            {
                bool? flag=LinkFlag(ref reader);
                if(property=="use_http_tmp_link")state.UseHttpTmpLink=flag;else state.UseLoadBalancing=flag;
                if(objectStart||arrayStart)state.Stack.Push(new(Context.Ignore));
                continue;
            }
            if(frame.Kind==Context.Row&&property is "id" or "name" or "cmd" or "xmltv_id")
            {
                if(objectStart||arrayStart)throw new InvalidDataException("Kanał zawiera nieprawidłowy typ pola.");
                string value=reader.TokenType switch{JsonTokenType.String=>reader.GetString()!,JsonTokenType.Number=>Encoding.UTF8.GetString(reader.ValueSpan),_=>""};
                int max=property switch{"id"=>512,"name"=>2048,"cmd"=>8192,_=>1024};
                if(Encoding.UTF8.GetByteCount(value)>max)throw new InvalidDataException("Pole kanału przekracza obsługiwany rozmiar.");
                switch(property){case "id":state.Id=value;break;case "name":state.Name=value;break;case "cmd":state.Command=value;break;default:state.Xmltv=value;break;}
                continue;
            }
            if(objectStart||arrayStart)state.Stack.Push(new(Context.Ignore));
        }
        jsonState=reader.CurrentState;return checked((int)reader.BytesConsumed);
    }
    static string Property(ref Utf8JsonReader reader)
    {
        foreach(string name in PropertyNames)if(reader.ValueTextEquals(name))return name;
        return "";
    }
    static void FinishRow(State state)
    {
        state.RowsRead++;
        if(string.IsNullOrWhiteSpace(state.Id)||string.IsNullOrWhiteSpace(state.Name)||string.IsNullOrWhiteSpace(state.Command)||state.Ids.Contains(state.Id))return;
        int bytes=Encoding.UTF8.GetByteCount(state.Id)+Encoding.UTF8.GetByteCount(state.Name)+Encoding.UTF8.GetByteCount(state.Command)+Encoding.UTF8.GetByteCount(state.Xmltv);
        if(state.Truncated||state.Channels.Count==ChannelLimit||state.RetainedBytes+bytes>RetainedByteLimit){state.Truncated=true;return;}
        state.Ids.Add(state.Id);state.RetainedBytes+=bytes;
        state.Channels.Add(new(state.Id,new(state.Name,"","TV",Source:"Moje źródło",ChannelId:state.Xmltv),state.Command){UseHttpTmpLink=state.UseHttpTmpLink,UseLoadBalancing=state.UseLoadBalancing});
    }
    static bool? LinkFlag(ref Utf8JsonReader reader)
    {
        if(reader.TokenType==JsonTokenType.False)return false;
        if(reader.TokenType==JsonTokenType.True)return true;
        // No coercion of null, objects, fractions, exponents, whitespace or
        // Unicode lookalikes. Unknown metadata must use the temporary-link flow.
        if(reader.TokenType==JsonTokenType.Number)return reader.ValueSpan.SequenceEqual("0"u8)?false:reader.ValueSpan.SequenceEqual("1"u8)?true:null;
        if(reader.TokenType!=JsonTokenType.String||reader.ValueSpan.Length>30)return null;
        string value=reader.GetString()!;
        return value is "0" or "false"?false:value is "1" or "true"?true:null;
    }
    static void CheckControl(string property,ref Utf8JsonReader reader)
    {
        string value=reader.TokenType==JsonTokenType.String?reader.GetString()!:reader.TokenType==JsonTokenType.Number?Encoding.UTF8.GetString(reader.ValueSpan):"";
        string text=value.Trim(' ','\t','\r','\n').ToLowerInvariant();
        bool inactive=reader.TokenType is JsonTokenType.Null or JsonTokenType.False||reader.TokenType==JsonTokenType.Number&&value=="0"||reader.TokenType==JsonTokenType.String&&text is "" or "0" or "false" or "ok" or "success";
        bool denied=PortalProtocol.CatalogAccessDeniedMessage(value);
        if(property is "msg" or "message" or "block_msg"){if(denied)throw new UnauthorizedAccessException("Portal odrzucił dostęp do katalogu.");return;}
        if(property=="auth")
        {
            if(reader.TokenType==JsonTokenType.True||reader.TokenType==JsonTokenType.Number&&value=="1"||reader.TokenType==JsonTokenType.String&&text is "1" or "true")return;
            throw new UnauthorizedAccessException("Portal odrzucił dostęp do katalogu.");
        }
        if(!inactive)
        {
            if(property is "status" or "blocked"||denied)throw new UnauthorizedAccessException("Portal odrzucił dostęp do katalogu.");
            throw new InvalidDataException("Portal zwrócił błąd katalogu kanałów.");
        }
    }
}
