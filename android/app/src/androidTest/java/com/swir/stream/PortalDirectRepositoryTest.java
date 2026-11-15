package com.swir.stream;

import androidx.test.ext.junit.runners.AndroidJUnit4;
import androidx.test.platform.app.InstrumentationRegistry;
import org.junit.After;
import org.junit.Before;
import org.junit.Test;
import org.junit.runner.RunWith;
import org.json.JSONObject;
import java.io.File;
import java.io.IOException;
import java.net.InetAddress;
import java.nio.charset.StandardCharsets;
import java.util.Collections;
import java.util.UUID;
import java.util.concurrent.atomic.AtomicBoolean;
import java.util.concurrent.atomic.AtomicInteger;
import java.util.concurrent.atomic.AtomicReference;
import static org.junit.Assert.*;

@RunWith(AndroidJUnit4.class)
public final class PortalDirectRepositoryTest {
    private File root;private String profile;private final AtomicBoolean cancel=new AtomicBoolean();
    private static final String MAC="02:00:00:00:00:01",GENRES="{\"js\":[{\"id\":1,\"title\":\"TV\"}]}",CHANNELS="{\"js\":[{\"id\":1,\"name\":\"Fixture TV\",\"cmd\":\"ffmpeg http://localhost/ch/1\"}]}";
    @Before public void setup(){root=new File(InstrumentationRegistry.getInstrumentation().getTargetContext().getCacheDir(),"portal-direct-"+UUID.randomUUID());profile=UUID.randomUUID().toString().replace("-","");}
    @After public void cleanup()throws Exception{delete(root);java.security.KeyStore keys=java.security.KeyStore.getInstance("AndroidKeyStore");keys.load(null);keys.deleteEntry("swir.providers.v1."+profile);}
    private static void delete(File file){File[] children=file.listFiles();if(children!=null)for(File child:children)delete(child);file.delete();}
    private static ProviderHttp.Document doc(String url,String text){return new ProviderHttp.Document(url,text.getBytes(StandardCharsets.UTF_8),System.currentTimeMillis(),0);}
    private ProviderRepository repository(ProviderHttp.Fetcher fetcher){return new ProviderRepository(root,profile,fetcher,host->new InetAddress[]{InetAddress.getByAddress(new byte[]{93,(byte)184,(byte)216,34})});}
    private ProviderHttp.Fetcher direct(AtomicReference<String> media){return (url,h,c)->{assertFalse(h.containsKey("Authorization"));if(url.contains("get_genres"))return doc(url,GENRES);if(url.contains("get_all_channels"))return doc(url,CHANNELS);if(url.contains("create_link"))return doc(url,"{\"js\":{\"cmd\":\"ffmpeg "+media.get()+"\"}}");throw new IOException("Unexpected synthetic request");};}
    private interface Throwing {void run()throws Exception;}
    private static void rejects(Throwing action)throws Exception{try{action.run();fail("Expected rejection");}catch(IOException expected){}}

    @Test public void directCatalogRetainsInputAndExplicitUnconfirmedStatus()throws Exception{
        ProviderRepository repo=repository(direct(new AtomicReference<>("https://media.example/live.ts")));ProviderRepository.SourceInfo added=repo.addPortal("Fixture","https://fixture.example/c/",MAC,cancel);
        assertEquals(1,added.count);assertEquals(PortalDiscovery.DIRECT_NOTICE,added.status);assertEquals("https://fixture.example/c/",repo.editConfiguration(added.id).getJSONObject("config").getString("url"));
        JSONObject stored=new ProviderVault(new File(root,profile),profile).read(added.id);assertEquals(PortalDiscovery.DIRECT,stored.getString("portalMode"));assertEquals("https://fixture.example/portal.php",stored.getString("portalEndpoint"));assertFalse(repo.stations().get(0).toJson().toString().contains(MAC));
    }
    @Test public void directMediaKeepsMacOnlyOnSameOriginAndNoBearer()throws Exception{
        AtomicReference<String> media=new AtomicReference<>("https://fixture.example/live.ts");ProviderRepository repo=repository(direct(media));repo.addPortal("Fixture","https://fixture.example/c/",MAC,cancel);Station ref=repo.stations().get(0),same=repo.resolve(ref,cancel);
        assertTrue(same.requestHeaders.get("Cookie").contains(MAC));assertFalse(same.requestHeaders.containsKey("Authorization"));assertEquals(PortalDiscovery.USER_AGENT,same.userAgent);
        media.set("https://media.example/live.ts");Station other=repo.resolve(ref,cancel);assertTrue(other.requestHeaders.isEmpty());assertFalse(other.toJson().toString().contains(MAC));
    }
    @Test public void failedRefreshKeepsEncryptedCatalogAndTimestamp()throws Exception{
        AtomicBoolean fail=new AtomicBoolean();ProviderHttp.Fetcher good=direct(new AtomicReference<>("https://media.example/live.ts"));ProviderRepository repo=repository((url,h,c)->{if(fail.get())return doc(url,"{\"blocked\":true,\"js\":[]}");return good.fetch(url,h,c);});ProviderRepository.SourceInfo added=repo.addPortal("Fixture","https://fixture.example/c/",MAC,cancel);
        JSONObject before=new ProviderVault(new File(root,profile),profile).read(added.id);fail.set(true);rejects(()->repo.refresh(added.id,cancel));JSONObject after=new ProviderVault(new File(root,profile),profile).read(added.id);assertEquals(before.toString(),after.toString());assertEquals(added.updatedAt,repo.listSources().get(0).updatedAt);assertEquals(1,repo.stations().size());
    }
    @Test public void sourceRemovedWhileResolvingCannotReturnOldLink()throws Exception{
        AtomicReference<ProviderRepository> owner=new AtomicReference<>();AtomicReference<String> id=new AtomicReference<>();ProviderHttp.Fetcher good=direct(new AtomicReference<>("https://media.example/live.ts"));ProviderRepository repo=repository((url,h,c)->{if(url.contains("create_link"))owner.get().remove(id.get());return good.fetch(url,h,c);});owner.set(repo);id.set(repo.addPortal("Fixture","https://fixture.example/c/",MAC,cancel).id);Station ref=repo.stations().get(0);rejects(()->repo.resolve(ref,cancel));assertTrue(repo.listSources().isEmpty());
    }
    @Test public void profileMismatchAndCancelledResolveDoNotExposeMedia()throws Exception{
        ProviderRepository repo=repository(direct(new AtomicReference<>("https://media.example/live.ts")));repo.addPortal("Fixture","https://fixture.example/c/",MAC,cancel);Station ref=repo.stations().get(0);ProviderRepository other=new ProviderRepository(root,UUID.randomUUID().toString().replace("-",""),(u,h,c)->{throw new AssertionError("Cross-profile fetch");},host->{throw new AssertionError("Cross-profile DNS");});rejects(()->other.resolve(ref,cancel));cancel.set(true);rejects(()->repo.resolve(ref,cancel));
    }
    @Test public void renamePreservesSourceIdentityAndRawEndpoint()throws Exception{
        ProviderRepository repo=repository(direct(new AtomicReference<>("https://media.example/live.ts")));ProviderRepository.SourceInfo source=repo.addPortal("Fixture","https://fixture.example/c/",MAC,cancel);String channel=repo.stations().get(0).providerChannelId;
        repo.update(source.id,"Renamed","portal","https://fixture.example/c/","","",MAC,"","","",Collections.emptyMap(),cancel);assertEquals(source.id,repo.listSources().get(0).id);assertEquals(channel,repo.stations().get(0).providerChannelId);assertEquals(PortalDiscovery.DIRECT_NOTICE,repo.listSources().get(0).status);
    }
    @Test public void originalCommandWithExplicitFalseFlagsSkipsCreateLinkAfterFreshSession()throws Exception{
        AtomicInteger probes=new AtomicInteger(),links=new AtomicInteger();AtomicBoolean denied=new AtomicBoolean();ProviderRepository repo=repository((url,h,c)->{if(url.contains("get_genres")){probes.incrementAndGet();return doc(url,denied.get()?"{\"blocked\":true,\"js\":[]}":GENRES);}if(url.contains("get_all_channels"))return doc(url,"{\"js\":[{\"id\":1,\"name\":\"TV\",\"cmd\":\"ffmpeg https://media.example/live.ts\",\"use_http_tmp_link\":\"0\",\"use_load_balancing\":0}]}");links.incrementAndGet();throw new AssertionError("Explicit direct media must not invoke create_link");});
        ProviderRepository.SourceInfo source=repo.addPortal("Fixture","https://fixture.example/c/",MAC,cancel);Station ref=repo.stations().get(0);int before=probes.get();Station first=repo.resolve(ref,cancel),second=repo.resolve(ref,cancel);assertEquals(before+2,probes.get());assertEquals(0,links.get());assertEquals("https://media.example/live.ts",first.url);assertEquals(first.url,second.url);assertTrue(first.requestHeaders.isEmpty());assertFalse(ref.toJson().toString().contains("media.example"));JSONObject stored=new ProviderVault(new File(root,profile),profile).read(source.id).getJSONArray("entries").getJSONObject(0);assertEquals(Boolean.FALSE,stored.get("use_http_tmp_link"));denied.set(true);rejects(()->repo.resolve(ref,cancel));assertEquals(0,links.get());
    }
    @Test public void missingUnknownTrueOrUnsafeOriginalStillRequestsFreshLink()throws Exception{
        for(String fields:new String[]{"",",\"use_http_tmp_link\":null,\"use_load_balancing\":0",",\"use_http_tmp_link\":false,\"use_load_balancing\":1",",\"use_http_tmp_link\":false,\"use_load_balancing\":false"}){
            AtomicInteger links=new AtomicInteger();String command=fields.endsWith("false")?"ffmpeg http://localhost/ch/1":"ffmpeg https://media.example/original.ts";ProviderRepository repo=repository((url,h,c)->{if(url.contains("get_genres"))return doc(url,GENRES);if(url.contains("get_all_channels"))return doc(url,"{\"js\":[{\"id\":1,\"name\":\"TV\",\"cmd\":\""+command+"\""+fields+"}]}");links.incrementAndGet();return doc(url,"{\"js\":{\"cmd\":\"ffmpeg https://media.example/resolved.ts\"}}");});ProviderRepository.SourceInfo source=repo.addPortal("Fixture","https://fixture.example/c/",MAC,cancel);Station resolved=repo.resolve(repo.stations().get(0),cancel);assertEquals(1,links.get());assertEquals("https://media.example/resolved.ts",resolved.url);repo.remove(source.id);
        }
    }
}
