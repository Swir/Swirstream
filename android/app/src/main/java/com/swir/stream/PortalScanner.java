package com.swir.stream;

import android.content.Context;
import java.io.IOException;
import java.util.*;
import java.util.concurrent.*;
import java.util.concurrent.atomic.AtomicBoolean;
import org.json.JSONObject;

/** Bounded read-only scan. A catalogue result never asserts decoded playback. */
final class PortalScanner {
    static final long ENTRY_MS=30000,TOTAL_MS=600000;
    enum Status { NOT_CHECKED, CHECKING, CLIENT_FOUND, PAGE_AVAILABLE, PUBLIC_CHECK_UNSUPPORTED, CATALOG_AVAILABLE, ACCESS_DENIED, FAILED, TIMEOUT, CANCELLED }
    static final class Result {
        final Status status;final int channels;final boolean limited;
        Result(Status status,int channels,boolean limited){this.status=status;this.channels=channels;this.limited=limited;}
        static Result status(Status status){return new Result(status,0,false);}
        String label(){switch(status){case CHECKING:return "Sprawdzanie…";case CLIENT_FOUND:return "Rozpoznano stronę portalu • dostęp i odtwarzanie niesprawdzone";case PAGE_AVAILABLE:return "Strona odpowiada • portal i dostęp niepotwierdzone";case PUBLIC_CHECK_UNSUPPORTED:return "Sprawdzenie publicznej strony nieobsługiwane • dodaj własny MAC";case CATALOG_AVAILABLE:return "Pobrano "+channels+" kanałów"+(limited?" (limit listy)":"")+" • odtwarzanie niesprawdzone";case ACCESS_DENIED:return "Odmowa dostępu";case FAILED:return "Błąd połączenia lub odpowiedzi • brak potwierdzenia";case TIMEOUT:return "Limit czasu • wynik niepełny";case CANCELLED:return "Anulowano • wynik niepełny";default:return "Nie sprawdzono";}}
    }
    interface Probe {Result check(PortalTxt.Entry entry,AtomicBoolean cancelled,long deadline)throws Exception;}
    interface Progress {void update(int index,Result result);}
    private final Probe probe;private final long entryMs,totalMs;
    PortalScanner(Context context){this(network(context),ENTRY_MS,TOTAL_MS);}
    PortalScanner(Probe probe,long entryMs,long totalMs){this.probe=probe;this.entryMs=entryMs;this.totalMs=totalMs;}
    private static long now(){return System.nanoTime()/1_000_000;}
    List<Result> scan(List<PortalTxt.Entry> entries,AtomicBoolean cancelled,Progress progress){
        if(entries.size()>PortalTxt.MAX_ENTRIES)throw new IllegalArgumentException("Too many scanner entries");
        List<Result> results=new ArrayList<>();for(int i=0;i<entries.size();i++)results.add(Result.status(Status.NOT_CHECKED));
        ScheduledExecutorService timer=Executors.newSingleThreadScheduledExecutor();long end=now()+totalMs;
        try{for(int i=0;i<entries.size();i++){
            if(cancelled.get()||now()>=end)break;AtomicBoolean item=new AtomicBoolean();long deadline=Math.min(end,now()+entryMs);
            ScheduledFuture<?> guard=timer.scheduleAtFixedRate(()->{if(cancelled.get()||now()>=deadline)item.set(true);},0,20,TimeUnit.MILLISECONDS);
            Result outcome;progress.update(i,Result.status(Status.CHECKING));
            try{outcome=probe.check(entries.get(i),item,deadline);if(cancelled.get())outcome=Result.status(Status.CANCELLED);else if(item.get()||now()>=deadline)outcome=Result.status(Status.TIMEOUT);}
            catch(Exception error){outcome=Result.status(cancelled.get()?Status.CANCELLED:item.get()||now()>=deadline?Status.TIMEOUT:denied(error)?Status.ACCESS_DENIED:Status.FAILED);}
            finally{guard.cancel(false);item.set(true);}
            results.set(i,outcome);progress.update(i,outcome);
            if(cancelled.get()||now()>=end)break;
            try{Thread.sleep(300);}catch(InterruptedException stopped){Thread.currentThread().interrupt();cancelled.set(true);break;}
        }}finally{timer.shutdownNow();}
        return results;
    }
    static boolean denied(Exception error){if(error instanceof ProviderHttp.StatusException){int code=((ProviderHttp.StatusException)error).code;return code==401||code==403;}String msg=error.getMessage();return "Portal odmówił dostępu dla podanego MAC. Sprawdź uprawnienia u dostawcy.".equals(msg)||"Portal nie przyznał sesji dla podanego MAC.".equals(msg)||"Portal nie potwierdził aktywnego urządzenia. Może wymagać dodatkowej rejestracji u dostawcy.".equals(msg);}
    static Probe network(Context context){return network(ProviderHttp.client(context));}
    static Probe network(okhttp3.OkHttpClient base){return (entry,cancel,deadline)->{
        if(!entry.account()){
            if(!SourceExplorerRules.allowedPage(entry.url))return Result.status(Status.PUBLIC_CHECK_UNSUPPORTED);
            long left=Math.max(1,deadline-now());SourceExplorerHttp http=new SourceExplorerHttp(base,left,true);
            PublicHttp.Document page=http.fetch(entry.url,cancel);PublicHttp.checkCancelled(cancel);
            return Result.status(SourceExplorerRules.portalFingerprint(page.url,page.text)?Status.CLIENT_FOUND:Status.PAGE_AVAILABLE);
        }
        ProviderHttp.Fetcher fetcher=new ProviderHttp.Fetcher(){
            private ProviderHttp.Fetcher bounded()throws IOException{PublicHttp.checkCancelled(cancel);long left=deadline-now();if(left<=0)throw new IOException("Limit czasu skanera.");return ProviderHttp.usingClient(base.newBuilder().callTimeout(left,TimeUnit.MILLISECONDS).build());}
            public ProviderHttp.Document fetch(String url,Map<String,String> headers,AtomicBoolean stopped)throws IOException{return bounded().fetch(url,headers,stopped);}
            public PortalChannelReader.Catalog fetchPortalChannels(String url,Map<String,String> headers,AtomicBoolean stopped)throws IOException{return bounded().fetchPortalChannels(url,headers,stopped);}
        };
        ProviderAdapters.Result catalogue=ProviderAdapters.portal(new JSONObject().put("url",entry.url).put("mac",entry.mac),fetcher,cancel);
        PublicHttp.checkCancelled(cancel);if(catalogue.entries.length()==0)throw new IOException("Brak kanałów.");
        return new Result(Status.CATALOG_AVAILABLE,catalogue.entries.length(),catalogue.portalTruncated);
    };}
    static String csv(List<PortalTxt.Entry> entries,List<Result> results){StringBuilder out=new StringBuilder("entry,line,type,status,channels,limited,playback\r\n");for(int i=0;i<entries.size();i++){Result row=i<results.size()?results.get(i):Result.status(Status.NOT_CHECKED);out.append(i+1).append(',').append(entries.get(i).line).append(',').append(entries.get(i).account()?"own_mac":"url_only").append(',').append(row.status.name()).append(',').append(row.channels).append(',').append(row.limited).append(",not_checked\r\n");}return out.toString();}
}
