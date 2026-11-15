package com.swir.stream;

import android.util.JsonReader;
import android.util.JsonToken;
import android.os.SystemClock;
import org.json.JSONObject;
import java.io.FilterInputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.InputStreamReader;
import java.nio.charset.CodingErrorAction;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.HashSet;
import java.util.List;
import java.util.Set;
import java.util.concurrent.atomic.AtomicBoolean;

/** Streaming private catalog: unknown provider metadata never becomes a DOM or a raw disk file. */
final class PortalChannelReader {
    static final int MAX_CHANNELS=30000,MAX_RETAINED_BYTES=8_000_000,MAX_TOKEN_BYTES=1_048_576,MAX_DEPTH=64;
    static final class Channel {
        final String id,name,command,xmltvId,groupId;final Boolean useHttpTmpLink,useLoadBalancing;
        Channel(String id,String name,String command,String xmltvId,String groupId){this(id,name,command,xmltvId,groupId,null,null);}
        Channel(String id,String name,String command,String xmltvId,String groupId,Boolean temporary,Boolean balancing){this.id=id;this.name=name;this.command=command;this.xmltvId=xmltvId;this.groupId=groupId;useHttpTmpLink=temporary;useLoadBalancing=balancing;}
    }
    static final class Catalog {
        final List<Channel> channels=new ArrayList<>();
        final String url;final long downloadedAt,modifiedAt;
        long rawBytes,rowsRead;int retainedBytes;boolean truncated;
        Catalog(String url,long downloaded,long modified){this.url=url;downloadedAt=downloaded;modifiedAt=modified;}
    }
    private final JsonReader reader;private final Catalog result;private final AtomicBoolean cancelled;
    private final Set<String> retainedIds=new HashSet<>();private boolean dataSeen,jsSeen;
    private PortalChannelReader(JsonReader reader,Catalog result,AtomicBoolean cancelled){this.reader=reader;this.result=result;this.cancelled=cancelled;}
    static Catalog read(InputStream input,String url,long downloaded,long modified,AtomicBoolean cancelled,long deadline)throws IOException{
        return read(input,url,downloaded,modified,cancelled,deadline,ProviderHttp.PORTAL_CHANNEL_BYTES);
    }
    static Catalog read(InputStream input,String url,long downloaded,long modified,AtomicBoolean cancelled,long deadline,int byteLimit)throws IOException{
        Catalog result=new Catalog(url,downloaded,modified);
        BoundedJsonInput bounded=new BoundedJsonInput(input,byteLimit,cancelled,deadline);
        try(JsonReader reader=new JsonReader(new InputStreamReader(bounded,StandardCharsets.UTF_8.newDecoder().onMalformedInput(CodingErrorAction.REPORT).onUnmappableCharacter(CodingErrorAction.REPORT)))){
            reader.setLenient(false);PortalChannelReader parser=new PortalChannelReader(reader,result,cancelled);parser.root();
            if(reader.peek()!=JsonToken.END_DOCUMENT)throw invalid();PublicHttp.checkCancelled(cancelled);
            if(!parser.dataSeen)throw invalid();result.rawBytes=bounded.count;return result;
        }catch(IOException error){PublicHttp.checkCancelled(cancelled);String message=error.getMessage();if(message!=null&&(message.startsWith("Lista portalu ")||message.startsWith("Portal odmówił ")||message.startsWith("Przekroczono ")))throw error;throw invalid();}
        catch(Exception error){PublicHttp.checkCancelled(cancelled);throw invalid();}
    }
    private void root()throws Exception{
        if(reader.peek()!=JsonToken.BEGIN_OBJECT)throw invalid();reader.beginObject();JSONObject envelope=new JSONObject();
        while(reader.hasNext()){
            PublicHttp.checkCancelled(cancelled);String name=reader.nextName();
            if(name.equals("js")){if(jsSeen)throw invalid();jsSeen=true;if(reader.peek()==JsonToken.BEGIN_ARRAY)array();else if(reader.peek()==JsonToken.BEGIN_OBJECT)wrapper();else throw invalid();}
            else if(isStatus(name))status(envelope,name);else reader.skipValue();
        }reader.endObject();validateEnvelope(envelope);
    }
    private void wrapper()throws Exception{
        reader.beginObject();JSONObject envelope=new JSONObject();
        while(reader.hasNext()){
            PublicHttp.checkCancelled(cancelled);String name=reader.nextName();if(name.equals("data")){if(reader.peek()!=JsonToken.BEGIN_ARRAY)throw invalid();array();}else if(isStatus(name))status(envelope,name);else reader.skipValue();
        }reader.endObject();validateEnvelope(envelope);
    }
    private void array()throws Exception{
        if(dataSeen)throw invalid();dataSeen=true;reader.beginArray();
        while(reader.hasNext()){
            PublicHttp.checkCancelled(cancelled);result.rowsRead++;
            if(reader.peek()!=JsonToken.BEGIN_OBJECT)throw invalid();reader.beginObject();String id="",name="",command="",xmltv="",group="";Boolean temporary=null,balancing=null;Set<String> fields=new HashSet<>();
            while(reader.hasNext()){
                String field=reader.nextName();
                if(field.equals("id")||field.equals("name")||field.equals("cmd")||field.equals("xmltv_id")||field.equals("tv_genre_id")){
                    if(!fields.add(field))throw invalid();String value=value(field.equals("cmd")?8192:field.equals("name")?2048:field.equals("xmltv_id")?1024:512);
                    switch(field){case "id":id=value;break;case "name":name=value;break;case "cmd":command=value;break;case "xmltv_id":xmltv=value;break;default:group=value;}
                }else if(field.equals("use_http_tmp_link")||field.equals("use_load_balancing")){if(!fields.add(field))throw invalid();Boolean flag=flag();if(field.equals("use_http_tmp_link"))temporary=flag;else balancing=flag;}else reader.skipValue();
            }reader.endObject();
            if(id.isEmpty()||command.isEmpty()||retainedIds.contains(id))continue;
            int bytes=utf8(id)+utf8(name)+utf8(command)+utf8(xmltv)+utf8(group);
            if(result.truncated||result.channels.size()>=MAX_CHANNELS||bytes>MAX_RETAINED_BYTES-result.retainedBytes){result.truncated=true;continue;}
            retainedIds.add(id);result.channels.add(new Channel(id,name,command,xmltv,group,temporary,balancing));result.retainedBytes+=bytes;
        }reader.endArray();
    }
    private String value(int limit)throws IOException{
        JsonToken token=reader.peek();if(token==JsonToken.NULL){reader.nextNull();return "";}
        if(token!=JsonToken.STRING&&token!=JsonToken.NUMBER)throw invalid();
        String value=reader.nextString();if(utf8(value)>limit)throw new IOException("Lista portalu zawiera zbyt długie pole kanału. Poprzednia lista pozostała zapisana.");return value;
    }
    private Boolean flag()throws IOException{JsonToken token=reader.peek();if(token==JsonToken.BOOLEAN)return reader.nextBoolean();if(token==JsonToken.STRING||token==JsonToken.NUMBER)return PortalDiscovery.channelFlag(reader.nextString());reader.skipValue();return null;}
    private static int utf8(String text){return text.getBytes(StandardCharsets.UTF_8).length;}
    private static boolean isStatus(String name){return name.equals("error")||name.equals("errors")||name.equals("status")||name.equals("blocked")||name.equals("auth")||name.equals("msg")||name.equals("message")||name.equals("block_msg");}
    private void status(JSONObject envelope,String name)throws Exception{
        if(envelope.has(name))throw invalid();JsonToken type=reader.peek();Object value;
        if((name.equals("status")||name.equals("blocked")||name.equals("auth"))&&(type==JsonToken.BEGIN_ARRAY||type==JsonToken.BEGIN_OBJECT))throw invalid();
        if(type==JsonToken.NULL){reader.nextNull();value=JSONObject.NULL;}
        else if(type==JsonToken.BOOLEAN)value=reader.nextBoolean();
        else if(type==JsonToken.STRING||type==JsonToken.NUMBER){String text=reader.nextString();if(utf8(text)>2048)throw invalid();value=type==JsonToken.NUMBER&&text.equals("0")?Integer.valueOf(0):text;}
        else if(type==JsonToken.BEGIN_ARRAY){reader.beginArray();boolean nonempty=reader.hasNext();while(reader.hasNext()){if(reader.peek()==JsonToken.STRING){String text=reader.nextString();if(utf8(text)>2048)throw invalid();validateEnvelope(new JSONObject().put(name,text));}else reader.skipValue();}reader.endArray();value=nonempty?Boolean.TRUE:JSONObject.NULL;}
        else{reader.skipValue();value=Boolean.TRUE;}
        envelope.put(name,value);
    }
    private static void validateEnvelope(JSONObject envelope)throws IOException{
        if(PortalDiscovery.denial(envelope)||PortalDiscovery.authEnvelope(envelope)||PortalDiscovery.hasError(envelope))throw new IOException("Portal odmówił udostępnienia listy kanałów. Poprzednia lista pozostała zapisana.");
    }
    private static IOException invalid(){return new IOException("Lista portalu ma nieprawidłowy format. Poprzednia lista pozostała zapisana.");}

    /** Limits raw UTF-8 tokens before JsonReader can allocate a selected string. */
    static final class BoundedJsonInput extends FilterInputStream {
        private final int limit;private final AtomicBoolean cancelled;private final long deadline;
        long count;private int depth,tokenBytes,unicodeDigits;private boolean string,escaped;
        BoundedJsonInput(InputStream input,int limit,AtomicBoolean cancelled,long deadline){super(input);this.limit=limit;this.cancelled=cancelled;this.deadline=deadline;}
        private void check()throws IOException{PublicHttp.checkCancelled(cancelled);if(SystemClock.elapsedRealtime()>deadline)throw new IOException("Przekroczono czas pobierania listy portalu.");}
        private void scan(byte[] bytes,int offset,int length)throws IOException{
            check();if(length>limit-count)throw new IOException("Lista portalu przekracza limit 64 MB.");count+=length;
            for(int i=offset;i<offset+length;i++){
                int value=bytes[i]&255;
                if(string){if(++tokenBytes>MAX_TOKEN_BYTES)throw invalid();if(unicodeDigits>0){if(!((value>='0'&&value<='9')||(value>='a'&&value<='f')||(value>='A'&&value<='F')))throw invalid();unicodeDigits--;}else if(escaped){escaped=false;if(value=='u')unicodeDigits=4;else if("\"\\/bfnrt".indexOf(value)<0)throw invalid();}else if(value=='\\')escaped=true;else if(value=='"'){string=false;tokenBytes=0;}else if(value<32)throw invalid();}
                else if(value=='"'){string=true;tokenBytes=0;}
                else if(value=='{'||value=='['){if(++depth>MAX_DEPTH)throw invalid();tokenBytes=0;}
                else if(value=='}'||value==']'){if(--depth<0)throw invalid();tokenBytes=0;}
                else if(value==','||value==':'||value==' '||value=='\r'||value=='\n'||value=='\t')tokenBytes=0;
                else if(++tokenBytes>MAX_TOKEN_BYTES)throw invalid();
            }
        }
        @Override public int read(byte[] bytes,int offset,int length)throws IOException{check();int count=in.read(bytes,offset,length);if(count>0)scan(bytes,offset,count);return count;}
        @Override public int read()throws IOException{byte[] one=new byte[1];int read=read(one,0,1);return read<0?-1:one[0]&255;}
    }
}
