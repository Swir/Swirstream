package com.swir.stream;

import android.content.Context;
import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.net.URI;
import java.nio.charset.StandardCharsets;
import java.util.Locale;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicBoolean;
import java.util.zip.GZIPInputStream;
import okhttp3.OkHttpClient;
import okhttp3.Request;
import okhttp3.Response;

/** No cookies, authentication, JS, portal protocol or implicit redirects. DNS is connection-pinned. */
final class SourceExplorerHttp implements PublicHttp.Fetcher {
    static final int MAX_BYTES=4_000_000;
    private final OkHttpClient client;
    SourceExplorerHttp(Context context){this(ProviderHttp.client(context));}
    SourceExplorerHttp(OkHttpClient base){client=base.newBuilder().followRedirects(false).followSslRedirects(false).callTimeout(20,TimeUnit.SECONDS).build();}
    static String allowed(String url)throws IOException{String safe=SourceExplorerRules.canonical(url);if(!SourceExplorerRules.allowedPage(safe))throw new IOException("Pominięto adres konta, API lub administracji. Eksplorator analizuje tylko publiczne strony i media.");return safe;}
    static String redirect(String current,String location)throws IOException{
        if(location==null)throw new IOException("Przekierowanie nie zawiera adresu.");String next;try{next=allowed(new URI(current).resolve(location).toString());}catch(Exception error){throw new IOException("Zablokowano przekierowanie poza publiczną analizę stron.");}
        if(current.startsWith("https://")&&!next.startsWith("https://"))throw new IOException("Zablokowano przekierowanie wyłączające HTTPS.");return next;
    }
    @Override public PublicHttp.Document fetch(String original,AtomicBoolean cancelled)throws IOException{
        String url=allowed(original);long deadline=android.os.SystemClock.elapsedRealtime()+20000;
        for(int hop=0;hop<5;hop++){
            PublicHttp.checkCancelled(cancelled);url=allowed(url);if(android.os.SystemClock.elapsedRealtime()>deadline)throw new IOException("Limit czasu publicznej strony.");Request request=new Request.Builder().url(url).header("User-Agent","SwirStreamAndroid/0.3.3 (+public-source-explorer)").header("Accept-Encoding","gzip").build();
            try(Response response=client.newCall(request).execute()){
                int status=response.code();if(status>=300&&status<400){url=redirect(url,response.header("Location"));continue;}if(status<200||status>=300)throw new IOException("HTTP "+status+" — nie potwierdzono dostępności strony.");if(response.body()==null)throw new IOException("Pusta odpowiedź strony.");
                String type=response.header("Content-Type","").toLowerCase(Locale.ROOT);if(!(type.startsWith("text/")||type.contains("xml")||type.contains("json")||type.contains("mpegurl")||type.contains("octet-stream")||type.isEmpty()))throw new IOException("Adres nie zwrócił strony ani manifestu. Materiał sprawdza odtwarzacz.");if(response.body().contentLength()>MAX_BYTES)throw new IOException("Dokument przekracza limit 4 MB.");
                long modified=response.headers().getDate("Last-Modified")==null?0:response.headers().getDate("Last-Modified").getTime();try(InputStream raw=response.body().byteStream();InputStream input="gzip".equalsIgnoreCase(response.header("Content-Encoding"))?new GZIPInputStream(raw):raw;ByteArrayOutputStream bytes=new ByteArrayOutputStream()){
                    byte[] block=new byte[8192];int read;while((read=input.read(block))!=-1){PublicHttp.checkCancelled(cancelled);if(android.os.SystemClock.elapsedRealtime()>deadline)throw new IOException("Limit czasu publicznej strony.");if(bytes.size()+read>MAX_BYTES)throw new IOException("Dokument przekracza limit 4 MB.");bytes.write(block,0,read);}PublicHttp.checkCancelled(cancelled);return new PublicHttp.Document(url,new String(bytes.toByteArray(),StandardCharsets.UTF_8),type,System.currentTimeMillis(),modified);
                }
            }catch(IOException error){PublicHttp.checkCancelled(cancelled);String message=error.getMessage();if(message!=null&&(message.startsWith("HTTP ")||message.startsWith("Dokument ")||message.startsWith("Limit ")||message.startsWith("Adres ")||message.startsWith("Pusta ")||message.startsWith("Zablokowano ")||message.startsWith("Pominięto ")))throw new IOException(message);throw new IOException("Nie można pobrać publicznej strony. Nie potwierdzono jej dostępności.");}
        }
        throw new IOException("Za dużo przekierowań publicznej strony.");
    }
}
