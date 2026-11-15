package com.swir.stream;

import android.os.SystemClock;
import android.view.View;
import android.widget.EditText;
import android.widget.ListView;
import androidx.test.core.app.ActivityScenario;
import androidx.test.ext.junit.runners.AndroidJUnit4;
import androidx.test.platform.app.InstrumentationRegistry;
import org.junit.After;
import org.junit.Test;
import org.junit.runner.RunWith;
import java.lang.reflect.Field;
import java.lang.reflect.Method;
import java.util.ArrayList;
import java.util.Collections;
import java.util.List;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicBoolean;
import java.util.concurrent.atomic.AtomicReference;
import static org.junit.Assert.*;

@RunWith(AndroidJUnit4.class)
public final class ProviderUiTest {
    @After public void restore()throws Exception{UiTestData.restore(InstrumentationRegistry.getInstrumentation().getTargetContext());}
    private static Object get(Object target,String name){try{Field f=target.getClass().getDeclaredField(name);f.setAccessible(true);return f.get(target);}catch(Exception e){throw new AssertionError(e);}}
    private static void set(Object target,String name,Object value){try{Field f=target.getClass().getDeclaredField(name);f.setAccessible(true);f.set(target,value);}catch(Exception e){throw new AssertionError(e);}}
    private static Object call(MainActivity a,String name,Class<?>[] types,Object... args){try{Method m=MainActivity.class.getDeclaredMethod(name,types);m.setAccessible(true);return m.invoke(a,args);}catch(Exception e){throw new AssertionError(e);}}
    private ActivityScenario<MainActivity> launch()throws Exception{
        UiTestData.seed(InstrumentationRegistry.getInstrumentation().getTargetContext());ActivityScenario<MainActivity> scene=ActivityScenario.launch(MainActivity.class);AtomicBoolean ready=new AtomicBoolean();long end=SystemClock.elapsedRealtime()+10000;
        while(!ready.get()&&SystemClock.elapsedRealtime()<end){scene.onActivity(a->ready.set(a.getWindow().getDecorView().findViewWithTag("channel_list")!=null));SystemClock.sleep(50);}assertTrue(ready.get());
        CountDownLatch pending=new CountDownLatch(1);scene.onActivity(a->((ExecutorService)get(a,"providerWork")).execute(pending::countDown));assertTrue(pending.await(10,TimeUnit.SECONDS));InstrumentationRegistry.getInstrumentation().waitForIdleSync();return scene;
    }
    private Station provider(){Station s=new Station();s.name="Własny kanał Żółw HD";s.providerProfileId="default";s.providerId=String.join("",Collections.nCopies(32,"a"));s.providerChannelId=String.join("",Collections.nCopies(64,"b"));s.url=s.providerReference();s.group="Moje TV";s.origin="Test operator";return s;}
    private void add(MainActivity a,Station station){set(a,"providerLibrary",new ArrayList<>(Collections.singletonList(station)));set(a,"channels",call(a,"group",new Class<?>[]{List.class},get(a,"library")));call(a,"filter",new Class<?>[]{});}
    private int count(MainActivity a){return ((ListView)a.getWindow().getDecorView().findViewWithTag("channel_list")).getAdapter().getCount();}
    @Test public void ownChannelsWithoutCountryRemainSearchable(){
        try(ActivityScenario<MainActivity> scene=uncheckedLaunch()){
            scene.onActivity(a->{assertNotNull(a.getWindow().getDecorView().findViewWithTag("my_sources"));add(a,provider());assertEquals(4,count(a));set(a,"query","wlasny zolw");call(a,"filter",new Class<?>[]{});assertEquals("Own source not hidden by default PL filter",1,count(a));});
        }
    }
    private ActivityScenario<MainActivity> uncheckedLaunch(){try{return launch();}catch(Exception e){throw new AssertionError(e);}}
    @Test public void publicRefreshDoesNotOverwritePrivateLibrary(){
        try(ActivityScenario<MainActivity> scene=uncheckedLaunch()){
            scene.onActivity(a->{add(a,provider());call(a,"publish",new Class<?>[]{List.class,String.class},new ArrayList<Station>(),"test");});InstrumentationRegistry.getInstrumentation().waitForIdleSync();
            scene.onActivity(a->{assertEquals(1,count(a));assertTrue(((List<?>)get(a,"library")).isEmpty());assertEquals(1,((List<?>)get(a,"providerLibrary")).size());});
        }
    }
    @Test public void publicExportSkipsProviderReferences(){
        Station own=provider();Station publicStation=new Station();publicStation.name="Public";publicStation.url="https://example.com/live.m3u8";
        List<Station> rows=new ArrayList<>();rows.add(own);rows.add(publicStation);String m3u=M3u.export(rows);assertTrue(m3u.contains(publicStation.url));assertFalse(m3u.contains("swir://"));assertFalse(m3u.contains(own.name));
    }
}
