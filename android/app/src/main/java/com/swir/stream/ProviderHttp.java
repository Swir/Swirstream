package com.swir.stream;

import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.InputStream;
import android.content.Context;
import android.os.Build;
import java.net.InetAddress;
import java.net.Proxy;
import java.net.URI;
import java.net.URL;
import java.nio.charset.StandardCharsets;
import java.util.Collections;
import java.util.LinkedHashMap;
import java.util.Locale;
import java.util.Map;
import java.util.concurrent.atomic.AtomicBoolean;
import java.util.zip.GZIPInputStream;
import java.util.concurrent.TimeUnit;
import javax.net.ssl.SSLContext;
import javax.net.ssl.TrustManager;
import javax.net.ssl.X509TrustManager;
import okhttp3.Authenticator;
import okhttp3.CookieJar;
import okhttp3.Dns;
import okhttp3.OkHttpClient;
import okhttp3.Request;
import okhttp3.Response;

/** Explicit user-owned providers. This does not change public crawler policy. */
@androidx.annotation.OptIn(markerClass=androidx.media3.common.util.UnstableApi.class)
public final class ProviderHttp {
    static final int MAX_BYTES=12*1024*1024;
    static final int PORTAL_CHANNEL_BYTES=64_000_000;
    static int documentLimit(String url){
        try{String query=checkedUri(url).getRawQuery();if(query==null)return MAX_BYTES;String type=null,action=null;
            for(String pair:query.split("&",-1)){String[] fields=pair.split("=",2);if(fields.length!=2)continue;if("type".equals(fields[0])){if(type!=null)return MAX_BYTES;type=fields[1];}if("action".equals(fields[0])){if(action!=null)return MAX_BYTES;action=fields[1];}}
            return "itv".equals(type)&&"get_all_channels".equals(action)?PORTAL_CHANNEL_BYTES:MAX_BYTES;
        }catch(IOException ignored){return MAX_BYTES;}
    }
    public static final class Document {
        public final String url;public final byte[] bytes;public final long downloadedAt,modifiedAt;
        Document(String url,byte[] bytes,long downloadedAt,long modifiedAt){this.url=url;this.bytes=bytes;this.downloadedAt=downloadedAt;this.modifiedAt=modifiedAt;}
        public String text(){return new String(bytes,StandardCharsets.UTF_8);}
    }
    interface Fetcher {
        Document fetch(String url,Map<String,String> headers,AtomicBoolean cancelled)throws IOException;
        default PortalChannelReader.Catalog fetchPortalChannels(String url,Map<String,String> headers,AtomicBoolean cancelled)throws IOException{
            if(documentLimit(url)!=PORTAL_CHANNEL_BYTES)throw new IOException("Nieprawidłowe żądanie listy portalu.");
            Document document=fetch(url,headers,cancelled);
            return PortalChannelReader.read(new java.io.ByteArrayInputStream(document.bytes),document.url,document.downloadedAt,document.modifiedAt,cancelled,android.os.SystemClock.elapsedRealtime()+30000);
        }
    }
    static final class StatusException extends IOException {final int code;StatusException(int code){super(code==401||code==403?"Dostawca odrzucił dane dostępu lub uprawnienia konta.":"Źródło odpowiedziało błędem HTTP "+code+".");this.code=code;}}
    static final Fetcher NETWORK=usingClient(client(null));
    interface Resolver {InetAddress[] resolve(String host)throws IOException;}
    private ProviderHttp(){}
    static Fetcher network(Context context){return usingClient(client(context));}
    static Fetcher usingClient(OkHttpClient client){return new Fetcher(){
        public Document fetch(String url,Map<String,String> headers,AtomicBoolean cancelled)throws IOException{return ProviderHttp.fetch(client,url,headers,cancelled);}
        public PortalChannelReader.Catalog fetchPortalChannels(String url,Map<String,String> headers,AtomicBoolean cancelled)throws IOException{
            if(documentLimit(url)!=PORTAL_CHANNEL_BYTES)throw new IOException("Nieprawidłowe żądanie listy portalu.");
            return readResponse(client,url,headers,cancelled,PORTAL_CHANNEL_BYTES,(input,current,downloaded,modified,deadline)->PortalChannelReader.read(input,current,downloaded,modified,cancelled,deadline));
        }
    };}
    static OkHttpClient client(Context context){
        OkHttpClient.Builder builder=new OkHttpClient.Builder().dns(new ProviderDataSource.PublicDns(Dns.SYSTEM)).proxy(Proxy.NO_PROXY).cookieJar(CookieJar.NO_COOKIES).authenticator(Authenticator.NONE).proxyAuthenticator(Authenticator.NONE).followRedirects(false).followSslRedirects(false).connectTimeout(8,TimeUnit.SECONDS).readTimeout(10,TimeUnit.SECONDS).callTimeout(30,TimeUnit.SECONDS);
        if(context!=null&&Build.VERSION.SDK_INT<=25)try{X509TrustManager manager=LegacyTls.createTrustManager(context);SSLContext tls=SSLContext.getInstance("TLS");tls.init(null,new TrustManager[]{manager},null);builder.sslSocketFactory(tls.getSocketFactory(),manager);}catch(Exception ignored){/* System TLS remains enforced. */}
        return builder.build();
    }

    /** Syntax-only check; DNS is checked again for each actual connection. */
    public static URI checkedUri(String value)throws IOException{
        try{
            if(value==null||value.length()>16384||value.indexOf('\r')>=0||value.indexOf('\n')>=0)throw new Exception();
            URI uri=new URI(value.trim());String host=uri.getHost(),scheme=uri.getScheme();
            if(host==null||!("https".equalsIgnoreCase(scheme)||"http".equalsIgnoreCase(scheme))||uri.getRawUserInfo()!=null||uri.getFragment()!=null||uri.getPort()==0||uri.getPort()>65535)throw new Exception();
            host=host.toLowerCase(Locale.ROOT);if(host.endsWith("."))host=host.substring(0,host.length()-1);if(host.equals("localhost")||host.endsWith(".localhost")||host.endsWith(".local")||host.endsWith(".lan")||host.endsWith(".internal")||host.endsWith(".home")||host.endsWith(".localdomain")||!host.contains(".")&&!host.contains(":"))throw new Exception();
            return uri;
        }catch(Exception e){throw new IOException("Podaj publiczny adres HTTP lub HTTPS bez danych przed nazwą serwera.");}
    }
    static void validateAddress(URI uri,Resolver resolver)throws IOException{
        InetAddress[] addresses;
        try{addresses=resolver.resolve(uri.getHost());}catch(Exception e){throw new IOException("Nie można odnaleźć serwera źródła.");}
        if(addresses==null||addresses.length==0)throw new IOException("Nie można odnaleźć serwera źródła.");
        for(InetAddress address:addresses)if(!ProviderDataSource.publicAddress(address))throw new IOException("Źródło wskazuje prywatny lub lokalny adres sieciowy.");
    }
    public static void validatePublic(String url)throws IOException{validateAddress(checkedUri(url),InetAddress::getAllByName);}
    public static boolean sameOrigin(String a,String b){try{URI x=checkedUri(a),y=checkedUri(b);return x.getScheme().equalsIgnoreCase(y.getScheme())&&x.getHost().equalsIgnoreCase(y.getHost())&&port(x)==port(y);}catch(IOException e){return false;}}
    private static int port(URI uri){return uri.getPort()>0?uri.getPort():"https".equalsIgnoreCase(uri.getScheme())?443:80;}
    static boolean protectedUrl(String url){try{String q=checkedUri(url).getRawQuery();return q!=null&&q.toLowerCase(Locale.ROOT).matches(".*(?:^|[&;])(?:username|password|token|access_token|auth|key|session|mac)=[^&;]+.*");}catch(IOException e){return true;}}
    static String redirect(String current,String location,boolean protectedRequest)throws IOException{
        if(location==null)throw new IOException("Serwer zwrócił niepełne przekierowanie.");
        URI a=checkedUri(current);String next;try{next=a.resolve(location).toString();}catch(Exception e){throw new IOException("Nieprawidłowe przekierowanie źródła.");}URI b=checkedUri(next);
        if("https".equalsIgnoreCase(a.getScheme())&&!"https".equalsIgnoreCase(b.getScheme()))throw new IOException("Źródło próbuje wyłączyć szyfrowanie połączenia.");
        if(protectedRequest&&!sameOrigin(current,next))throw new IOException("Serwer próbuje przekazać prywatne połączenie innej domenie.");return next;
    }
    static Map<String,String> cleanHeaders(Map<String,String> raw)throws IOException{
        Map<String,String> out=new LinkedHashMap<>();if(raw==null)return out;if(raw.size()>20)throw new IOException("Za dużo nagłówków połączenia.");
        for(Map.Entry<String,String> entry:raw.entrySet()){String key=entry.getKey(),value=entry.getValue();if(key==null||value==null||!key.matches("[A-Za-z0-9-]{1,80}")||value.length()>8192||value.matches("(?s).*[\\x00-\\x1f\\x7f].*"))throw new IOException("Nieprawidłowy nagłówek połączenia.");if(key.equalsIgnoreCase("Host")||key.equalsIgnoreCase("Content-Length")||key.equalsIgnoreCase("Connection")||key.equalsIgnoreCase("Transfer-Encoding")||key.equalsIgnoreCase("Proxy-Authorization"))throw new IOException("Ten nagłówek jest zarządzany przez odtwarzacz.");out.put(key,value);}return out;
    }
    public static Document fetch(String original,Map<String,String> supplied,AtomicBoolean cancelled)throws IOException{return fetch(client(null),original,supplied,cancelled);}
    private static Document fetch(OkHttpClient client,String original,Map<String,String> supplied,AtomicBoolean cancelled)throws IOException{
        return readResponse(client,original,supplied,cancelled,MAX_BYTES,(input,current,downloaded,modified,deadline)->{
            try(ByteArrayOutputStream bytes=new ByteArrayOutputStream()){
                byte[] block=new byte[16384];int n;while((n=input.read(block))!=-1){PublicHttp.checkCancelled(cancelled);if(android.os.SystemClock.elapsedRealtime()>deadline)throw new IOException("Przekroczono czas pobierania źródła.");if(bytes.size()+n>MAX_BYTES)throw new IOException("Lista źródła przekracza limit rozmiaru.");bytes.write(block,0,n);}return new Document(current,bytes.toByteArray(),downloaded,modified);
            }
        });
    }
    private interface ResponseReader<T>{T read(InputStream input,String url,long downloaded,long modified,long deadline)throws IOException;}
    private static <T>T readResponse(OkHttpClient client,String original,Map<String,String> supplied,AtomicBoolean cancelled,int maxBytes,ResponseReader<T> reader)throws IOException{
        String current=checkedUri(original).toString();Map<String,String> headers=cleanHeaders(supplied);boolean protect=protectedUrl(original)||headers.keySet().stream().anyMatch(k->k.equalsIgnoreCase("Authorization")||k.equalsIgnoreCase("Cookie"));long budget=client.callTimeoutMillis()>0?Math.min(30000,client.callTimeoutMillis()):30000;long deadline=android.os.SystemClock.elapsedRealtime()+budget;
        for(int hop=0;hop<5;hop++){
            PublicHttp.checkCancelled(cancelled);checkedUri(current);if(android.os.SystemClock.elapsedRealtime()>deadline)throw new IOException("Przekroczono czas pobierania źródła.");protect|=protectedUrl(current);
            Request.Builder request=new Request.Builder().url(current).header("User-Agent","SwirStreamAndroid/0.3.2").header("Accept-Encoding","gzip");for(Map.Entry<String,String> entry:headers.entrySet())request.header(entry.getKey(),entry.getValue());
            try(Response response=client.newBuilder().callTimeout(Math.max(1,deadline-android.os.SystemClock.elapsedRealtime()),TimeUnit.MILLISECONDS).build().newCall(request.build()).execute()){
                int code=response.code();if(code>=300&&code<400){String next=redirect(current,response.header("Location"),protect);if(!sameOrigin(current,next))headers=Collections.emptyMap();current=next;continue;}
                if(code<200||code>=300)throw new StatusException(code);
                if(response.body()==null)throw new IOException("Źródło nie zwróciło danych.");if(response.body().contentLength()>maxBytes)throw new IOException("Lista źródła przekracza limit rozmiaru.");
                java.util.Date lastModified=response.headers().getDate("Last-Modified");long modified=lastModified==null?0:lastModified.getTime();try(InputStream raw=response.body().byteStream();InputStream input="gzip".equalsIgnoreCase(response.header("Content-Encoding"))?new GZIPInputStream(raw):raw){
                    return reader.read(input,current,System.currentTimeMillis(),modified,deadline);
                }
            }catch(IOException e){PublicHttp.checkCancelled(cancelled);if(e instanceof StatusException)throw e;String msg=e.getMessage();if(msg!=null&&(msg.startsWith("Dostawca ")||msg.startsWith("Źródło ")||msg.startsWith("Serwer ")||msg.startsWith("Lista ")||msg.startsWith("Portal odmówił ")||msg.startsWith("Przekroczono ")||msg.startsWith("Nieprawidłowe ")))throw new IOException(msg);throw new IOException("Nie udało się połączyć ze źródłem. Poprzednia lista pozostaje zapisana.");}
        }throw new IOException("Źródło wykonało zbyt wiele przekierowań.");
    }
}
