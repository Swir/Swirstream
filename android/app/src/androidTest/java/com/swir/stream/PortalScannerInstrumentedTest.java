package com.swir.stream;

import android.content.Context;
import androidx.test.ext.junit.runners.AndroidJUnit4;
import androidx.test.platform.app.InstrumentationRegistry;
import org.junit.*;
import org.junit.runner.RunWith;
import static org.junit.Assert.*;
import java.io.*;
import java.net.InetAddress;
import java.nio.charset.StandardCharsets;
import java.security.KeyStore;
import java.util.*;
import java.util.concurrent.atomic.*;
import okhttp3.*;

/** Synthetic operator fixtures only; no external requests or real credentials. */
@RunWith(AndroidJUnit4.class)
public final class PortalScannerInstrumentedTest {
    private File folder;private String profile;private AtomicBoolean active,cancel;private List<String> actions;
    @Before public void setup(){Context c=InstrumentationRegistry.getInstrumentation().getTargetContext();profile=UUID.randomUUID().toString().replace("-","");folder=new File(c.getCacheDir(),"scanner-tests-"+profile);active=new AtomicBoolean(true);cancel=new AtomicBoolean();actions=new ArrayList<>();}
    @After public void clean()throws Exception{remove(folder);KeyStore keys=KeyStore.getInstance("AndroidKeyStore");keys.load(null);keys.deleteEntry("swir.providers.v1."+profile);}
    private static void remove(File f){if(f.isDirectory()){File[] items=f.listFiles();if(items!=null)for(File child:items)remove(child);}f.delete();}
    static String body(String url,Map<String,String> headers)throws IOException{if(url.contains("get_genres")&&!headers.containsKey("Authorization"))throw new ProviderHttp.StatusException(401);if(url.contains("handshake"))return "{\"js\":{\"token\":\"synthetic-secret\"}}";if(url.contains("get_profile"))return "{\"js\":{\"id\":17,\"status\":0}}";if(url.contains("get_genres"))return "{\"js\":[]}";if(url.contains("get_all_channels"))return "{\"js\":[{\"id\":\"1\",\"name\":\"Kanał testowy\",\"cmd\":\"ffmpeg http://localhost/ch/1\"}]}";if(url.contains("create_link"))return "{\"js\":{\"cmd\":\"https://media.example.org/one.m3u8\"}}";throw new IOException("Unknown synthetic route");}
    private ProviderRepository repository(boolean invalidate){return new ProviderRepository(folder,profile,(u,h,c)->{actions.add(u);String value=body(u,h);if(invalidate&&u.contains("get_all_channels"))active.set(false);return new ProviderHttp.Document(u,value.getBytes(StandardCharsets.UTF_8),0,0);},host->new InetAddress[]{InetAddress.getByAddress(new byte[]{93,(byte)184,(byte)216,34})});}
    @Test public void selectedSourceUsesEncryptedStoreAndCanonicalDuplicateIdentity()throws Exception{ProviderRepository repository=repository(false);Object gate=new Object();ProviderRepository.SourceInfo first=repository.addScannedPortal("Wybrany portal","https://portal.example.org/c","02:00:00:00:00:01",cancel,gate,active::get);ProviderRepository.SourceInfo second=repository.addScannedPortal("Inna nazwa","https://portal.example.org/c/","02:00:00:00:00:01",cancel,gate,active::get);assertEquals(first.id,second.id);assertEquals(1,repository.listSources().size());assertEquals(1,repository.stations().size());assertTrue(repository.stations().get(0).validProviderReference());assertFalse(repository.stations().get(0).toJson().toString().contains("02:00"));assertFalse(repository.stations().get(0).toJson().toString().contains("synthetic-secret"));}
    @Test public void invalidatedLifecycleCannotCommitAfterCatalogueArrives()throws Exception{ProviderRepository repository=repository(true);try{repository.addScannedPortal("Wybrane","https://portal.example.org/c/","02:00:00:00:00:01",cancel,new Object(),active::get);fail();}catch(IOException expected){}assertTrue(repository.listSources().isEmpty());assertTrue(cancel.get());assertTrue(actions.stream().anyMatch(u->u.contains("get_all_channels")));}
    @Test public void authenticatedGenreDenialDoesNotContinueToChannels()throws Exception{ProviderHttp.Fetcher fetcher=(u,h,c)->{actions.add(u);if(u.contains("get_genres")&&h.containsKey("Authorization"))throw new ProviderHttp.StatusException(403);return new ProviderHttp.Document(u,body(u,h).getBytes(StandardCharsets.UTF_8),0,0);};try{ProviderAdapters.portal(new org.json.JSONObject().put("url","https://portal.example.org/c/").put("mac","02:00:00:00:00:01"),fetcher,cancel);fail();}catch(ProviderHttp.StatusException denied){assertEquals(403,denied.code);}assertFalse(actions.stream().anyMatch(u->u.contains("get_all_channels")));}
    @Test public void scannerPublicRedirectAllowsOnlySameOriginOrStandardTlsUpgrade(){assertTrue(SourceExplorerHttp.scannerRedirect("http://portal.example.org/c/","https://portal.example.org/c/"));assertTrue(SourceExplorerHttp.scannerRedirect("https://portal.example.org/c/","https://portal.example.org/elsewhere"));assertFalse(SourceExplorerHttp.scannerRedirect("http://portal.example.org/c/","http://portal.example.org:443/c/"));assertFalse(SourceExplorerHttp.scannerRedirect("https://portal.example.org/c/","http://portal.example.org/c/"));assertFalse(SourceExplorerHttp.scannerRedirect("https://portal.example.org/c/","https://other.example.org/c/"));}
}
