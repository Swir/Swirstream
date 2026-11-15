package com.swir.stream;

import android.content.Context;
import android.os.SystemClock;
import android.widget.EditText;
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
public final class ProviderIntegrationTest {
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
    @Test public void savedVaultLoadsInActivitySearchesEveryNameAndSurvivesRecreation()throws Exception{
        try(ActivityScenario<MainActivity> scene=launch()){
            scene.onActivity(a->{assertEquals(2,privateLibrary(a).size());assertEquals(5,rows(a));for(Station station:privateLibrary(a)){assertTrue(station.url.startsWith("swir://provider/"+first+"/"));set(a,"query",station.name);call(a,"filter",new Class<?>[]{});assertEquals("Every private channel name visible in real Activity",1,rows(a));}set(a,"query","");call(a,"filter",new Class<?>[]{});});
            scene.recreate();drain(scene,"work");drain(scene,"providerWork");scene.onActivity(a->{assertEquals(2,privateLibrary(a).size());assertEquals(first,((ProviderRepository)get(a,"providers")).profileId());assertEquals(5,rows(a));});
        }
    }
    @Test public void switchingProfilesClearsPreviousChannelsAndRestoresOnlyOwner()throws Exception{
        try(ActivityScenario<MainActivity> scene=launch()){
            scene.onActivity(a->{((ProfileStore)get(a,"profiles")).select(secondName);call(a,"profileChanged",new Class<?>[]{});assertTrue("Previous private library clears synchronously",privateLibrary(a).isEmpty());});drain(scene,"providerWork");
            scene.onActivity(a->{assertEquals(second,((ProviderRepository)get(a,"providers")).profileId());assertTrue(privateLibrary(a).isEmpty());assertEquals(3,rows(a));assertTrue(((ProfileStore)get(a,"profiles")).favorites().isEmpty());((ProfileStore)get(a,"profiles")).select(firstName);call(a,"profileChanged",new Class<?>[]{});});drain(scene,"providerWork");scene.onActivity(a->{assertEquals(2,privateLibrary(a).size());assertEquals(5,rows(a));});
        }
    }
    @Test public void failedRefreshKeepsVisibleChannelsAndRemovalClearsLoadedCache()throws Exception{
        try(ActivityScenario<MainActivity> scene=launch()){
            long at=fixture.listSources().get(0).updatedAt;offline.set(true);try{fixture.refresh(sourceId,new AtomicBoolean());fail("Fixture refresh must fail");}catch(IOException expected){}assertEquals(at,fixture.listSources().get(0).updatedAt);
            scene.onActivity(a->call(a,"reloadProviders",new Class<?>[]{boolean.class},false));drain(scene,"providerWork");scene.onActivity(a->assertEquals(2,privateLibrary(a).size()));
            fixture.remove(sourceId);scene.onActivity(a->call(a,"reloadProviders",new Class<?>[]{boolean.class},false));drain(scene,"providerWork");scene.onActivity(a->{assertTrue(privateLibrary(a).isEmpty());assertEquals(3,rows(a));});assertTrue(new ProviderRepository(context,first).stations().isEmpty());
        }
    }
    @Test public void favoriteHistoryAndAiPersistOnlyOpaqueReference()throws Exception{
        Station reference=fixture.stations().get(0),resolved=fixture.resolve(reference,new AtomicBoolean());assertTrue(resolved.url.contains("integration-secret"));
        try(ActivityScenario<MainActivity> scene=launch()){
            scene.onActivity(a->{Station saved=privateLibrary(a).stream().filter(s->s.key().equals(reference.key())).findFirst().orElseThrow(AssertionError::new);call(a,"favorite",new Class<?>[]{Station.class},saved);ProfileStore profiles=(ProfileStore)get(a,"profiles");
                // Exercise serialization's last line of defense as well as Activity's safe favorite path.
                profiles.watched(resolved);PersonalAi learning=(PersonalAi)get(a,"ai");learning.playback(saved,true,300);assertEquals(1,learning.preferenceCount());assertEquals(1,learning.sourceCount());assertEquals(saved.url,profiles.history().get(0).url);
            });
        }
        ProfileStore reopened=new ProfileStore(context);assertEquals(first,reopened.currentId());assertEquals(reference.key(),reopened.favorites().get(0).key());assertEquals(reference.url,reopened.history().get(0).url);assertEquals(1,new PersonalAi(context,first).sourceCount());assertNoSecret(new File(new File(context.getFilesDir(),"profiles"),first));assertNoSecret(new File(context.getFilesDir(),"catalog"));
        reopened.select(secondName);assertTrue(reopened.favorites().isEmpty());assertTrue(reopened.history().isEmpty());assertEquals(0,new PersonalAi(context,second).preferenceCount());
    }
    @Test public void aiAcceptsOwnedReferenceButRejectsForeignMalformedAndResolvedUrl()throws Exception{
        Station owned=fixture.stations().get(0);PersonalAi learning=new PersonalAi(context,first);
        Station foreign=Station.fromJson(owned.toJson());foreign.providerProfileId=second;foreign.url=foreign.providerReference();
        Station malformed=Station.fromJson(owned.toJson());malformed.providerChannelId="bad-channel";malformed.url=malformed.providerReference();
        Station mismatched=Station.fromJson(owned.toJson());mismatched.url="swir://provider/"+second+"/"+owned.providerId+"/"+owned.providerChannelId;
        Station transientStream=fixture.resolve(owned,new AtomicBoolean());
        for(Station invalid:new Station[]{foreign,malformed,mismatched,transientStream}){learning.preference(invalid,true);learning.playback(invalid,true,300);}
        assertEquals(0,learning.preferenceCount());assertEquals(0,learning.sourceCount());learning.preference(owned,true);learning.playback(owned,true,300);assertEquals(1,learning.preferenceCount());assertEquals(1,learning.sourceCount());
        PersonalAi other=new PersonalAi(context,second);other.preference(owned,true);other.playback(owned,true,300);assertEquals(0,other.preferenceCount());assertEquals(0,other.sourceCount());assertNoSecret(new File(new File(context.getFilesDir(),"profiles"),first));
    }
    private static void assertNoSecret(File file)throws Exception{if(!file.exists())return;if(file.isDirectory()){File[] children=file.listFiles();if(children!=null)for(File child:children)assertNoSecret(child);return;}try(FileInputStream input=new FileInputStream(file);ByteArrayOutputStream output=new ByteArrayOutputStream()){byte[] bytes=new byte[8192];int n;while((n=input.read(bytes))!=-1)output.write(bytes,0,n);String value=new String(output.toByteArray(),StandardCharsets.UTF_8);assertFalse("Plaintext app file: "+file.getName(),value.contains("integration-secret"));assertFalse("Private endpoint must not enter public files",value.contains("fixture.example"));}}
}
