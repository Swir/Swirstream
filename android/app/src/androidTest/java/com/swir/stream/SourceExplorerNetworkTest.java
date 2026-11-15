package com.swir.stream;

import android.content.Context;
import android.os.SystemClock;
import androidx.media3.ui.PlayerView;
import androidx.test.core.app.ActivityScenario;
import androidx.test.ext.junit.runners.AndroidJUnit4;
import androidx.test.platform.app.InstrumentationRegistry;
import org.json.*;
import org.junit.*;
import org.junit.runner.RunWith;
import static org.junit.Assert.*;
import java.io.*;
import java.lang.reflect.Field;
import java.nio.charset.StandardCharsets;
import java.util.*;
import java.util.concurrent.atomic.*;

@androidx.media3.common.util.UnstableApi @RunWith(AndroidJUnit4.class)
public final class SourceExplorerNetworkTest {
    private Context context(){return InstrumentationRegistry.getInstrumentation().getTargetContext();}
    private static final String PORTAL="http://middleware-stalker.com/stalker_portal/c/",PLAYLIST="https://iptv-org.github.io/iptv/countries/pl.m3u",DASH="https://storage.googleapis.com/shaka-demo-assets/angel-one/dash.mpd";
    private void enabled(){assertEquals("true",InstrumentationRegistry.getArguments().getString("network"));}
    @After public void restore()throws Exception{UiTestData.restore(context());}
    @Test public void publishedPortalAndPlaylistUseOnlyPublicTransportAndSearchReportsOutcome()throws Exception{
        enabled();SourceExplorerHttp transport=new SourceExplorerHttp(context());List<String> requests=new ArrayList<>();SourceExplorer engine=new SourceExplorer((url,cancel)->{requests.add(url);return transport.fetch(url,cancel);});
        SourceCandidate portal=engine.analyze(PORTAL,null,new AtomicBoolean()).candidates.get(0);assertEquals(SourceExplorerRules.Kind.PortalCandidate,portal.kind);assertEquals(0,portal.channelCount);assertTrue(portal.checkedAt>0);assertEquals("Only the published front page, no scripts or protocol",1,requests.size());assertEquals(PORTAL,requests.get(0));
        SourceCandidate playlist=engine.analyze(PLAYLIST,null,new AtomicBoolean()).candidates.get(0);assertEquals(SourceExplorerRules.Kind.PublicPlaylist,playlist.kind);assertTrue(playlist.channelCount>10);
        int before=requests.size();String searchOutcome;int found=0;JSONArray searchResults=new JSONArray();try{SourceExplorer.Scan scan=engine.search("iptv-org",Collections.emptySet(),null,new AtomicBoolean());found=scan.candidates.size();assertTrue("Real search must follow a published public playlist",scan.candidates.stream().anyMatch(row->row.kind==SourceExplorerRules.Kind.PublicPlaylist));for(SourceCandidate result:scan.candidates)searchResults.put(result.json());searchOutcome="completed";}catch(IOException handled){searchOutcome="unavailable-preserved";}assertTrue("Search made real public requests",requests.size()>before);for(String url:requests)assertTrue(SourceExplorerRules.allowedPage(url));
        write("explorer-public-network.json",new JSONObject().put("verifiedAt",System.currentTimeMillis()).put("sdk",android.os.Build.VERSION.SDK_INT).put("portal",portal.json()).put("portalFrontPageRequests",1).put("portalAuthenticationRequests",0).put("playlist",playlist.json()).put("webSearchQuery","iptv-org").put("webSearchOutcome",searchOutcome).put("webSearchRequests",requests.size()-before).put("webSearchCandidates",found).put("webSearchResults",searchResults).put("searchEngineUrls",new JSONArray(requests.subList(before,requests.size()).stream().filter(u->u.startsWith("https://www.bing.com/search")||u.startsWith("https://html.duckduckgo.com/html/")).collect(java.util.stream.Collectors.toList()))));
    }
    @Test public void discoveredClearDashImportsEncryptedAndPlaysInsideSwirPlayer()throws Exception{
        enabled();SourceCandidate candidate=new SourceExplorer(context()).analyze(DASH,null,new AtomicBoolean()).candidates.get(0);assertEquals(SourceExplorerRules.Kind.DirectStream,candidate.kind);assertFalse(candidate.protectedMedia);String profile=UUID.randomUUID().toString().replace("-","");ProviderRepository repository=new ProviderRepository(context(),profile);AtomicBoolean confirmed=new AtomicBoolean();AtomicLong watched=new AtomicLong();AtomicReference<String> failure=new AtomicReference<>("");String source="";
        try{source=repository.addDirect("Oficjalna próbka Shaka",candidate.url,"",new AtomicBoolean()).id;Station reference=repository.stations().get(0);assertTrue(reference.url.startsWith("swir://provider/"));Station resolved=repository.resolve(reference,new AtomicBoolean());UiTestData.seed(context());try(ActivityScenario<MainActivity> scene=ActivityScenario.launch(MainActivity.class)){AtomicBoolean ready=new AtomicBoolean();long deadline=SystemClock.elapsedRealtime()+15000;while(!ready.get()&&SystemClock.elapsedRealtime()<deadline){scene.onActivity(a->ready.set(a.getWindow().getDecorView().findViewWithTag("swir_player")!=null));SystemClock.sleep(50);}assertTrue(ready.get());scene.onActivity(a->{PlayerView view=a.getWindow().getDecorView().findViewWithTag("swir_player");SwirPlayer player=new SwirPlayer(a,view,resolved,"Oszczędzaj transfer",new SwirPlayer.Listener(){public void state(String text){}public void confirmed(Station s,long ms){confirmed.set(true);}public void watchedThirtySeconds(Station s){}public void failed(Station s,String text,long ms){failure.set(text);}});try{Field field=MainActivity.class.getDeclaredField("player");field.setAccessible(true);field.set(a,player);}catch(Exception error){throw new AssertionError(error);}});deadline=SystemClock.elapsedRealtime()+25000;while((!confirmed.get()||watched.get()<1500)&&failure.get().isEmpty()&&SystemClock.elapsedRealtime()<deadline){SystemClock.sleep(100);scene.onActivity(a->{try{Field field=MainActivity.class.getDeclaredField("player");field.setAccessible(true);watched.set(((SwirPlayer)field.get(a)).watchedMillis());}catch(Exception error){throw new AssertionError(error);}});}assertTrue("Direct DASH decoded video: "+failure.get(),confirmed.get());assertTrue("Moving video after discovered direct import",watched.get()>=1500);assertTrue(failure.get().isEmpty());}
            write("explorer-direct-playback.json",new JSONObject().put("verifiedAt",System.currentTimeMillis()).put("sdk",android.os.Build.VERSION.SDK_INT).put("manifest",DASH).put("candidateKind",candidate.kind.name()).put("encryptedReference",true).put("firstFrame",confirmed.get()).put("movingVideoMs",watched.get()).put("failure",failure.get()));
        }finally{if(!source.isEmpty())repository.remove(source);java.security.KeyStore keys=java.security.KeyStore.getInstance("AndroidKeyStore");keys.load(null);keys.deleteEntry("swir.providers.v1."+profile);}
    }
    private void write(String name,JSONObject value)throws Exception{File dir=new File(context().getExternalFilesDir(null),"evidence");assertTrue(dir.isDirectory()||dir.mkdirs());try(FileOutputStream out=new FileOutputStream(new File(dir,name))){out.write(value.toString(2).getBytes(StandardCharsets.UTF_8));}}
}
