package com.swir.stream;

import androidx.test.ext.junit.runners.AndroidJUnit4;
import androidx.test.platform.app.InstrumentationRegistry;
import org.json.JSONArray;
import org.json.JSONObject;
import org.junit.Before;
import org.junit.Test;
import org.junit.runner.RunWith;
import java.io.File;
import java.io.IOException;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.Collections;
import java.util.HashSet;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Set;
import java.util.UUID;
import java.util.concurrent.atomic.AtomicBoolean;
import static org.junit.Assert.*;

@RunWith(AndroidJUnit4.class)
public class CatalogRepositoryTest {
    File folder;Fixture fetch;CatalogRepository repo;Set<String> countries=new LinkedHashSet<>(Arrays.asList("PL","DE"));
    @Before public void setup(){folder=new File(InstrumentationRegistry.getInstrumentation().getTargetContext().getCacheDir(),"catalog-test-"+UUID.randomUUID());folder.mkdirs();fetch=new Fixture();repo=new CatalogRepository(folder,fetch);}
    static class Fixture implements PublicHttp.Fetcher {
        boolean offline,invalidMetadata,cancelRadio;long modified=System.currentTimeMillis()-2*CatalogRepository.DAY;final List<String> visits=new ArrayList<>();
        @Override public PublicHttp.Document fetch(String url,AtomicBoolean cancelled)throws IOException{
            visits.add(url);PublicHttp.checkCancelled(cancelled);if(offline)throw new IOException("503 fixture");String body;
            if(url.contains("channels.json")){
                try{JSONArray array=new JSONArray();for(int i=0;i<12;i++)array.put(invalidMetadata?new JSONObject().put("wrong",i):new JSONObject().put("id",i==0?"TVP1.pl":i==1?"DasErste.de":"Channel"+i+".pl").put("name",i==0?"TVP1":i==1?"Das Erste":"Channel "+i).put("country",i==1?"DE":"PL").put("website","https://channel"+i+".example/").put("alt_names",new JSONArray(i==1?"[\"ARD\"]":"[]")));body=array.toString();}catch(Exception e){throw new IOException(e);}
            }else if(url.contains("radio-browser")){if(cancelRadio){cancelled.set(true);PublicHttp.checkCancelled(cancelled);}String country=url.contains("countrycode=DE")?"DE":"PL";body="[{\"name\":\"Radio "+country+"\",\"url\":\"https://radio.example/"+country+"\",\"url_resolved\":\"https://radio.example/"+country+"\",\"stationuuid\":\"radio-"+country+"\",\"countrycode\":\""+country+"\",\"tags\":\"news\"}]";
            }else if(url.contains("Free-TV"))body="#EXTM3U x-tvg-url=\"https://epg.example/pl.xml\"\n#EXTINF:-1 tvg-logo=\"https://logo.example/a.png\",ARD\nhttps://alternate.example/de.m3u8\n#EXTINF:-1,Unknown\nhttps://alternate.example/unknown.m3u8\n";
            else if(url.contains("de.m3u"))body="#EXTM3U\n#EXTINF:-1 tvg-id=\"DasErste.de@HD\",Das Erste\nhttps://cdn.example:1935/de.m3u8\n";
            else body="#EXTM3U\n#EXTINF:-1 tvg-id=\"TVP1.pl@HD\",TVP1\nhttps://cdn.example/pl.m3u8\n";
            return new PublicHttp.Document(url,body,"text/plain",System.currentTimeMillis(),modified);
        }
    }
    @Test public void certificateFailureIsReportedOnlyWhenMirrorsAndCacheCannotServeFeed() throws Exception {
        java.security.cert.CertificateException cert=new java.security.cert.CertificateException("fixture");
        CatalogRepository failing=new CatalogRepository(folder,(url,cancel)->{throw new IOException(cert);});
        try{failing.feed("tv:JP",new String[]{"https://test.example/jp.m3u"},true,new AtomicBoolean());fail("feed must fail");}catch(IOException expected){}
        assertTrue(failing.catalogueCertificateRejected());
        assertFalse(CatalogRepository.certificateFailure(new IOException("404")));
        CatalogRepository working=new CatalogRepository(folder,(url,cancel)->{
            if(url.contains("first.example"))throw new IOException(cert);
            return new PublicHttp.Document(url,"#EXTM3U\n#EXTINF:-1,NHK\nhttps://station.example/live.m3u8","text/plain",System.currentTimeMillis(),System.currentTimeMillis());
        });
        working.feed("tv:JP",new String[]{"https://first.example/jp.m3u","https://second.example/jp.m3u"},true,new AtomicBoolean());
        assertFalse(working.catalogueCertificateRejected());
    }
    @Test public void wideSelectionUsesOneWorldPlaylistAndKeepsBritishFeedIdentity() {
        Set<String> many = new LinkedHashSet<>(Arrays.asList("GB","FR","IT","PL","ES","JP","US","CA","MX","EG","IL","CH","BE"));
        List<String> urls = new ArrayList<>();
        CatalogRepository catalog = new CatalogRepository(folder,(url,cancel)->{
            urls.add(url);
            if (url.contains("channels.json")) return fetch.fetch(url,cancel);
            return new PublicHttp.Document(url,"#EXTM3U\n#EXTINF:-1 tvg-id=\"Alpha.uk\",Alpha\nhttps://cdn.example/a.m3u8\n#EXTINF:-1 tvg-id=\"Other.cn\",Other\nhttps://cdn.example/b.m3u8","text/plain",System.currentTimeMillis(),0);
        });
        List<Station> rows = catalog.scan(many,false,null,new AtomicBoolean());
        assertTrue(rows.stream().anyMatch(s->s.country.equals("GB") && s.id.equals("Alpha.uk")));
        assertFalse(rows.stream().anyMatch(s->s.country.equals("CN")));
        assertTrue(urls.stream().anyMatch(s->s.endsWith("/index.m3u")));
        assertFalse(urls.stream().anyMatch(s->s.contains("/countries/")));
    }
    @Test public void refreshComparesAddressesAndPreservesLastGoodPlaylistOnError() throws Exception {
        String source="https://public.example/tv.m3u";
        String first="#EXTM3U\n#EXTINF:-1,One\nhttps://cdn.example/one.m3u8\n";
        String second=first.replace("one.m3u8","two.m3u8");
        String[] body={second};
        CatalogRepository catalog=new CatalogRepository(folder,(url,cancel)->new PublicHttp.Document(url,body[0],"text/plain",System.currentTimeMillis(),0));
        assertTrue(catalog.remember(new PublicHttp.Document(source,first,"text/plain",System.currentTimeMillis()-1000,0),1,1));
        String result=catalog.refreshFoundPlaylist(catalog.playlists().get(0),countries,new AtomicBoolean());
        assertTrue(result.contains("Dodane adresy: 1, usunięte: 1"));
        CatalogRepository.FoundPlaylist saved=catalog.playlists().get(0);
        assertTrue(saved.text.contains("two.m3u8"));
        body[0]="<html>offline</html>";
        try { catalog.refreshFoundPlaylist(saved,countries,new AtomicBoolean()); fail(); } catch(IOException expected) {}
        assertEquals(saved.sha256,catalog.playlists().get(0).sha256);
    }
    @Test public void mixedScanAndWarmCacheRetainBothKindsAndScope(){
        List<Station> all=repo.scanAll(countries,message->{},new AtomicBoolean());assertEquals(5,all.size());assertTrue(all.stream().anyMatch(s->s.kind.equals("tv")));assertTrue(all.stream().anyMatch(s->s.kind.equals("radio")));assertTrue(all.stream().allMatch(s->countries.contains(s.country)));
        CatalogRepository warm=new CatalogRepository(folder,fetch);assertEquals(5,warm.load().size());assertTrue(warm.matchesScope(countries));assertFalse(warm.matchesScope(Collections.singleton("PL")));assertEquals(fetch.modified,warm.dataAt());
        List<Station> tv=repo.scan(countries,false,null,new AtomicBoolean());assertTrue(tv.stream().allMatch(s->s.kind.equals("tv")));assertFalse(repo.matchesScope(countries));
        List<Station> radio=repo.scan(countries,true,null,new AtomicBoolean());assertTrue(radio.stream().allMatch(s->s.kind.equals("radio")));
    }
    @Test public void failedRefreshKeepsDatesAndOriginalPlaylists(){
        List<Station> first=repo.scanAll(countries,null,new AtomicBoolean());long before=repo.dataAt();List<CatalogRepository.FoundPlaylist> found=repo.playlists();assertEquals(3,found.size());CatalogRepository.FoundPlaylist free=found.stream().filter(p->p.source.contains("Free-TV")).findFirst().get();assertEquals(Arrays.asList("https://epg.example/pl.xml"),free.epgUrls());assertEquals(2,free.sourceCount);assertEquals(1,free.matchingCount);
        String original=free.text;fetch.offline=true;List<Station> offline=repo.scanAll(countries,null,new AtomicBoolean());assertEquals(first.size(),offline.size());assertEquals(before,repo.dataAt());assertTrue(repo.diagnostics().stream().anyMatch(s->s.contains("TRYB AWARYJNY")));assertEquals(original,repo.playlists().stream().filter(p->p.source.contains("Free-TV")).findFirst().get().text);
        List<Station> imported=repo.importM3u(original,free.source);Station ard=imported.stream().filter(s->s.name.equals("ARD")).findFirst().get();assertEquals("DasErste.de",ard.id);assertEquals("DE",ard.country);assertEquals(2,repo.importM3u(repo.exportM3u(imported),free.source).size());
    }
    @Test public void cancellingMixedScanDoesNotCommitPartialTv()throws Exception{
        repo.scanAll(countries,null,new AtomicBoolean());String original=CatalogRepository.readJson(new File(folder,"catalog.json"),1_000_000).toString();fetch.cancelRadio=true;AtomicBoolean cancel=new AtomicBoolean();boolean threw=false;try{repo.scanAll(countries,null,cancel);}catch(IllegalStateException e){threw=true;}assertTrue(threw);assertEquals(original,CatalogRepository.readJson(new File(folder,"catalog.json"),1_000_000).toString());
    }
    @Test public void invalidMetadataCannotReplaceValidFeed()throws Exception{
        repo.scan(countries,false,null,new AtomicBoolean());File metadata=new File(new File(folder,"feeds"),Station.sha256("channels")+".json");String original=CatalogRepository.readJson(metadata,1_000_000).optString("text");fetch.invalidMetadata=true;repo.scan(countries,false,null,new AtomicBoolean());assertEquals(original,CatalogRepository.readJson(metadata,1_000_000).optString("text"));assertTrue(repo.diagnostics().stream().anyMatch(s->s.contains("TRYB AWARYJNY")));
    }
    @Test public void oneExpiredAlternativeDoesNotEraseFreshSources(){
        long now=System.currentTimeMillis();Station old=station("Old","https://cdn.example/old.m3u8","PL");old.fetchedAt=now-8*CatalogRepository.DAY;Station fresh=station("Fresh","https://cdn.example/fresh.m3u8","PL");fresh.fetchedAt=now-1000;repo.save(Arrays.asList(old,fresh));CatalogRepository warm=new CatalogRepository(folder,fetch);List<Station> loaded=warm.load();assertEquals(1,loaded.size());assertEquals("Fresh",loaded.get(0).name);assertEquals(fresh.fetchedAt,warm.dataAt());
    }
    @Test public void refreshOfOneCountryRetainsOtherDownloadedCountriesAndUnknownImports(){
        long now=System.currentTimeMillis();Station foreign=station("Downloaded German","https://saved.example/de.m3u8","DE");foreign.fetchedAt=now-1000;
        Station imported=station("My imported list","https://saved.example/import.m3u8","");imported.fetchedAt=now-2000;
        Station expired=station("Expired","https://saved.example/expired.m3u8","DE");expired.fetchedAt=now-8*CatalogRepository.DAY;
        repo.save(Arrays.asList(foreign,imported,expired));List<Station> all=repo.scanAll(Collections.singleton("PL"),null,new AtomicBoolean());
        assertTrue(all.stream().anyMatch(s->s.url.equals(foreign.url)&&s.fetchedAt==foreign.fetchedAt));
        assertTrue(all.stream().anyMatch(s->s.url.equals(imported.url)&&s.country.isEmpty()&&s.fetchedAt==imported.fetchedAt));
        assertFalse("Retention cannot extend an expired feed",all.stream().anyMatch(s->s.url.equals(expired.url)));
        assertTrue(all.stream().anyMatch(s->s.country.equals("PL")));assertTrue(repo.matchesScope(Collections.singleton("PL")));
        assertTrue(repo.load().stream().anyMatch(s->s.url.equals(imported.url)));
    }
    @Test public void failedNewCountryDoesNotMarkRetainedForeignCatalogueAsItsScope()throws Exception{
        List<Station> first=repo.scanAll(countries,null,new AtomicBoolean());long before=repo.dataAt();
        File catalogue=new File(folder,"catalog.json");String saved=CatalogRepository.readJson(catalogue,1_000_000).toString();
        Set<String> missing=Collections.singleton("FR");fetch.offline=true;
        List<Station> retained=repo.scanAll(missing,null,new AtomicBoolean());
        assertEquals(first.size(),retained.size());assertTrue(retained.stream().noneMatch(s->s.country.equals("FR")));
        assertEquals("A failed scope must not refresh catalogue timestamps",saved,CatalogRepository.readJson(catalogue,1_000_000).toString());
        CatalogRepository warm=new CatalogRepository(folder,fetch);assertEquals(first.size(),warm.load().size());
        assertEquals(before,warm.dataAt());assertTrue(warm.matchesScope(countries));
        assertFalse("Startup must retry the country that yielded no sources",warm.matchesScope(missing));
        fetch.offline=false;Set<String> successful=Collections.singleton("DE");List<Station> refreshed=warm.scanAll(successful,null,new AtomicBoolean());
        assertTrue(refreshed.stream().anyMatch(s->s.country.equals("DE")));assertTrue(refreshed.stream().anyMatch(s->s.country.equals("PL")));
        assertTrue("An actual scoped result still updates the cache scope",warm.matchesScope(successful));
    }
    @Test public void sharedHomepageCannotCrossCountryAndNestedPagesAreFollowed()throws Exception{
        Station pl=station("Local Channel","https://shared.example/","PL"),de=station("Foreign Channel","https://shared.example/","DE");List<String> visits=new ArrayList<>();
        PublicHttp.Fetcher website=(url,cancel)->{visits.add(url);String body=url.contains("bing.com")?"<rss><channel/></rss>":url.endsWith(".m3u8")?"#EXTM3U\n#EXT-X-MEDIA-SEQUENCE:9\n#EXTINF:4,\nsegment.ts":"<title>Foreign Channel live</title><source src='https://cdn.example/live.m3u8'>";return new PublicHttp.Document(url,body,"text/html",System.currentTimeMillis(),0);};
        WebCrawler crawler=new WebCrawler(website,message->{},new File(folder,"visits-test.json"),null,(doc,all,matching)->{},()->true);assertTrue(crawler.search("",Collections.singleton("PL"),Arrays.asList(pl,de),null,new AtomicBoolean()).isEmpty());
        Station single=station("News","https://news.example/","PL");PublicHttp.Fetcher nested=(url,cancel)->new PublicHttp.Document(url,url.contains("bing.com")?"<rss><channel/></rss>":url.equals(single.url)?"<a href='/live'>Live</a>":url.endsWith("/live")?"<a href='/live/player'>Player</a>":url.endsWith("/live/player")?"<source src='https://cdn.example/live.m3u8'>":"#EXTM3U\n#EXT-X-MEDIA-SEQUENCE:5\n#EXTINF:4,\nsegment.ts","text/html",System.currentTimeMillis(),0);
        assertEquals(1,new WebCrawler(nested,message->{},new File(folder,"nested.json"),null,(doc,all,matching)->{},()->true).search("News",Collections.singleton("PL"),Arrays.asList(single),null,new AtomicBoolean()).size());
    }
    @Test public void learnedRankingPreservesScopeAndExplorationAndResetIsIsolated()throws Exception{
        List<Station> seeds=new ArrayList<>();for(int i=0;i<12;i++)seeds.add(station("Station "+i,"https://s"+i+".example/","PL"));
        JSONObject prior=new JSONObject();for(int i=0;i<8;i++)prior.put(seeds.get(i).url,System.currentTimeMillis());File history=new File(folder,"visits-a.json");CatalogRepository.writeJson(history,prior);
        CatalogRepository.Ranking prefer=list->Collections.sort(list,(a,b)->Integer.compare(Integer.parseInt(a.name.substring(8)),Integer.parseInt(b.name.substring(8))));
        WebCrawler crawler=new WebCrawler(fetch,message->{},history,prefer,(doc,all,matching)->{},()->true);List<Station> ordered=crawler.ordered(seeds);assertEquals(12,ordered.size());assertEquals(12,new HashSet<>(ordered.stream().map(s->s.url).collect(java.util.stream.Collectors.toList())).size());assertTrue(Integer.parseInt(ordered.get(3).name.substring(8))>=8);assertTrue(Integer.parseInt(ordered.get(7).name.substring(8))>=8);
        CatalogRepository.writeJson(new File(folder,"visits-b.json"),prior);repo.resetDiscoveryLearning("a");assertFalse(history.exists());assertTrue(new File(folder,"visits-b.json").exists());
    }
    @Test public void productionSizedDirectoryReachesLivePageWithoutPerHitFullScan()throws Exception{
        // Production contains about 31k broadcasters. The old per-hit scan
        // compiled hundreds of thousands of native ICU matchers for this case.
        StringBuilder json=new StringBuilder("[");
        for(int i=0;i<31751;i++){if(i>0)json.append(',');json.append("{\"id\":\"").append(i==0?"DW.de":"Station"+i+".us").append("\",\"name\":\"").append(i==0?"DW":"Station "+i).append("\",\"country\":\"").append(i%20==0?"DE":"US").append("\",\"website\":\"https://").append(i==0?"dw":"station"+i).append(".example/\",\"alt_names\":[\"Alias ").append(i).append("\"],\"categories\":[\"news\"],\"is_nsfw\":false}");}
        String directory=json.append(']').toString();StringBuilder xml=new StringBuilder("<rss><channel>");
        for(int i=0;i<10;i++)xml.append("<item><title>").append(i==9?"DW English live":"Unrelated station "+i).append("</title><link>https://").append(i==9?"news.dw":"unrelated"+i).append(".example/live</link></item>");String rss=xml.append("</channel></rss>").toString();
        List<String> visited=new ArrayList<>();PublicHttp.Fetcher pages=(url,cancel)->{visited.add(url);String body=url.contains("channels.json")?directory:url.contains("bing.com")?rss:url.endsWith(".m3u8")?"#EXTM3U\n#EXT-X-MEDIA-SEQUENCE:9\n#EXTINF:4,\nsegment.ts":"<title>DW English live</title><source src='https://cdn.example/live.m3u8'>";return new PublicHttp.Document(url,body,"text/plain",System.currentTimeMillis(),0);};
        long started=android.os.SystemClock.elapsedRealtime();CatalogRepository large=new CatalogRepository(new File(folder,"production-size"),pages);List<Station> found=large.searchWeb("DW English",Collections.singleton("DE"),null,new AtomicBoolean());long elapsed=android.os.SystemClock.elapsedRealtime()-started;
        assertEquals(1,found.size());assertEquals("DW.de",found.get(0).id);assertEquals("DE",found.get(0).country);assertTrue(visited.contains("https://news.dw.example/live"));assertTrue(visited.stream().filter(url->url.contains("bing.com")).count()>=2);assertTrue("31k-entry local search took "+elapsed+" ms",elapsed<30000);
        WebCrawler.BroadcasterIndex index=new WebCrawler.BroadcasterIndex(Arrays.asList(station("DW","https://dw.example/","DE"),station("Other","https://other.example/","PL")));assertEquals(1,index.matchingHost("https://news.dw.example/live").size());assertTrue(index.matchingHost("https://dw.example.evil.example/live").isEmpty());
    }
    static Station station(String name,String url,String country){Station s=new Station();s.name=name;s.url=url;s.homepage=url;s.country=country;s.id=name+"."+country.toLowerCase();s.fetchedAt=System.currentTimeMillis();return s;}
}
