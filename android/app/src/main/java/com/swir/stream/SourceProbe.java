package com.swir.stream;

import java.io.File;
import java.io.IOException;
import java.util.Collections;
import java.util.List;
import java.util.Locale;
import java.util.concurrent.atomic.AtomicBoolean;

/** Checks live manifests, never trains the AI or claims that video was decoded. */
final class SourceProbe {
    private final PublicHttp.Fetcher fetcher;private final WebCrawler.Log log;
    SourceProbe(PublicHttp.Fetcher fetcher,WebCrawler.Log log){this.fetcher=fetcher;this.log=log;}
    void check(List<Station> stations,CatalogRepository.Progress progress,AtomicBoolean cancelled)throws IOException{
        long deadline=System.currentTimeMillis()+30000;int checked=0;
        for(Station station:stations){
            PublicHttp.checkCancelled(cancelled);if(checked>=12||System.currentTimeMillis()>=deadline)break;
            String path;try{path=java.net.URI.create(station.url).getPath().toLowerCase(Locale.ROOT);}catch(Exception e){continue;}
            if(station.freshManifest()||!PublicHttp.allowedDocument(station.url)||!(path.endsWith(".m3u8")||path.endsWith(".mpd")))continue;
            checked++;CatalogRepository.progress(progress,"Deep Search 3/3: transmisja "+checked+"/"+Math.min(12,stations.size())+" • "+station.name);
            PublicHttp.Fetcher bounded=(url,cancel)->{if(System.currentTimeMillis()>=deadline)throw new IOException("Limit kontroli manifestów.");return fetcher.fetch(url,cancel,station.userAgent,station.referrer);};
            // No visit history is read or written by isLive().
            WebCrawler probe=new WebCrawler(bounded,log,null,null,(doc,all,matching)->{},()->false);
            try{station.liveManifest=probe.isLive(station.url,cancelled,0);station.manifestCheckedAt=System.currentTimeMillis();}
            catch(IOException e){PublicHttp.checkCancelled(cancelled);station.liveManifest=false;station.manifestCheckedAt=System.currentTimeMillis();log.write("Kontrola "+station.name+": "+e.getMessage()+". Adres pozostaje do próby w odtwarzaczu.");}
        }
        Collections.sort(stations,(a,b)->Boolean.compare(b.freshManifest(),a.freshManifest()));
    }
}
