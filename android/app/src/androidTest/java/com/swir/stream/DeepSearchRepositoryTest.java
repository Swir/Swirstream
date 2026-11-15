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
import java.util.List;
import java.util.UUID;
import java.util.concurrent.atomic.AtomicBoolean;
import static org.junit.Assert.*;

/** End-to-end repository regressions with independent, fallible public feeds. */
@RunWith(AndroidJUnit4.class)
public class DeepSearchRepositoryTest {
    private File folder;
    private Fixture fetch;
    private CatalogRepository repository;

    @Before public void setup() {
        folder=new File(InstrumentationRegistry.getInstrumentation().getTargetContext().getCacheDir(),"deep-test-"+UUID.randomUUID());
        assertTrue(folder.mkdirs());fetch=new Fixture();repository=new CatalogRepository(folder,fetch);
    }

    private static String playlist(String... rows) {
        StringBuilder body=new StringBuilder("#EXTM3U\n");
        for(int i=0;i<rows.length;i+=3)body.append("#EXTINF:-1 tvg-id=\"").append(rows[i]).append("\",").append(rows[i+1]).append('\n').append(rows[i+2]).append('\n');
        return body.toString();
    }

    private static class Fixture implements PublicHttp.Fetcher {
        final List<String> visited=new ArrayList<>();
        boolean failMetadata,failGlobal,cancelGlobal;
        String customDirectory,streams;
        final long fetchedAt=System.currentTimeMillis(),modifiedAt=fetchedAt-60_000;
        String global=playlist("TVP1.pl","TVP 1","https://global.example/tvp1.m3u8");
        String free=playlist("TVP1.pl","TVP1 (720p)","https://free.example/tvp1.m3u8");
        @Override public PublicHttp.Document fetch(String url,AtomicBoolean cancelled)throws IOException {
            visited.add(url);PublicHttp.checkCancelled(cancelled);String body;
            if(url.contains("channels.json")) {
                if(failMetadata)throw new IOException("Metadata unavailable");
                if(customDirectory!=null)body=customDirectory;else try {
                    JSONArray channels=new JSONArray();
                    for(int i=0;i<12;i++)channels.put(new JSONObject()
                        .put("id",i==0?"TVP1.pl":i==1?"DW.de":"Fixture"+i+".pl")
                        .put("name",i==0?"TVP1":i==1?"DW":"Fixture "+i)
                        .put("country",i==1?"DE":"PL")
                        .put("website",i==1?"https://dw.example/":"https://tvp"+i+".example/")
                        .put("alt_names",new JSONArray()).put("is_nsfw",false));
                    body=channels.toString();
                }catch(Exception e){throw new IOException(e);}
            }else if(url.endsWith("/streams.json")&&streams!=null)body=streams;
            else if(url.contains("Free-TV/"))body=free;
            else if(url.endsWith("/index.m3u")) {
                if(cancelGlobal){cancelled.set(true);PublicHttp.checkCancelled(cancelled);}
                if(failGlobal)throw new IOException("Global source and mirror unavailable");
                body=global;
            }else if(url.contains("bing.com/"))body="<rss><channel/></rss>";
            else throw new IOException("Website/search unavailable: "+url);
            return new PublicHttp.Document(url,body,"text/plain",fetchedAt,modifiedAt);
        }
    }

    @Test public void bothGlobalFeedsAreReadBeforeWebsitesAndSurviveSearchOutage() {
        List<Station> found=repository.searchWeb("TVP 1",Collections.singleton("PL"),null,new AtomicBoolean());
        assertEquals(2,found.size());
        assertTrue(found.stream().anyMatch(s->s.url.equals("https://global.example/tvp1.m3u8")));
        assertTrue(found.stream().anyMatch(s->s.url.equals("https://free.example/tvp1.m3u8")));
        int global=-1,free=-1,firstWeb=Integer.MAX_VALUE;
        for(int i=0;i<fetch.visited.size();i++) {
            String url=fetch.visited.get(i);
            if(url.endsWith("/index.m3u"))global=i;
            else if(url.contains("Free-TV/"))free=i;
            else if(!url.contains("channels.json"))firstWeb=Math.min(firstWeb,i);
        }
        assertTrue("Global list must precede web search",global>=0&&global<firstWeb);
        assertTrue("Independent Free-TV list must precede web search",free>=0&&free<firstWeb);
    }

    @Test public void missingMetadataDoesNotDisableIdentifiedPlaylistSources() {
        fetch.failMetadata=true;
        List<Station> found=repository.searchWeb("TVP 1",Collections.singleton("PL"),null,new AtomicBoolean());
        assertEquals(2,found.size());
        assertTrue(found.stream().allMatch(s->"PL".equals(s.country)&&"TVP1.pl".equals(s.tvgId)));
    }

    @Test public void failedGlobalProviderAndMirrorDoNotEraseIndependentResults() {
        fetch.failGlobal=true;
        List<Station> found=repository.searchWeb("TVP 1",Collections.singleton("PL"),null,new AtomicBoolean());
        assertEquals(1,found.size());assertEquals("https://free.example/tvp1.m3u8",found.get(0).url);
        assertTrue("Both global endpoints attempted",fetch.visited.stream().filter(url->url.endsWith("/index.m3u")).count()>=2);
    }

    @Test public void cancelledDeepSearchDoesNotReplaceSavedCatalogue()throws Exception {
        Station previous=new Station();previous.name="Saved";previous.country="PL";previous.id="Saved.pl";
        previous.url="https://saved.example/live.m3u8";previous.fetchedAt=System.currentTimeMillis();
        repository.save(Collections.singletonList(previous));
        File catalogue=new File(folder,"catalog.json");String before=CatalogRepository.readJson(catalogue,1_000_000).toString();
        fetch.cancelGlobal=true;AtomicBoolean cancelled=new AtomicBoolean();
        try{repository.searchWeb("TVP 1",Collections.singleton("PL"),null,cancelled);fail("Cancellation must be propagated");}
        catch(IllegalStateException expected){assertTrue(cancelled.get());}
        assertEquals(before,CatalogRepository.readJson(catalogue,1_000_000).toString());
    }

    @Test public void legacyAndFeedLanguageNamesSurviveDirectoryEnrichment() {
        fetch.global=playlist("DWEnglish.de","DW","https://global.example/english.m3u8",
            "DWGerman.de","DW","https://global.example/german.m3u8");
        fetch.free=playlist("DW.de@English","DW","https://free.example/english.m3u8",
            "DW.de@German","DW","https://free.example/german.m3u8");
        List<Station> found=repository.searchWeb("DW English",Collections.singleton("DE"),null,new AtomicBoolean());
        assertEquals("Legacy ID and modern feed variant should both be searchable",2,found.size());
        assertTrue(found.stream().allMatch(s->s.url.endsWith("/english.m3u8")&&s.country.equals("DE")));
        assertFalse("Distinct language feed must not enter English results",found.stream().anyMatch(s->s.url.contains("german")));
    }

    @Test public void numericNamesAndCountryScopeRemainStrictAcrossGlobalLists() {
        fetch.global=playlist("TVP1.pl","TVP 1","https://global.example/pl1.m3u8",
            "TVP10.pl","TVP 10","https://global.example/pl10.m3u8",
            "TVP1.de","TVP 1","https://global.example/de1.m3u8");
        fetch.free=playlist("TVP10.pl","TVP10","https://free.example/pl10.m3u8",
            "TVP1.de","TVP1","https://free.example/de1.m3u8");
        List<Station> found=repository.searchWeb("TVP 1",Collections.singleton("PL"),null,new AtomicBoolean());
        assertEquals(1,found.size());assertEquals("https://global.example/pl1.m3u8",found.get(0).url);
        assertEquals("PL",found.get(0).country);
    }

    private static Station probeStation(String path) {
        Station station=new Station();station.name=path;station.id=path+".pl";station.country="PL";
        station.url="https://probe.example/"+path+".m3u8";
        station.fetchedAt=System.currentTimeMillis()-60_000;station.modifiedAt=station.fetchedAt-60_000;
        return station;
    }

    @Test public void htmlVodAndFailedRequestsNeverConfirmLiveOrRemoveCandidates()throws Exception {
        Station html=probeStation("html"),vod=probeStation("vod"),offline=probeStation("offline");
        long originalFetch=html.fetchedAt,originalModified=html.modifiedAt;
        List<Station> sources=new ArrayList<>(Arrays.asList(html,vod,offline));
        PublicHttp.Fetcher documents=(url,cancel)->{
            if(url.contains("offline"))throw new IOException("HTTP 503");
            String text=url.contains("html")?"<html><body>200 OK login required</body></html>":
                "#EXTM3U\n#EXT-X-PLAYLIST-TYPE:VOD\n#EXT-X-MEDIA-SEQUENCE:9\n#EXTINF:4,\nsegment.ts\n#EXT-X-ENDLIST\n";
            return new PublicHttp.Document(url,text,"text/plain",System.currentTimeMillis(),0);
        };
        new SourceProbe(documents,message->{}).check(sources,null,new AtomicBoolean());
        assertEquals("Failed candidates remain available for a player attempt",3,sources.size());
        for(Station station:sources){assertFalse(station.liveManifest);assertFalse(station.freshManifest());assertTrue(station.manifestCheckedAt>0);}
        assertEquals("Probing must not refresh feed age",originalFetch,html.fetchedAt);
        assertEquals(originalModified,html.modifiedAt);
    }

    @Test public void liveVariantRetainsHeadersAndOnlyConfirmsManifestAvailability()throws Exception {
        Station source=probeStation("master");source.userAgent="SwirProbeFixture/1";source.referrer="https://broadcaster.example/live";
        List<String> requests=new ArrayList<>();
        PublicHttp.Fetcher documents=new PublicHttp.Fetcher(){
            @Override public PublicHttp.Document fetch(String url,AtomicBoolean cancelled)throws IOException {
                throw new IOException("Required playback headers were omitted");
            }
            @Override public PublicHttp.Document fetch(String url,AtomicBoolean cancelled,String agent,String referrer)throws IOException {
                assertEquals(source.userAgent,agent);assertEquals(source.referrer,referrer);requests.add(url);
                String text=url.endsWith("/master.m3u8")?"#EXTM3U\n#EXT-X-STREAM-INF:BANDWIDTH=800000\nvariants/live.m3u8\n":
                    "#EXTM3U\n#EXT-X-TARGETDURATION:4\n#EXT-X-MEDIA-SEQUENCE:9\n#EXTINF:4,\nsegment.ts\n";
                return new PublicHttp.Document(url,text,"application/vnd.apple.mpegurl",System.currentTimeMillis(),0);
            }
        };
        new SourceProbe(documents,message->{}).check(new ArrayList<>(Collections.singletonList(source)),null,new AtomicBoolean());
        assertEquals(Arrays.asList("https://probe.example/master.m3u8","https://probe.example/variants/live.m3u8"),requests);
        assertTrue(source.liveManifest);assertTrue(source.freshManifest());assertTrue(source.manifestCheckedAt>0);
        assertTrue("Manifest availability must not claim decoded video",source.availability().contains("obraz do sprawdzenia"));
        Station saved=Station.fromJson(source.toJson());assertTrue(saved.freshManifest());
        saved.manifestCheckedAt=System.currentTimeMillis()-31*60*1000L;
        assertFalse("Manifest checks expire independently of catalogue freshness",saved.freshManifest());
    }

    @Test public void probeCancellationStopsBeforeNextCandidateAndKeepsTheList()throws Exception {
        Station first=probeStation("first"),second=probeStation("second");
        List<Station> sources=new ArrayList<>(Arrays.asList(first,second));List<String> requests=new ArrayList<>();
        AtomicBoolean cancelled=new AtomicBoolean();
        PublicHttp.Fetcher documents=(url,cancel)->{requests.add(url);cancel.set(true);return new PublicHttp.Document(url,
            "#EXTM3U\n#EXT-X-MEDIA-SEQUENCE:9\n#EXTINF:4,\nsegment.ts\n","text/plain",System.currentTimeMillis(),0);};
        try{new SourceProbe(documents,message->{}).check(sources,null,cancelled);fail("Cancellation must propagate");}
        catch(IOException expected){assertTrue(cancelled.get());}
        assertEquals(Collections.singletonList(first.url),requests);assertEquals(2,sources.size());
        assertEquals("Interrupted check must not be reported as completed",0L,first.manifestCheckedAt);
        assertEquals(0L,second.manifestCheckedAt);assertFalse(first.freshManifest());assertFalse(second.freshManifest());
    }

    @Test public void structuredIndexAddsMissingAlternativeWithHeadersAndStrictCountry()throws Exception {
        fetch.streams=new JSONArray()
            .put(new JSONObject().put("channel","TVP1.pl").put("feed","HD").put("title","TVP 1")
                .put("url","https://index.example/additional.m3u8").put("user_agent","RequiredAgent/1")
                .put("referrer","https://broadcaster.example/live"))
            .put(new JSONObject().put("channel","DW.de").put("title","TVP 1").put("url","https://index.example/foreign.m3u8"))
            .put(new JSONObject().put("channel","Unknown.pl").put("title","TVP 1").put("url","https://index.example/unknown.m3u8"))
            .toString();
        List<Station> found=repository.searchWeb("TVP 1",Collections.singleton("PL"),null,new AtomicBoolean());
        assertEquals("Two playlist addresses plus one independently indexed address",3,found.size());
        Station extra=found.stream().filter(s->s.url.equals("https://index.example/additional.m3u8")).findFirst().get();
        assertEquals("TVP1.pl",extra.id);assertEquals("TVP1.pl",extra.tvgId);assertEquals("HD",extra.feed);
        assertEquals("PL",extra.country);assertEquals("RequiredAgent/1",extra.userAgent);
        assertEquals("https://broadcaster.example/live",extra.referrer);assertEquals("https://tvp0.example/",extra.homepage);
        assertEquals("https://iptv-org.github.io/api/streams.json",extra.origin);
        assertEquals(fetch.fetchedAt,extra.fetchedAt);assertEquals(fetch.modifiedAt,extra.modifiedAt);
        assertFalse(found.stream().anyMatch(s->s.url.contains("foreign")||s.url.contains("unknown")));
    }

    private static JSONObject directoryRow(String id,String name,String country,String successor)throws Exception {
        JSONObject row=new JSONObject().put("id",id).put("name",name).put("country",country)
            .put("alt_names",new JSONArray()).put("is_nsfw",false);
        if(successor!=null)row.put("replaced_by",successor);return row;
    }

    @Test public void renamedChannelChainIsSearchableWithoutCyclesOrCrossCountryAliases()throws Exception {
        JSONArray rows=new JSONArray()
            .put(directoryRow("Old.pl","Stara Marka","PL","Middle.pl"))
            .put(directoryRow("Middle.pl","Pośrednia Marka","PL","Current.pl"))
            .put(directoryRow("Current.pl","Nowa Marka","PL",null))
            .put(directoryRow("CycleA.pl","Pętla Alfa","PL","CycleB.pl"))
            .put(directoryRow("CycleB.pl","Pętla Beta","PL","CycleA.pl"))
            .put(directoryRow("Cross.pl","Obca Stara Marka","PL","Foreign.de"))
            .put(directoryRow("Foreign.de","Zagraniczna Marka","DE",null));
        for(int i=0;i<3;i++)rows.put(directoryRow("Filler"+i+".pl","Wypełnienie "+i,"PL",null));
        fetch.customDirectory=rows.toString();
        fetch.global=playlist("Current.pl","Nowa Marka","https://global.example/current.m3u8");
        fetch.free=playlist("Unrelated.pl","Inny Kanał","https://free.example/other.m3u8");
        List<Station> found=repository.searchWeb("Stara Marka",Collections.singleton("PL"),null,new AtomicBoolean());
        assertEquals(1,found.size());assertEquals("Current.pl",found.get(0).id);
        assertTrue(M3u.matches("Pośrednia Marka",found.get(0)));
        List<Station> directory=repository.metadata(false,new AtomicBoolean());
        Station cycleA=directory.stream().filter(s->s.id.equals("CycleA.pl")).findFirst().get();
        Station cycleB=directory.stream().filter(s->s.id.equals("CycleB.pl")).findFirst().get();
        Station foreign=directory.stream().filter(s->s.id.equals("Foreign.de")).findFirst().get();
        assertFalse(M3u.matches("Pętla Beta",cycleA));assertFalse(M3u.matches("Pętla Alfa",cycleB));
        assertFalse(M3u.matches("Obca Stara Marka",foreign));
    }

    @Test public void invalidStructuredIndexCannotReplaceValidCacheOrRefreshItsDate()throws Exception {
        fetch.streams="[{\"channel\":\"TVP1.pl\",\"title\":\"TVP 1\",\"url\":\"https://index.example/live.m3u8\"}]";
        String[] urls={"https://iptv-org.github.io/api/streams.json","https://raw.githubusercontent.com/iptv-org/api/gh-pages/streams.json"};
        PublicHttp.Document original=repository.feed("streams",urls,true,new AtomicBoolean());
        File cache=new File(new File(folder,"feeds"),Station.sha256("streams")+".json");
        String saved=CatalogRepository.readJson(cache,1_000_000).toString();
        fetch.streams="[{\"channel\":\"TVP1.pl\",\"unexpected_field\":\"schema changed\"}]";
        PublicHttp.Document fallback=repository.feed("streams",urls,true,new AtomicBoolean());
        assertEquals(original.text,fallback.text);assertEquals(original.fetchedAt,fallback.fetchedAt);
        assertEquals(original.modifiedAt,fallback.modifiedAt);assertEquals(saved,CatalogRepository.readJson(cache,1_000_000).toString());
        assertTrue(repository.diagnostics().stream().anyMatch(message->message.contains("TRYB AWARYJNY")));
        assertEquals("Initial download plus both failed refresh endpoints",3,fetch.visited.stream().filter(url->url.endsWith("/streams.json")).count());
    }

    @Test public void duplicateSourcesKeepPlaybackHeadersTogetherWithTheirVerification() {
        long now=System.currentTimeMillis();
        Station bare=probeStation("duplicate"),indexed=probeStation("duplicate");
        bare.userAgent="ExistingAgent";bare.manifestCheckedAt=now-1000;bare.liveManifest=false;
        indexed.userAgent="OtherAgent";indexed.referrer="https://indexed.example/live";
        Station merged=CatalogRepository.deduplicate(Arrays.asList(bare,indexed)).get(0);
        assertEquals("ExistingAgent",merged.userAgent);assertEquals(indexed.referrer,merged.referrer);
        assertFalse("Changed request headers need a fresh probe",merged.liveManifest);assertFalse(merged.freshManifest());

        Station old=probeStation("verified"),verified=probeStation("verified");
        old.userAgent="StaleAgent";old.referrer="https://old.example/live";
        verified.userAgent="";verified.referrer="https://verified.example/live";
        verified.manifestCheckedAt=now;verified.liveManifest=true;
        merged=CatalogRepository.deduplicate(Arrays.asList(old,verified)).get(0);
        assertEquals("An empty verified agent is part of the verified request too","",merged.userAgent);
        assertEquals(verified.referrer,merged.referrer);assertEquals(verified.manifestCheckedAt,merged.manifestCheckedAt);
        assertTrue(merged.freshManifest());

        Station confirmed=probeStation("confirmed"),unconfirmed=probeStation("confirmed");
        confirmed.userAgent="ConfirmedAgent";confirmed.referrer="https://confirmed.example/live";
        confirmed.manifestCheckedAt=now;confirmed.liveManifest=true;
        unconfirmed.userAgent="UnverifiedAgent";unconfirmed.referrer="https://other.example/live";
        unconfirmed.manifestCheckedAt=now+1;unconfirmed.liveManifest=false;
        merged=CatalogRepository.deduplicate(Arrays.asList(confirmed,unconfirmed)).get(0);
        assertEquals("ConfirmedAgent",merged.userAgent);assertEquals("https://confirmed.example/live",merged.referrer);
        assertEquals(now,merged.manifestCheckedAt);assertTrue(merged.freshManifest());
    }

    @Test public void renamedChannelFieldWithValidUrlsCannotPoisonStructuredCache()throws Exception {
        // Real indexes contain some null channel IDs. They are permitted as long
        // as the feed also contains usable records with the supported identity field.
        fetch.streams="[{\"channel\":\"TVP1.pl\",\"url\":\"https://index.example/live.m3u8\"},"
            +"{\"channel\":null,\"url\":\"https://index.example/unidentified.m3u8\"}]";
        String[] urls={"https://iptv-org.github.io/api/streams.json","https://raw.githubusercontent.com/iptv-org/api/gh-pages/streams.json"};
        PublicHttp.Document original=repository.feed("streams",urls,true,new AtomicBoolean());
        File cache=new File(new File(folder,"feeds"),Station.sha256("streams")+".json");
        String saved=CatalogRepository.readJson(cache,1_000_000).toString();
        fetch.streams="[{\"channel_id\":\"TVP1.pl\",\"url\":\"https://index.example/live.m3u8\"},"
            +"{\"channel\":null,\"url\":\"https://index.example/unidentified.m3u8\"}]";
        PublicHttp.Document fallback=repository.feed("streams",urls,true,new AtomicBoolean());
        assertEquals("Public URLs alone do not validate the stream-index schema",original.text,fallback.text);
        assertEquals(original.fetchedAt,fallback.fetchedAt);assertEquals(original.modifiedAt,fallback.modifiedAt);
        assertEquals(saved,CatalogRepository.readJson(cache,1_000_000).toString());
        assertTrue(repository.diagnostics().stream().anyMatch(message->message.contains("TRYB AWARYJNY")));
    }
}
