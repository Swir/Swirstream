package com.swir.stream;

import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.net.HttpURLConnection;
import java.net.InetAddress;
import java.net.URI;
import java.net.URL;
import java.nio.charset.StandardCharsets;
import java.util.Locale;
import java.util.concurrent.atomic.AtomicBoolean;
import java.util.zip.GZIPInputStream;

/** Bounded public-document reader. Never executes scripts or browser credentials. */
public final class PublicHttp {
    public static final class Document {
        public final String url,text,contentType;public final long fetchedAt,modifiedAt;
        public Document(String url,String text,String type,long fetchedAt,long modifiedAt){this.url=url;this.text=text;this.contentType=type;this.fetchedAt=fetchedAt;this.modifiedAt=modifiedAt;}
    }
    public interface Fetcher {
        Document fetch(String url,AtomicBoolean cancelled)throws IOException;
        default Document fetch(String url,AtomicBoolean cancelled,String agent,String referrer)throws IOException{return fetch(url,cancelled);}
    }
    static final Fetcher FETCHER=new Fetcher(){public Document fetch(String url,AtomicBoolean cancelled)throws IOException{return PublicHttp.fetch(url,cancelled);}public Document fetch(String url,AtomicBoolean cancelled,String agent,String referrer)throws IOException{return PublicHttp.fetch(url,cancelled,agent,referrer);}};
    // These checks run for every broadcaster in the production directory. Do not
    // compile an ICU regex per hostname: tens of thousands of native matchers
    // caused allocation pressure before the crawler could visit its first page.
    private static boolean numericHost(String host){if(host.isEmpty())return false;for(int i=0;i<host.length();i++){char c=host.charAt(i);if(c!='.'&&(c<'0'||c>'9'))return false;}return true;}
    public static boolean browserOnly(String url){try{String host=new URI(url).getHost().toLowerCase(Locale.ROOT);for(String value:new String[]{"youtube.com","youtu.be","dailymotion.com","twitch.tv"})if(host.equals(value)||host.endsWith("."+value))return true;return false;}catch(Exception e){return true;}}
    public static boolean allowedStream(String url){
        try{URI uri=new URI(url);String scheme=uri.getScheme(),host=uri.getHost();if(!("https".equalsIgnoreCase(scheme)||"http".equalsIgnoreCase(scheme))||host==null||uri.getRawUserInfo()!=null)return false;
            host=host.toLowerCase(Locale.ROOT);if(host.equals("localhost")||host.endsWith(".localhost")||host.endsWith(".local")||host.endsWith(".lan")||host.endsWith(".internal")||host.endsWith(".home")||host.endsWith(".localdomain"))return false;
            if(numericHost(host)||host.contains(":")){if(!publicAddress(InetAddress.getByName(host)))return false;}else if(!host.contains("."))return false;
            String path=uri.getPath().toLowerCase(Locale.ROOT);return !browserOnly(url)&&!path.endsWith(".html")&&!path.endsWith(".htm");
        }catch(Exception e){return false;}
    }
    public static boolean allowedDocument(String url){
        try{URI uri=new URI(url);String scheme=uri.getScheme(),host=uri.getHost();if(!("https".equalsIgnoreCase(scheme)||"http".equalsIgnoreCase(scheme))||host==null||uri.getRawUserInfo()!=null)return false;
            int port=uri.getPort();if(port!=-1&&port!=80&&port!=443)return false;
            host=host.toLowerCase(Locale.ROOT);if(host.equals("localhost")||host.endsWith(".localhost")||host.endsWith(".local")||host.endsWith(".lan")||host.endsWith(".internal")||host.endsWith(".home")||host.endsWith(".localdomain"))return false;
            if(numericHost(host)||host.contains(":"))return publicAddress(InetAddress.getByName(host));return host.contains(".");
        }catch(Exception e){return false;}
    }
    static boolean publicAddress(InetAddress address){
        if(address.isAnyLocalAddress()||address.isLoopbackAddress()||address.isLinkLocalAddress()||address.isSiteLocalAddress()||address.isMulticastAddress())return false;
        byte[] b=address.getAddress();if(b.length==16)return (b[0]&0xe0)==0x20;
        int a=b[0]&255,c=b[1]&255;return a!=0&&a!=10&&a!=127&&a<224&&!(a==100&&c>=64&&c<=127)&&!(a==169&&c==254)&&!(a==172&&c>=16&&c<=31)&&!(a==192&&c==168);
    }
    public static void checkCancelled(AtomicBoolean cancelled)throws IOException{if(cancelled.get()||Thread.currentThread().isInterrupted())throw new IOException("Anulowano wyszukiwanie.");}
    public static Document fetch(String original,AtomicBoolean cancelled)throws IOException{
        return fetch(original,cancelled,"","");
    }
    private static Document fetch(String original,AtomicBoolean cancelled,String agent,String referrer)throws IOException{
        String url=original;long deadline=System.currentTimeMillis()+20000;
        for(int redirects=0;redirects<5;redirects++){
            checkCancelled(cancelled);if(!allowedDocument(url))throw new IOException("Niepubliczny adres dokumentu.");
            URI uri=URI.create(url);InetAddress[] addresses=InetAddress.getAllByName(uri.getHost());if(addresses.length==0)throw new IOException("Brak adresu serwera.");for(InetAddress address:addresses)if(!publicAddress(address))throw new IOException("Serwer wskazuje sieć prywatną.");
            HttpURLConnection connection=(HttpURLConnection)new URL(url).openConnection();connection.setInstanceFollowRedirects(false);connection.setConnectTimeout(8000);connection.setReadTimeout(8000);connection.setRequestProperty("User-Agent",agent.isEmpty()?"SwirStreamAndroid/0.2.1 (+public-stream-discovery)":M3u.clean(agent));connection.setRequestProperty("Accept-Encoding","gzip");if(allowedDocument(referrer))connection.setRequestProperty("Referer",referrer);
            try{
                int status=connection.getResponseCode();if(status>=300&&status<400){String next=connection.getHeaderField("Location");if(next==null)throw new IOException("Przekierowanie bez adresu.");url=uri.resolve(next).toString();continue;}
                if(status<200||status>=300)throw new IOException("HTTP "+status);
                String type=connection.getContentType();type=type==null?"":type.toLowerCase(Locale.ROOT);if(!(type.startsWith("text/")||type.contains("json")||type.contains("xml")||type.contains("mpegurl")||type.contains("octet-stream")||type.isEmpty()))throw new IOException("Adres nie jest dokumentem ani playlistą.");
                if(connection.getContentLength()>20_000_000)throw new IOException("Dokument przekracza 20 MB.");
                long modified=connection.getLastModified();try(InputStream raw=connection.getInputStream();InputStream input="gzip".equalsIgnoreCase(connection.getContentEncoding())?new GZIPInputStream(raw):raw;ByteArrayOutputStream buffer=new ByteArrayOutputStream()){
                    byte[] chunk=new byte[16384];int count;while((count=input.read(chunk))!=-1){checkCancelled(cancelled);if(System.currentTimeMillis()>deadline)throw new IOException("Limit czasu dokumentu.");if(buffer.size()+count>20_000_000)throw new IOException("Dokument przekracza 20 MB.");buffer.write(chunk,0,count);}
                    return new Document(url,new String(buffer.toByteArray(),StandardCharsets.UTF_8),type,System.currentTimeMillis(),modified);
                }
            }finally{connection.disconnect();}
        }
        throw new IOException("Za dużo przekierowań.");
    }
}
