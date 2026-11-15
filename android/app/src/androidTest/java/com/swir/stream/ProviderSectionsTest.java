package com.swir.stream;

import android.content.Context;
import android.os.SystemClock;
import android.widget.EditText;
import android.app.AlertDialog;
import android.view.View;
import android.view.KeyEvent;
import android.view.WindowManager;
import android.graphics.Rect;
import android.content.pm.ActivityInfo;
import java.util.Arrays;
import java.util.Collections;
import android.widget.ListView;
import androidx.test.core.app.ActivityScenario;
import androidx.test.ext.junit.runners.AndroidJUnit4;
import androidx.test.platform.app.InstrumentationRegistry;
import org.junit.After;
import org.junit.Before;
import org.junit.Test;
import org.junit.runner.RunWith;
import static org.junit.Assert.*;
import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.IOException;
import java.lang.reflect.Field;
import java.lang.reflect.Method;
import java.net.InetAddress;
import java.nio.charset.StandardCharsets;
import java.security.KeyStore;
import java.util.ArrayList;
import java.util.List;
import java.util.UUID;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicBoolean;
import java.util.concurrent.atomic.AtomicReference;

/** Real vault -> production Activity loading. No operator credentials or new network endpoint. */
@RunWith(AndroidJUnit4.class)
public final class ProviderSectionsTest {
    private Context context;private File index;private byte[] originalIndex;private boolean hadIndex;
    private String first,second,firstName,secondName,sourceId;private ProviderRepository fixture;
    private final List<String> createdProfiles=new ArrayList<>();private final AtomicBoolean offline=new AtomicBoolean();
    @Before public void setup()throws Exception{
        context=InstrumentationRegistry.getInstrumentation().getTargetContext();index=new File(context.getFilesDir(),"profiles/index.json");hadIndex=ProfileStore.hasSnapshot(index);originalIndex=hadIndex?ProfileStore.readJson(index,64000).toString().getBytes(StandardCharsets.UTF_8):null;
        UiTestData.seed(context);ProfileStore profiles=new ProfileStore(context);String suffix=UUID.randomUUID().toString().substring(0,8);firstName="Źródła A "+suffix;secondName="Źródła B "+suffix;profiles.add(firstName);first=profiles.currentId();createdProfiles.add(first);profiles.add(secondName);second=profiles.currentId();createdProfiles.add(second);profiles.select(firstName);
        fixture=new ProviderRepository(new File(context.getNoBackupFilesDir(),"providers"),first,(url,headers,cancel)->{
            if(offline.get())throw new IOException("Fixture offline");String body="#EXTM3U\n#EXTINF:-1 tvg-id=\"PrivateOne.pl\" group-title=\"news\",Prywatna Żółć HD\nhttps://media.example/live/1.m3u8?token=integration-secret\n#EXTINF:-1 group-title=\"sports\",Prywatny Sport\nhttps://media.example/live/2.m3u8?token=integration-secret\n";
            return new ProviderHttp.Document(url,body.getBytes(StandardCharsets.UTF_8),System.currentTimeMillis(),0);
        },host->new InetAddress[]{InetAddress.getByAddress(new byte[]{93,(byte)184,(byte)216,34})});sourceId=fixture.addM3u("Moja biblioteka","https://fixture.example:8443/get.php?password=integration-secret","",new AtomicBoolean()).id;
    }
    @After public void restore()throws Exception{
        UiTestData.restore(context);KeyStore store=KeyStore.getInstance("AndroidKeyStore");store.load(null);
        for(String id:createdProfiles){removeTree(new File(new File(context.getNoBackupFilesDir(),"providers"),id));removeTree(new File(new File(context.getFilesDir(),"profiles"),id));store.deleteEntry("swir.providers.v1."+id);}
        if(hadIndex){try(FileOutputStream output=new FileOutputStream(index)){output.write(originalIndex);}}else index.delete();new File(index.getPath()+".bak").delete();
    }
    private static void removeTree(File file){if(file.isDirectory()){File[] children=file.listFiles();if(children!=null)for(File child:children)removeTree(child);}file.delete();}
    private static Object get(Object target,String name){try{Field field=target.getClass().getDeclaredField(name);field.setAccessible(true);return field.get(target);}catch(Exception e){throw new AssertionError(e);}}
    private static void set(Object target,String name,Object value){try{Field field=target.getClass().getDeclaredField(name);field.setAccessible(true);field.set(target,value);}catch(Exception e){throw new AssertionError(e);}}
    private static Object call(MainActivity activity,String name,Class<?>[] types,Object... args){try{Method method=MainActivity.class.getDeclaredMethod(name,types);method.setAccessible(true);return method.invoke(activity,args);}catch(Exception e){throw new AssertionError(e);}}
    private ActivityScenario<MainActivity> launch()throws Exception{
        ActivityScenario<MainActivity> scene=ActivityScenario.launch(MainActivity.class);AtomicBoolean ready=new AtomicBoolean();long deadline=SystemClock.elapsedRealtime()+15000;
        while(!ready.get()&&SystemClock.elapsedRealtime()<deadline){scene.onActivity(a->ready.set(a.getWindow().getDecorView().findViewWithTag("channel_list")!=null));SystemClock.sleep(40);}assertTrue("Production Activity opened saved library",ready.get());drain(scene,"work");drain(scene,"providerWork");return scene;
    }
    private static void drain(ActivityScenario<MainActivity> scene,String worker)throws Exception{CountDownLatch done=new CountDownLatch(1);scene.onActivity(a->((ExecutorService)get(a,worker)).execute(done::countDown));assertTrue(done.await(15,TimeUnit.SECONDS));InstrumentationRegistry.getInstrumentation().waitForIdleSync();}
    private static int rows(MainActivity activity){return ((ListView)activity.getWindow().getDecorView().findViewWithTag("channel_list")).getAdapter().getCount();}
    @SuppressWarnings("unchecked") private static List<Station> privateLibrary(MainActivity activity){return (List<Station>)get(activity,"providerLibrary");}
    private void shot(String name)throws Exception{InstrumentationRegistry.getInstrumentation().waitForIdleSync();SystemClock.sleep(250);android.graphics.Bitmap bitmap=InstrumentationRegistry.getInstrumentation().getUiAutomation().takeScreenshot();assertNotNull(bitmap);File dir=new File(context.getExternalFilesDir(null),"evidence");dir.mkdirs();try(FileOutputStream output=new FileOutputStream(new File(dir,"provider-section-"+name+".png"))){assertTrue(bitmap.compress(android.graphics.Bitmap.CompressFormat.PNG,100,output));}bitmap.recycle();}
    private AlertDialog page(MainActivity a){return (AlertDialog)get(get(a,"providerPage"),"page");}
    private void section(ActivityScenario<MainActivity> scene,String type)throws Exception{scene.onActivity(a->a.getWindow().getDecorView().findViewWithTag("provider_tile_"+type).performClick());drain(scene,"providerWork");}
    @Test public void fourSectionsVisibleWithoutScrollingOnPhoneAndLandscapeAndDpad()throws Exception{
        try(ActivityScenario<MainActivity> scene=launch()){
            for(int orientation:new int[]{ActivityInfo.SCREEN_ORIENTATION_PORTRAIT,ActivityInfo.SCREEN_ORIENTATION_LANDSCAPE}){
                scene.onActivity(a->a.setRequestedOrientation(orientation));AtomicBoolean laidOut=new AtomicBoolean();long deadline=SystemClock.elapsedRealtime()+7000;
                while(!laidOut.get()&&SystemClock.elapsedRealtime()<deadline){scene.onActivity(a->{View root=a.getWindow().getDecorView(),tile=root.findViewWithTag("provider_tile_m3u");int expected=orientation==ActivityInfo.SCREEN_ORIENTATION_PORTRAIT?android.content.res.Configuration.ORIENTATION_PORTRAIT:android.content.res.Configuration.ORIENTATION_LANDSCAPE;laidOut.set(a.getResources().getConfiguration().orientation==expected&&tile.getWidth()>0&&((root.getWidth()>root.getHeight())==(expected==android.content.res.Configuration.ORIENTATION_LANDSCAPE)));});if(!laidOut.get())SystemClock.sleep(40);}
                assertTrue("Orientation completed and real tile laid out",laidOut.get());
                // Hardware arrows leave touch mode; requestFocus alone cannot do this on API23.
                InstrumentationRegistry.getInstrumentation().sendKeyDownUpSync(KeyEvent.KEYCODE_DPAD_DOWN);InstrumentationRegistry.getInstrumentation().waitForIdleSync();
                scene.onActivity(a->{for(String type:new String[]{"m3u","xtream","portal","drm"}){View tile=a.getWindow().getDecorView().findViewWithTag("provider_tile_"+type);Rect rect=new Rect();assertTrue("Tile shown: "+type,tile.isShown());assertTrue("Tile visible rect: "+type,tile.getGlobalVisibleRect(rect));assertEquals("Tile full width: "+type,tile.getWidth(),rect.width());assertEquals("Tile full height: "+type,tile.getHeight(),rect.height());assertTrue("Tile focusable: "+type,tile.isFocusable());}assertTrue("Remote can focus first tile after leaving touch mode",a.getWindow().getDecorView().findViewWithTag("provider_tile_m3u").requestFocus());});
                InstrumentationRegistry.getInstrumentation().sendKeyDownUpSync(KeyEvent.KEYCODE_DPAD_RIGHT);InstrumentationRegistry.getInstrumentation().waitForIdleSync();scene.onActivity(a->assertEquals("Right arrow selects Xtream","provider_tile_xtream",a.getCurrentFocus().getTag()));
            }
            scene.onActivity(a->{a.getWindow().getDecorView().findViewWithTag("fullscreen").performClick();assertFalse("Section row hidden in fullscreen",a.getWindow().getDecorView().findViewWithTag("provider_tiles").isShown());});InstrumentationRegistry.getInstrumentation().sendKeyDownUpSync(KeyEvent.KEYCODE_BACK);InstrumentationRegistry.getInstrumentation().waitForIdleSync();AtomicBoolean returned=new AtomicBoolean();long deadline=SystemClock.elapsedRealtime()+5000;
            while(!returned.get()&&SystemClock.elapsedRealtime()<deadline){scene.onActivity(a->returned.set(!(Boolean)get(a,"fullScreen")&&a.getWindow().getDecorView().findViewWithTag("provider_tiles").isShown()));if(!returned.get())SystemClock.sleep(40);}assertTrue("Back exits fullscreen and restores four sections",returned.get());
        }
    }
    @Test public void everyTileOpensOwnSectionAndDirectSecureForm()throws Exception{
        try(ActivityScenario<MainActivity> scene=launch()){
            for(String type:new String[]{"m3u","xtream","portal","drm"}){section(scene,type);shot(type);scene.onActivity(a->{assertNotNull(page(a).getWindow().getDecorView().findViewWithTag("provider_section_"+type));assertEquals(type.equals("m3u"),page(a).getWindow().getDecorView().findViewWithTag("provider_discover")!=null);page(a).getWindow().getDecorView().findViewWithTag("provider_add").performClick();});InstrumentationRegistry.getInstrumentation().waitForIdleSync();scene.onActivity(a->{assertNotNull(page(a).getWindow().getDecorView().findViewWithTag("provider_form_"+type));assertTrue((page(a).getWindow().getAttributes().flags&WindowManager.LayoutParams.FLAG_SECURE)!=0);assertFalse(page(a).getWindow().getDecorView().findViewWithTag("provider_url").isSaveEnabled());page(a).dismiss();});}
        }
    }
    @Test public void showChannelsActuallyFiltersSelectedAccountSearchAndReturn()throws Exception{
        try(ActivityScenario<MainActivity> scene=launch()){
            section(scene,"m3u");scene.onActivity(a->page(a).getWindow().getDecorView().findViewWithTag("provider_show_"+sourceId).performClick());drain(scene,"providerWork");scene.onActivity(a->{assertEquals(sourceId,get(a,"providerScopeId"));assertEquals(2,rows(a));((EditText)a.getWindow().getDecorView().findViewWithTag("channel_search")).setText("zolc");});SystemClock.sleep(300);scene.onActivity(a->{assertEquals(1,rows(a));((EditText)a.getWindow().getDecorView().findViewWithTag("channel_search")).setText("");});SystemClock.sleep(300);scene.onActivity(a->{a.getWindow().getDecorView().findViewWithTag("provider_scope_clear").performClick();assertEquals("All returns 3 TV + 1 radio + 2 private channels",6,rows(a));assertEquals("",get(a,"providerScopeId"));});
        }
    }
    @Test public void editPrefillsProtectedDataAndSectionFilterRetainsAllLegacyTypes()throws Exception{
        try(ActivityScenario<MainActivity> scene=launch()){
            section(scene,"m3u");scene.onActivity(a->page(a).getWindow().getDecorView().findViewWithTag("provider_edit_"+sourceId).performClick());drain(scene,"providerWork");scene.onActivity(a->{assertEquals("Moja biblioteka",((EditText)page(a).getWindow().getDecorView().findViewWithTag("provider_name")).getText().toString());assertTrue(((EditText)page(a).getWindow().getDecorView().findViewWithTag("provider_url")).getText().toString().contains("integration-secret"));assertTrue((page(a).getWindow().getAttributes().flags&WindowManager.LayoutParams.FLAG_SECURE)!=0);page(a).dismiss();});
        }
        List<ProviderRepository.SourceInfo> all=new ArrayList<>();for(String type:new String[]{"m3u","xtream","portal","drm","unknown"})all.add(new ProviderRepository.SourceInfo(type,type,type,1,1,""));assertEquals(5,ProviderManagerDialog.sourcesForSection(all,-1).size());for(int i=0;i<4;i++)assertEquals(1,ProviderManagerDialog.sourcesForSection(all,i).size());
    }
    @Test public void foundPlaylistPrefillIncludesEpgWithoutSavingUntilConfirmation()throws Exception{
        try(ActivityScenario<MainActivity> scene=launch()){
            CatalogRepository.FoundPlaylist playlist=new CatalogRepository.FoundPlaylist();playlist.name="Publiczne TV";playlist.source="https://public.example/list.m3u";playlist.text="#EXTM3U x-tvg-url=\"https://public.example/epg.xml\"\n#EXTINF:-1,News\nhttps://public.example/live.m3u8\n";playlist.fetchedAt=System.currentTimeMillis();PlaylistSuggestions.Entry entry=new PlaylistSuggestions.Entry(playlist,1,1,0);
            scene.onActivity(a->{call(a,"playlistResults",new Class<?>[]{ProviderRepository.class,String.class,List.class,List.class},get(a,"providers"),"News",Collections.singletonList(entry),Collections.singletonList("Fixture public playlist: 1 match"));AlertDialog results=(AlertDialog)get(a,"discoveryPage");assertNotNull(results.getWindow().getDecorView().findViewWithTag("playlist_discovery_results"));results.getWindow().getDecorView().findViewWithTag("playlist_result_1").performClick();((AlertDialog)get(a,"discoveryPage")).getButton(AlertDialog.BUTTON_POSITIVE).performClick();});InstrumentationRegistry.getInstrumentation().waitForIdleSync();scene.onActivity(a->{AlertDialog form=page(a);assertEquals("https://public.example/list.m3u",((EditText)form.getWindow().getDecorView().findViewWithTag("provider_url")).getText().toString());assertEquals("https://public.example/epg.xml",((EditText)form.getWindow().getDecorView().findViewWithTag("provider_epg")).getText().toString());form.dismiss();});assertEquals(1,fixture.listSources().size());
        }
    }
}
