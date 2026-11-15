package com.swir.stream;

import android.content.*;
import android.graphics.SurfaceTexture;
import android.view.Surface;
import androidx.test.ext.junit.runners.AndroidJUnit4;
import androidx.test.platform.app.InstrumentationRegistry;
import androidx.media3.common.*;
import androidx.media3.datasource.DefaultHttpDataSource;
import androidx.media3.exoplayer.ExoPlayer;
import androidx.media3.exoplayer.source.DefaultMediaSourceFactory;
import org.json.*;
import org.junit.*;
import org.junit.runner.RunWith;
import static org.junit.Assert.*;
import java.io.*;
import java.nio.charset.StandardCharsets;
import java.util.*;
import java.util.concurrent.*;
import java.util.concurrent.atomic.*;

/** Explicit opt-in tests against the exact production endpoints used by the APK. */
@RunWith(AndroidJUnit4.class)
public class NetworkSmokeTest {
    Context context; File folder;
    @Before public void setup() {
        Assume.assumeTrue("true".equals(InstrumentationRegistry.getArguments().getString("network")));
        Context base=InstrumentationRegistry.getInstrumentation().getTargetContext();
        folder=new File(base.getCacheDir(),"network-smoke");folder.mkdirs();
        context=new ContextWrapper(base){@Override public File getFilesDir(){return folder;} @Override public android.content.SharedPreferences getSharedPreferences(String name,int mode){return super.getSharedPreferences("network-smoke-"+name,mode);}};
    }
    private void write(String name,JSONObject value)throws Exception {
        byte[] bytes=value.toString(2).getBytes(StandardCharsets.UTF_8);
        try(FileOutputStream out=new FileOutputStream(new File(folder,name))){out.write(bytes);}
        File evidence=context.getExternalFilesDir("evidence");if(evidence!=null){evidence.mkdirs();try(FileOutputStream out=new FileOutputStream(new File(evidence,name))){out.write(bytes);}}
    }
    @Test public void actualCataloguesAndCurrentXmltv() throws Exception {
        CatalogRepository repo=new CatalogRepository(context); List<String> updates=new ArrayList<>();
        Set<String> countries=new LinkedHashSet<>(Arrays.asList("PL","DE")); AtomicBoolean cancelled=new AtomicBoolean();
        List<Station> tv=repo.scan(countries,false,updates::add,cancelled);
        List<String> tvDiagnostics=new ArrayList<>(repo.diagnostics());
        List<Station> radio=repo.scan(countries,true,updates::add,cancelled);
        EpgRepository epg=new EpgRepository(context);epg.refresh(Collections.singleton("PL"),tv,updates::add,cancelled);
        Station schedule=new Station();schedule.country="PL";schedule.name="TVN";schedule.tvgId="TVN.pl";
        JSONObject evidence=new JSONObject().put("at",System.currentTimeMillis()).put("tvSources",tv.size()).put("radioSources",radio.size()).put("catalogDataAt",repo.dataAt()).put("tvDiagnostics",new JSONArray(tvDiagnostics)).put("radioDiagnostics",new JSONArray(repo.diagnostics())).put("foundPlaylists",repo.playlists().size()).put("epgDataAt",epg.dataAt()).put("epgNotice",epg.notice()).put("tvnScheduleOnly",epg.now(schedule)).put("updates",new JSONArray(updates));
        write("production-network.json",evidence);
        int localNames=0;for(Station saved:tv){assertTrue("Saved TV display name: "+saved.name,LocalChannelSearch.prepare(saved.name).matches(saved));localNames++;}
        for(Station saved:radio){assertTrue("Saved radio display name: "+saved.name,LocalChannelSearch.prepare(saved.name).matches(saved));localNames++;}
        evidence.put("downloadedNamesMatched",localNames);write("production-network.json",evidence);
        assertTrue("TV actual source count "+tv.size(),tv.size()>20);assertTrue("Radio actual source count "+radio.size(),radio.size()>5);
        assertTrue("PL+DE filter",tv.stream().allMatch(s->countries.contains(s.country)));assertTrue("Stored original playlists",repo.playlists().size()>0);
        assertTrue("EPG publisher freshness",epg.dataAt()>System.currentTimeMillis()-48*EpgRepository.HOUR);assertFalse("Actual TVN schedule (not a playback assertion)",epg.now(schedule).contains("brak danych"));
    }
    @Test public void realPublicLiveStreamDecoded() throws Exception {
        CatalogRepository repo=new CatalogRepository(context);List<Station> stations=repo.scan(Collections.singleton("DE"),false,message->{},new AtomicBoolean());
        List<Station> candidates=new ArrayList<>();
        for(Station s:stations) if(s.name.toLowerCase(Locale.ROOT).contains("dw english") || s.name.toLowerCase(Locale.ROOT).startsWith("das erste"))candidates.add(s);
        assertFalse("Actual public broadcaster candidates",candidates.isEmpty());
        JSONArray attempts=new JSONArray();boolean decoded=false;
        for(Station station:candidates.subList(0,Math.min(5,candidates.size()))) {
            AtomicReference<ExoPlayer> player=new AtomicReference<>(); AtomicReference<SurfaceTexture> texture=new AtomicReference<>();AtomicReference<Surface> surface=new AtomicReference<>();
            AtomicReference<String> failure=new AtomicReference<>("");AtomicInteger frames=new AtomicInteger();AtomicLong lastPts=new AtomicLong(Long.MIN_VALUE);CountDownLatch done=new CountDownLatch(1);long start=System.currentTimeMillis();
            InstrumentationRegistry.getInstrumentation().runOnMainSync(()->{
                SurfaceTexture t=new SurfaceTexture(0);t.setDefaultBufferSize(64,64);texture.set(t);Surface output=new Surface(t);surface.set(output);
                DefaultHttpDataSource.Factory http=new DefaultHttpDataSource.Factory().setUserAgent(station.userAgent.isEmpty()?"SwirStreamAndroid/0.2.1":station.userAgent).setConnectTimeoutMs(8000).setReadTimeoutMs(10000).setAllowCrossProtocolRedirects(true);
                if(!station.referrer.isEmpty())http.setDefaultRequestProperties(Collections.singletonMap("Referer",station.referrer));
                ExoPlayer p=new ExoPlayer.Builder(context).setMediaSourceFactory(new DefaultMediaSourceFactory(http)).build();player.set(p);p.setVideoSurface(output);p.setVolume(0);
                p.setTrackSelectionParameters(p.getTrackSelectionParameters().buildUpon().setMaxVideoSize(1280,720).setMaxVideoBitrate(3500000).build());
                p.setVideoFrameMetadataListener((pts,release,format,mediaFormat)->{if(lastPts.getAndSet(pts)!=pts&&frames.incrementAndGet()>=3)done.countDown();});
                p.addListener(new Player.Listener(){@Override public void onPlayerError(PlaybackException error){failure.set(error.getErrorCodeName());done.countDown();}});
                p.setMediaItem(MediaItem.fromUri(station.url));p.prepare();p.play();
            });
            try{done.await(25,TimeUnit.SECONDS);}finally{InstrumentationRegistry.getInstrumentation().runOnMainSync(()->{if(player.get()!=null)player.get().release();if(surface.get()!=null)surface.get().release();if(texture.get()!=null)texture.get().release();});}
            boolean ok=frames.get()>=3&&failure.get().isEmpty();attempts.put(new JSONObject().put("name",station.name).put("url",station.url).put("advancingVideoFrames",frames.get()).put("elapsedMs",System.currentTimeMillis()-start).put("error",failure.get()).put("passed",ok));
            if(ok){decoded=true;break;}
        }
        write("production-playback.json",new JSONObject().put("at",System.currentTimeMillis()).put("attempts",attempts).put("decoded",decoded));
        assertTrue("At least one actual live source must decode advancing video frames",decoded);
    }
    @Test public void realBroadcasterWebsiteYieldsLiveSource() throws Exception {
        CatalogRepository repo=new CatalogRepository(context);List<String> progress=new ArrayList<>();long started=android.os.SystemClock.elapsedRealtime();
        List<Station> result=repo.searchWeb("DW English",Collections.singleton("DE"),progress::add,new AtomicBoolean());
        JSONArray stations=new JSONArray();for(Station s:result)stations.put(s.toJson());
        write("production-web.json",new JSONObject().put("at",System.currentTimeMillis()).put("elapsedMs",android.os.SystemClock.elapsedRealtime()-started).put("processPssKb",android.os.Debug.getPss()).put("sources",stations).put("diagnostics",new JSONArray(repo.diagnostics())).put("progress",new JSONArray(progress)));
        assertTrue("Public broadcaster HTML must yield at least one actual live manifest",result.size()>0);
        assertTrue(result.stream().allMatch(s->"DE".equals(s.country)&&PublicHttp.allowedStream(s.url)));
    }
    @Test public void deepSearchAddsSourcesBeyondCountryCatalogues()throws Exception{
        CatalogRepository repo=new CatalogRepository(context);Set<String> countries=Collections.singleton("DE");
        List<Station> baseline=repo.scan(countries,false,null,new AtomicBoolean());Set<String> existing=new HashSet<>();for(Station s:baseline)existing.add(s.sourceKey());
        JSONArray cases=new JSONArray();int additional=0,manifests=0;
        for(String query:new String[]{"DW English","Altenburg TV"}){
            long started=System.currentTimeMillis();List<Station> found=repo.searchWeb(query,countries,null,new AtomicBoolean());int added=0,ready=0;JSONArray rows=new JSONArray();
            for(Station s:found){if(!existing.contains(s.sourceKey()))added++;if(s.freshManifest())ready++;rows.put(s.toJson());}
            additional+=added;manifests+=ready;cases.put(new JSONObject().put("query",query).put("elapsedMs",System.currentTimeMillis()-started).put("sources",rows).put("additionalToCountryCatalog",added).put("liveManifests",ready).put("diagnostics",new JSONArray(repo.diagnostics())));
            assertTrue(query+" keeps country",found.stream().allMatch(s->"DE".equals(s.country)));
        }
        write("production-deep-search.json",new JSONObject().put("at",System.currentTimeMillis()).put("baselineSources",baseline.size()).put("cases",cases).put("additionalSources",additional).put("liveManifests",manifests));
        assertTrue("Real deep search must add candidates beyond current country and Free-TV lists",additional>0);
        assertTrue("At least one current live manifest must be checked",manifests>0);
    }
}
