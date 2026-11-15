package com.swir.stream;

import android.app.AlertDialog;
import android.content.Context;
import android.os.Bundle;
import android.os.SystemClock;
import android.view.View;
import android.view.WindowManager;
import android.widget.EditText;
import android.widget.ListView;
import androidx.test.core.app.ActivityScenario;
import androidx.test.ext.junit.runners.AndroidJUnit4;
import androidx.test.platform.app.InstrumentationRegistry;
import org.junit.*;
import org.junit.runner.RunWith;
import static org.junit.Assert.*;
import java.io.*;
import java.lang.reflect.*;
import java.net.InetAddress;
import java.nio.charset.StandardCharsets;
import java.security.KeyStore;
import java.util.*;
import java.util.concurrent.*;
import java.util.concurrent.atomic.*;

/** Actual Activity -> secure form -> protocol fixture -> vault -> local channel search. */
@RunWith(AndroidJUnit4.class)
public final class ProviderSmartImportTest {
    private Context context;private File index;private byte[] original;private boolean hadIndex;private String profile;
    private ProviderRepository fixture;private final List<String> calls=Collections.synchronizedList(new ArrayList<>());
    private static final String LINK="https://fixture.example:8443/panel%20one/get.php?username=user%2Bname&password=fixture-secret%26value&type=m3u_plus";
    @Before public void setup()throws Exception{
        context=InstrumentationRegistry.getInstrumentation().getTargetContext();index=new File(context.getFilesDir(),"profiles/index.json");hadIndex=ProfileStore.hasSnapshot(index);original=hadIndex?ProfileStore.readJson(index,64000).toString().getBytes(StandardCharsets.UTF_8):null;UiTestData.seed(context);ProfileStore store=new ProfileStore(context);store.add("Import "+UUID.randomUUID().toString().substring(0,8));profile=store.currentId();
        fixture=new ProviderRepository(new File(context.getNoBackupFilesDir(),"providers"),profile,(url,headers,cancel)->{calls.add(url);String body;if(url.contains("xmltv.php"))body="<tv/>";else if(url.contains("get_live_categories"))body="[{\"category_id\":\"1\",\"category_name\":\"Moje TV\"}]";else if(url.contains("get_live_streams"))body="[{\"stream_id\":1,\"name\":\"Żółw TV HD\",\"category_id\":\"1\"},{\"stream_id\":2,\"name\":\"Prywatny Sport\",\"category_id\":\"1\"}]";else if(url.contains("player_api.php"))body="{\"user_info\":{\"auth\":1,\"status\":\"Active\",\"allowed_output_formats\":[\"ts\"]}}";else body="#EXTM3U\n#EXTINF:-1,Żółw TV HD\nhttps://cdn.example/one.m3u8?token=fixture-secret\n#EXTINF:-1,Prywatny Sport\nhttps://cdn.example/two.m3u8\n";return new ProviderHttp.Document(url,body.getBytes(StandardCharsets.UTF_8),System.currentTimeMillis(),0);},host->new InetAddress[]{InetAddress.getByAddress(new byte[]{93,(byte)184,(byte)216,34})});
    }
    @After public void restore()throws Exception{UiTestData.restore(context);remove(new File(context.getNoBackupFilesDir(),"providers/"+profile));remove(new File(context.getFilesDir(),"profiles/"+profile));KeyStore keys=KeyStore.getInstance("AndroidKeyStore");keys.load(null);keys.deleteEntry("swir.providers.v1."+profile);if(hadIndex){try(FileOutputStream out=new FileOutputStream(index)){out.write(original);}}else index.delete();new File(index+".bak").delete();}
    private static void remove(File path){if(path.isDirectory()){File[] children=path.listFiles();if(children!=null)for(File child:children)remove(child);}path.delete();}
    private static Object get(Object target,String name){try{Field field=target.getClass().getDeclaredField(name);field.setAccessible(true);return field.get(target);}catch(Exception error){throw new AssertionError(error);}}
    private static void set(Object target,String name,Object value){try{Field field=target.getClass().getDeclaredField(name);field.setAccessible(true);field.set(target,value);}catch(Exception error){throw new AssertionError(error);}}
    private static void call(MainActivity activity,String name,Class<?>[] types,Object... args){try{Method method=MainActivity.class.getDeclaredMethod(name,types);method.setAccessible(true);method.invoke(activity,args);}catch(Exception error){throw new AssertionError(error);}}
    private static void idle(){InstrumentationRegistry.getInstrumentation().waitForIdleSync();}
    private static void drain(ActivityScenario<MainActivity> scene,String worker)throws Exception{CountDownLatch done=new CountDownLatch(1);scene.onActivity(a->((ExecutorService)get(a,worker)).execute(done::countDown));assertTrue("Background operation finished",done.await(15,TimeUnit.SECONDS));idle();}
    private ActivityScenario<MainActivity> launch()throws Exception{ActivityScenario<MainActivity> scene=ActivityScenario.launch(MainActivity.class);AtomicBoolean ready=new AtomicBoolean();long until=SystemClock.elapsedRealtime()+15000;while(!ready.get()&&SystemClock.elapsedRealtime()<until){scene.onActivity(a->ready.set(a.getWindow().getDecorView().findViewWithTag("channel_list")!=null));if(!ready.get())SystemClock.sleep(30);}assertTrue(ready.get());drain(scene,"work");drain(scene,"providerWork");scene.onActivity(a->set(a,"providers",fixture));return scene;}
    private static AlertDialog page(MainActivity activity){return (AlertDialog)get(get(activity,"providerPage"),"page");}
    private static EditText field(MainActivity activity,String tag){return page(activity).getWindow().getDecorView().findViewWithTag(tag);}
    private static int rows(MainActivity activity){return ((ListView)activity.getWindow().getDecorView().findViewWithTag("channel_list")).getAdapter().getCount();}
    private void startPaste(ActivityScenario<MainActivity> scene,String link){scene.onActivity(a->a.getWindow().getDecorView().findViewWithTag("paste_source_link").performClick());idle();scene.onActivity(a->{field(a,"provider_pasted_link").setText(link);page(a).getButton(AlertDialog.BUTTON_POSITIVE).performClick();});idle();}
    private static void save(ActivityScenario<MainActivity> scene)throws Exception{scene.onActivity(a->page(a).getButton(AlertDialog.BUTTON_POSITIVE).performClick());drain(scene,"providerWork");drain(scene,"providerWork");}
    @Test public void universalPastePrefillsWithoutFetchThenImportsAndOpensSearchableAccount()throws Exception{
        try(ActivityScenario<MainActivity> scene=launch()){startPaste(scene,LINK);scene.onActivity(a->{assertNotNull("Recognized link opens dedicated Xtream form",page(a).getWindow().getDecorView().findViewWithTag("provider_form_xtream"));assertEquals("https://fixture.example:8443/panel%20one",field(a,"provider_url").getText().toString());assertEquals("user+name",field(a,"provider_login").getText().toString());assertEquals("fixture-secret&value",field(a,"provider_password").getText().toString());assertTrue((page(a).getWindow().getAttributes().flags&WindowManager.LayoutParams.FLAG_SECURE)!=0);assertFalse(field(a,"provider_password").isSaveEnabled());assertNotNull("Password remains masked",field(a,"provider_password").getTransformationMethod());});assertTrue(calls.isEmpty());assertTrue(fixture.listSources().isEmpty());save(scene);assertEquals(1,fixture.listSources().size());assertTrue(calls.get(0).startsWith("https://fixture.example:8443/panel%20one/player_api.php?username=user%2Bname&password=fixture-secret%26value"));scene.onActivity(a->{assertEquals(fixture.listSources().get(0).id,get(a,"providerScopeId"));assertEquals(2,rows(a));((EditText)a.getWindow().getDecorView().findViewWithTag("channel_search")).setText("zolw");call(a,"filter",new Class<?>[]{});assertEquals(1,rows(a));});assertPrivateFiles();}
    }
    @Test public void fullLinkInXtreamServerWorksAndConflictingFieldsDoNotFetch()throws Exception{
        try(ActivityScenario<MainActivity> scene=launch()){scene.onActivity(a->a.getWindow().getDecorView().findViewWithTag("provider_tile_xtream").performClick());drain(scene,"providerWork");scene.onActivity(a->page(a).getWindow().getDecorView().findViewWithTag("provider_add").performClick());idle();scene.onActivity(a->{field(a,"provider_url").setText(LINK);field(a,"provider_login").setText("different");page(a).getButton(AlertDialog.BUTTON_POSITIVE).performClick();assertNotNull("Conflicting fields show an error before fetch",field(a,"provider_url").getError());assertEquals(LINK,field(a,"provider_url").getText().toString());});assertTrue(calls.isEmpty());scene.onActivity(a->field(a,"provider_login").setText(""));save(scene);assertEquals(1,fixture.listSources().size());assertEquals(2,fixture.stations().size());}
    }
    @Test public void explicitOwnPlaylistRetainsCustomRoutingAndIsNeverPublicDiscovery()throws Exception{
        String link="https://fixture.example:8443/get.php?username=u&password=fixture-secret&tenant=custom&output=m3u8";
        try(ActivityScenario<MainActivity> scene=launch()){scene.onActivity(a->a.getWindow().getDecorView().findViewWithTag("paste_source_link").performClick());idle();scene.onActivity(a->{field(a,"provider_pasted_link").setText(link);page(a).getButton(AlertDialog.BUTTON_POSITIVE).performClick();assertNotNull("Unknown routing requires explicit own-playlist choice",field(a,"provider_pasted_link").getError());page(a).getWindow().getDecorView().findViewWithTag("provider_paste_m3u").performClick();});idle();scene.onActivity(a->assertEquals(link,field(a,"provider_url").getText().toString()));assertTrue(calls.isEmpty());save(scene);ProviderRepository.SourceInfo source=fixture.listSources().get(0);assertEquals("m3u",source.type);assertFalse(fixture.editConfiguration(source.id).getJSONObject("config").optBoolean("publicDiscovery"));assertEquals(link,calls.get(0));assertPrivateFiles();}
    }
    @Test public void searchPasteRoutesPrivatelyAndTypingDoesNotStealFocusOrSaveSecret()throws Exception{
        try(ActivityScenario<MainActivity> scene=launch()){scene.onActivity(a->{EditText search=a.getWindow().getDecorView().findViewWithTag("channel_search");for(char c:"http:".toCharArray())search.append(String.valueOf(c));assertNull(get(a,"providerPage"));assertFalse(search.isSaveEnabled());search.setText(LINK);});idle();scene.onActivity(a->{assertNotNull(field(a,"provider_pasted_link"));assertEquals(LINK,field(a,"provider_pasted_link").getText().toString());assertEquals("",get(a,"query"));assertEquals("",((EditText)a.getWindow().getDecorView().findViewWithTag("channel_search")).getText().toString());Bundle saved=new Bundle();a.onSaveInstanceState(saved);assertFalse(saved.toString().contains("fixture-secret"));assertFalse(String.valueOf(get(a,"lastNotice")).contains("fixture-secret"));page(a).dismiss();});assertTrue(calls.isEmpty());assertTrue(fixture.listSources().isEmpty());assertPrivateFiles();}
    }
    @Test public void malformedAndDoubleEncodedSecretsNeverReachPublicTransportOrHistory()throws Exception{
        File folder=new File(context.getCacheDir(),"private-search-"+UUID.randomUUID());AtomicInteger network=new AtomicInteger();CatalogRepository catalog=new CatalogRepository(folder,(url,cancel)->{network.incrementAndGet();throw new IOException("Unexpected transport");});
        try{for(String query:new String[]{LINK,"https:/bad?username=u&password=fixture-secret","username%253Du%2526password%253Dfixture-secret"}){assertTrue(catalog.searchWeb(query,Collections.singleton("PL"),null,new AtomicBoolean()).isEmpty());for(String line:catalog.diagnostics())assertFalse(line.contains("fixture-secret"));}assertEquals(0,network.get());noSecrets(folder);}
        finally{remove(folder);}
    }
    private void assertPrivateFiles()throws Exception{noSecrets(context.getFilesDir());noSecrets(new File(context.getNoBackupFilesDir(),"providers/"+profile));}
    private static void noSecrets(File path)throws Exception{if(!path.exists())return;if(path.isDirectory()){File[] children=path.listFiles();if(children!=null)for(File child:children)noSecrets(child);return;}try(FileInputStream in=new FileInputStream(path);ByteArrayOutputStream out=new ByteArrayOutputStream()){byte[] buffer=new byte[8192];int n;while((n=in.read(buffer))!=-1)out.write(buffer,0,n);assertFalse("No raw private data in "+path.getName(),new String(out.toByteArray(),StandardCharsets.ISO_8859_1).contains("fixture-secret"));}}
}
