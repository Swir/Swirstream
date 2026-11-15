package com.swir.stream;

import android.os.SystemClock;
import androidx.test.ext.junit.runners.AndroidJUnit4;
import androidx.test.platform.app.InstrumentationRegistry;
import org.junit.Test;
import org.junit.runner.RunWith;
import org.json.JSONObject;
import java.io.*;
import java.net.InetAddress;
import java.nio.charset.StandardCharsets;
import java.util.*;
import java.util.concurrent.atomic.*;
import java.util.function.IntFunction;
import java.util.zip.GZIPOutputStream;
import okhttp3.*;
import okio.*;
import static org.junit.Assert.*;

/** Generated synthetic responses; no operator, DNS or external connection. */
@RunWith(AndroidJUnit4.class)
public final class PortalChannelReaderTest {
    static final String URL="https://fixture.example/portal.php?type=itv&action=get_all_channels",ROW="{\"id\":1,\"name\":\"Żółć TV\",\"cmd\":\"ffmpeg http://localhost/ch/1\",\"status\":1}";
    private interface Throwing{void run()throws Exception;}
    private static void rejects(Throwing action)throws Exception{try{action.run();fail("Expected safe rejection");}catch(IOException expected){assertFalse(expected.getMessage().contains("fixture.example"));assertFalse(expected.getMessage().contains("synthetic-secret"));}}
    private static InputStream bytes(String text){return new ByteArrayInputStream(text.getBytes(StandardCharsets.UTF_8));}
    private static PortalChannelReader.Catalog read(InputStream input)throws Exception{return PortalChannelReader.read(input,URL,1234,567,new AtomicBoolean(),SystemClock.elapsedRealtime()+30000);}
    private static PortalChannelReader.Catalog read(String text)throws Exception{return read(bytes(text));}
    private static String repeat(char ch,int count){char[] chars=new char[count];Arrays.fill(chars,ch);return new String(chars);}
    private static InputStream generated(int count,IntFunction<String> part){return new InputStream(){int index,offset;byte[] current=new byte[0];public int read(byte[] out,int start,int length){while(offset==current.length){if(index==count)return -1;current=part.apply(index++).getBytes(StandardCharsets.UTF_8);offset=0;}int n=Math.min(length,current.length-offset);System.arraycopy(current,offset,out,start,n);offset+=n;return n;}public int read(){byte[] one=new byte[1];return read(one,0,1)<0?-1:one[0]&255;}};}
    private static InputStream rows(int count,String suffix){return generated(count+2,i->i==0?"{\"js\":{\"data\":[":i==count+1?"]"+suffix+"}}":(i==1?"":",")+"{\"id\":"+i+",\"name\":\"TV\",\"cmd\":\"ffmpeg http://localhost/ch/"+i+"\"}");}
    @Test public void utf8SplitMetadataAndUniqueRows()throws Exception{
        InputStream raw=bytes("\uFEFF{\"js\":{\"data\":["+ROW+","+ROW+"]},\"status\":0}");InputStream split=new FilterInputStream(raw){public int read(byte[] b,int o,int l)throws IOException{return super.read(b,o,Math.min(1,l));}};
        PortalChannelReader.Catalog result=read(split);assertEquals(1,result.channels.size());assertEquals(2,result.rowsRead);assertEquals("Żółć TV",result.channels.get(0).name);assertEquals(1234,result.downloadedAt);assertEquals(567,result.modifiedAt);assertFalse(result.truncated);assertEquals(1,read("{\"js\":["+ROW+"]}").channels.size());
        for(String flag:new String[]{"0","false","\"0\"","\"false\""}){PortalChannelReader.Channel c=read("{\"js\":["+ROW.replace("\"status\":1","\"use_http_tmp_link\":"+flag+",\"use_load_balancing\":0")+"]}").channels.get(0);assertEquals(Boolean.FALSE,c.useHttpTmpLink);assertEquals(Boolean.FALSE,c.useLoadBalancing);}
        assertNull(read("{\"js\":["+ROW+"]}").channels.get(0).useHttpTmpLink);assertNull(read("{\"js\":["+ROW.replace("\"status\":1","\"use_http_tmp_link\":0.0")+"]}").channels.get(0).useHttpTmpLink);
    }
    @Test public void thirtyEightMegabytesStreamWithoutWholeDocument()throws Exception{
        String chunk="\""+repeat('x',640000)+"\"";InputStream input=generated(62,i->i==0?"{\"js\":["+ROW+"],\"unused\":[":i==61?"]}":(i==1?"":",")+chunk);
        PortalChannelReader.Catalog result=read(input);assertTrue(result.rawBytes>38_000_000);assertEquals(1,result.channels.size());assertTrue(result.retainedBytes<1000);assertFalse(result.truncated);
    }
    @Test public void uniqueCountCapStillReadsWholeResponse()throws Exception{
        PortalChannelReader.Catalog result=read(rows(30001,""));assertEquals(30000,result.channels.size());assertEquals(30001,result.rowsRead);assertTrue(result.truncated);assertEquals("30000",result.channels.get(29999).id);
        rejects(()->read(rows(30001,",\"message\":\"Authorization failed. 75\"")));
    }
    @Test public void retainedByteBudgetIsBoundedAndLaterMalformedDataRejected()throws Exception{
        String command=repeat('x',7900);IntFunction<String> part=i->i==0?"{\"js\":[":i==1101?"]}":(i==1?"":",")+"{\"id\":"+i+",\"name\":\"TV\",\"cmd\":\""+command+"\"}";
        PortalChannelReader.Catalog result=read(generated(1102,part));assertTrue(result.truncated);assertTrue(result.channels.size()<1100);assertTrue(result.retainedBytes<=8_000_000);assertEquals(1100,result.rowsRead);
        rejects(()->read(generated(1102,i->i==1101?"]} trailing":part.apply(i))));
    }
    @Test public void envelopeRefusalsBeforeAndAfterDataAreRejected()throws Exception{
        for(String field:new String[]{"\"blocked\":true","\"status\":1","\"auth\":false","\"auth\":{}","\"status\":[]","\"error\":{}","\"errors\":[\"Access denied\"]","\"msg\":[\"Account blocked\"]","\"msg\":\"Account blocked\"","\"message\":\"Authorization failed. 75\"","\"block_msg\":[\"Invalid token\"]"}){
            rejects(()->read("{"+field+",\"js\":["+ROW+"]}"));rejects(()->read("{\"js\":{\"data\":["+ROW+"],"+field+"}}"));
        }for(String text:new String[]{"Not authorized.","Authentication required.","Authorization required."})rejects(()->read("{\"js\":["+ROW+"],\"msg\":\""+text+"\"}"));assertEquals(1,read("{\"js\":["+ROW+"],\"errors\":[]}").channels.size());
    }
    @Test public void malformedUtf8JsonAndIgnoredEscapesFailClosed()throws Exception{
        for(String bad:new String[]{"{\"js\":["+ROW+"]}{}","{\"js\":["+ROW+"],}","{\"js\":[],\"js\":[]}","{\"js\":[{\"id\":1,\"id\":2,\"cmd\":\"x\"}]}","{\"js\":[{\"id\":{},\"cmd\":\"x\"}]}","{\"js\":[],\"unused\":\"\\x\"}","{\"js\":[],\"unused\":\"\\u12XZ\"}","{\"js\":[],\"unused\":\"\\\n\"}"})rejects(()->read(bad));
        byte[] prefix="{\"js\":[],\"unused\":\"".getBytes(StandardCharsets.UTF_8);ByteArrayOutputStream data=new ByteArrayOutputStream();data.write(prefix);data.write(new byte[]{(byte)0xc3,0x28});data.write("\"}".getBytes(StandardCharsets.UTF_8));rejects(()->read(new ByteArrayInputStream(data.toByteArray())));
    }
    @Test public void rawTokenDepthAndSelectedFieldLimitsAreExact()throws Exception{
        byte[] json=("{\"js\":["+ROW+"]}").getBytes(StandardCharsets.UTF_8);PortalChannelReader.read(new ByteArrayInputStream(json),URL,0,0,new AtomicBoolean(),SystemClock.elapsedRealtime()+30000,json.length);
        rejects(()->PortalChannelReader.read(new ByteArrayInputStream(json),URL,0,0,new AtomicBoolean(),SystemClock.elapsedRealtime()+30000,json.length-1));
        rejects(()->read("{\"js\":[],\"unused\":\""+repeat('x',1_048_577)+"\"}"));rejects(()->read("{\"js\":[],\"unused\":"+repeat('[',65)+"0"+repeat(']',65)+"}"));
        rejects(()->read("{\"js\":[{\"id\":1,\"name\":\"TV\",\"cmd\":\""+repeat('x',8193)+"\"}]}"));
    }
    @Test public void cancellationAndDeadlineCannotReturnPartialCatalog()throws Exception{
        AtomicBoolean cancel=new AtomicBoolean();InputStream input=new FilterInputStream(rows(1000,"")){int reads;public int read(byte[] b,int o,int l)throws IOException{if(++reads==3)cancel.set(true);return super.read(b,o,Math.min(256,l));}};
        rejects(()->PortalChannelReader.read(input,URL,0,0,cancel,SystemClock.elapsedRealtime()+30000));
        rejects(()->PortalChannelReader.read(bytes("{\"js\":[]}"),URL,0,0,new AtomicBoolean(),SystemClock.elapsedRealtime()-1));
    }
    @Test public void sharedHttpRedirectGzipMetadataAndPrivateHeaders()throws Exception{
        ByteArrayOutputStream packed=new ByteArrayOutputStream();try(GZIPOutputStream gzip=new GZIPOutputStream(packed)){gzip.write(("{\"js\":["+ROW+"]}").getBytes(StandardCharsets.UTF_8));}AtomicInteger requests=new AtomicInteger();
        OkHttpClient client=ProviderHttp.client(null).newBuilder().addInterceptor(chain->{Request r=chain.request();assertEquals("mac=synthetic-secret",r.header("Cookie"));int n=requests.incrementAndGet();Response.Builder b=new Response.Builder().request(r).protocol(Protocol.HTTP_1_1).message("Fixture");if(n==1)return b.code(302).header("Location","/moved.php").body(ResponseBody.create(null,new byte[0])).build();return b.code(200).header("Content-Encoding","gzip").header("Last-Modified","Sun, 15 Nov 2026 08:00:00 GMT").body(ResponseBody.create(null,packed.toByteArray())).build();}).build();
        PortalChannelReader.Catalog result=ProviderHttp.usingClient(client).fetchPortalChannels(URL,Collections.singletonMap("Cookie","mac=synthetic-secret"),new AtomicBoolean());assertEquals(2,requests.get());assertEquals(1,result.channels.size());assertTrue(result.modifiedAt>0);assertTrue(result.downloadedAt>result.modifiedAt);assertTrue(result.url.endsWith("/moved.php"));
        for(String location:new String[]{"https://other.example/path","http://fixture.example/path","http://127.0.0.1/path"}){AtomicInteger count=new AtomicInteger();OkHttpClient blocked=ProviderHttp.client(null).newBuilder().addInterceptor(chain->{count.incrementAndGet();return new Response.Builder().request(chain.request()).protocol(Protocol.HTTP_1_1).message("Fixture").code(302).header("Location",location).body(ResponseBody.create(null,new byte[0])).build();}).build();rejects(()->ProviderHttp.usingClient(blocked).fetchPortalChannels(URL,Collections.singletonMap("Cookie","mac=synthetic-secret"),new AtomicBoolean()));assertEquals(1,count.get());}
        rejects(()->ProviderHttp.usingClient(client).fetchPortalChannels("https://fixture.example/list.m3u",Collections.emptyMap(),new AtomicBoolean()));
        String padding="\""+repeat('x',640000)+"\"";OkHttpClient large=ProviderHttp.client(null).newBuilder().addInterceptor(chain->new Response.Builder().request(chain.request()).protocol(Protocol.HTTP_1_1).message("Fixture").code(200).body(new ResponseBody(){public MediaType contentType(){return MediaType.parse("application/json");}public long contentLength(){return 13_000_000;}public BufferedSource source(){return Okio.buffer(Okio.source(generated(23,i->i==0?"{\"js\":["+ROW+"],\"extra\":[":i==22?"]}":(i==1?"":",")+padding)));}}).build()).build();
        rejects(()->ProviderHttp.usingClient(large).fetch(URL,Collections.emptyMap(),new AtomicBoolean()));
        assertTrue(ProviderHttp.usingClient(large).fetchPortalChannels(URL,Collections.emptyMap(),new AtomicBoolean()).rawBytes>12*1024*1024);
    }
    @Test public void repositoryShowsTruncationAndRejectsLateFailureWithoutReplacingVault()throws Exception{
        android.content.Context context=InstrumentationRegistry.getInstrumentation().getTargetContext();File root=new File(context.getCacheDir(),"streaming-"+UUID.randomUUID());String profile=UUID.randomUUID().toString().replace("-","");AtomicBoolean fail=new AtomicBoolean();
        ProviderHttp.Fetcher fetcher=new ProviderHttp.Fetcher(){public ProviderHttp.Document fetch(String url,Map<String,String> h,AtomicBoolean c){return new ProviderHttp.Document(url,"{\"js\":[{\"id\":1,\"title\":\"TV\"}]}".getBytes(StandardCharsets.UTF_8),1234,0);}public PortalChannelReader.Catalog fetchPortalChannels(String url,Map<String,String> h,AtomicBoolean c)throws IOException{if(fail.get())throw new IOException("Portal odmówił udostępnienia listy kanałów.");PortalChannelReader.Catalog result=new PortalChannelReader.Catalog(url,1234,0);result.channels.add(new PortalChannelReader.Channel("1","TV","ffmpeg http://localhost/ch/1","","1"));result.rowsRead=30001;result.rawBytes=38_099_494;result.truncated=true;return result;}};
        try{ProviderRepository repo=new ProviderRepository(root,profile,fetcher,host->new InetAddress[]{InetAddress.getByAddress(new byte[]{93,(byte)184,(byte)216,34})});ProviderRepository.SourceInfo added=repo.addPortal("Fixture","https://fixture.example/c/","02:00:00:00:00:01",new AtomicBoolean());assertTrue(added.status.contains("Lista ograniczona"));assertTrue(added.status.contains("odtwarzanie niepotwierdzone"));ProviderVault vault=new ProviderVault(new File(root,profile),profile);JSONObject before=vault.read(added.id);assertTrue(before.getBoolean("portalTruncated"));assertEquals(30001,before.getLong("portalRowsRead"));fail.set(true);rejects(()->repo.refresh(added.id,new AtomicBoolean()));assertEquals(before.toString(),vault.read(added.id).toString());}
        finally{delete(root);java.security.KeyStore keys=java.security.KeyStore.getInstance("AndroidKeyStore");keys.load(null);keys.deleteEntry("swir.providers.v1."+profile);}
    }
    private static void delete(File file){File[] children=file.listFiles();if(children!=null)for(File child:children)delete(child);file.delete();}
}
