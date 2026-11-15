package com.swir.stream;

import android.content.Context;
import android.os.SystemClock;
import android.view.View;
import android.view.ViewGroup;
import android.view.inputmethod.EditorInfo;
import android.widget.EditText;
import android.widget.ListView;
import android.widget.TextView;
import androidx.test.core.app.ActivityScenario;
import androidx.test.ext.junit.runners.AndroidJUnit4;
import androidx.test.platform.app.InstrumentationRegistry;
import org.json.JSONArray;
import org.json.JSONObject;
import org.junit.After;
import org.junit.Test;
import org.junit.runner.RunWith;
import java.io.File;
import java.io.IOException;
import java.lang.reflect.Field;
import java.lang.reflect.Method;
import java.net.URLDecoder;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.Collections;
import java.util.HashSet;
import java.util.List;
import java.util.Set;
import java.util.UUID;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicBoolean;
import java.util.concurrent.atomic.AtomicInteger;
import java.util.concurrent.atomic.AtomicReference;
import static org.junit.Assert.*;

/** Local-library UI regressions. Fixture URLs are never asserted to play video. */
@RunWith(AndroidJUnit4.class)
public final class LocalSearchUiTest {
    private Context context(){return InstrumentationRegistry.getInstrumentation().getTargetContext();}
    @After public void restoreCatalogue()throws Exception{UiTestData.restore(context());}

    private static Object field(Object target,String name){try{Field f=target.getClass().getDeclaredField(name);f.setAccessible(true);return f.get(target);}catch(Exception e){throw new AssertionError(e);}}
    private static void setField(Object target,String name,Object value){try{Field f=target.getClass().getDeclaredField(name);f.setAccessible(true);f.set(target,value);}catch(Exception e){throw new AssertionError(e);}}
    private static void countries(MainActivity activity,Set<String> values){try{Method m=MainActivity.class.getDeclaredMethod("setCountries",Set.class);m.setAccessible(true);m.invoke(activity,values);}catch(Exception e){throw new AssertionError(e);}}
    private static EditText search(MainActivity a){return a.getWindow().getDecorView().findViewWithTag("channel_search");}
    private static ListView list(MainActivity a){return a.getWindow().getDecorView().findViewWithTag("channel_list");}
    private static boolean busy(MainActivity a){return (Boolean)field(a,"busy");}
    private static View text(View root,String value){if(root instanceof TextView&&value.contentEquals(((TextView)root).getText()))return root;if(root instanceof ViewGroup){ViewGroup group=(ViewGroup)root;for(int i=0;i<group.getChildCount();i++){View found=text(group.getChildAt(i),value);if(found!=null)return found;}}return null;}
    private static void clickText(MainActivity a,String value){View button=text(a.getWindow().getDecorView(),value);assertNotNull("Visible tab: "+value,button);assertTrue(button.performClick());}
    private static Station station(String name,String id,String country,String kind,String suffix){Station s=new Station();s.name=name;s.id=s.tvgId=id;s.country=country;s.kind=kind;s.url="https://fixture.example/"+suffix+".m3u8";s.fetchedAt=System.currentTimeMillis();return s;}

    private ActivityScenario<MainActivity> launch(Station... additional)throws Exception{
        UiTestData.seed(context());CatalogRepository saved=new CatalogRepository(context());List<Station> rows=saved.load();rows.addAll(Arrays.asList(additional));saved.save(rows);
        ActivityScenario<MainActivity> scene=ActivityScenario.launch(MainActivity.class);AtomicBoolean ready=new AtomicBoolean();long end=SystemClock.elapsedRealtime()+10000;
        while(!ready.get()&&SystemClock.elapsedRealtime()<end){scene.onActivity(a->ready.set(list(a)!=null));SystemClock.sleep(50);}assertTrue("Cached catalogue opens",ready.get());return scene;
    }
    private static void awaitCount(ActivityScenario<MainActivity> scene,int expected,String... expectedNames){
        AtomicInteger actual=new AtomicInteger(-1);AtomicReference<List<String>> actualNames=new AtomicReference<>(Collections.emptyList());long end=SystemClock.elapsedRealtime()+4000;
        do{scene.onActivity(a->{ListView rows=list(a);actual.set(rows.getCount());List<String> names=new ArrayList<>();for(int i=0;i<rows.getCount();i++){MainActivity.Channel channel=(MainActivity.Channel)rows.getAdapter().getItem(i);for(Station station:channel.sources)names.add(station.name);}actualNames.set(names);});
            if(actual.get()==expected&&actualNames.get().containsAll(Arrays.asList(expectedNames)))return;SystemClock.sleep(50);
        }while(SystemClock.elapsedRealtime()<end);
        assertEquals("Filtered row count",expected,actual.get());assertTrue("Expected source names "+Arrays.toString(expectedNames)+" but found "+actualNames.get(),actualNames.get().containsAll(Arrays.asList(expectedNames)));
    }
    private static void query(ActivityScenario<MainActivity> scene,String value,int expected,String... expectedNames){scene.onActivity(a->search(a).setText(value));awaitCount(scene,expected,expectedNames);}
    private static void awaitFinished(ActivityScenario<MainActivity> scene){AtomicBoolean running=new AtomicBoolean(true);long end=SystemClock.elapsedRealtime()+15000;while(running.get()&&SystemClock.elapsedRealtime()<end){scene.onActivity(a->running.set(busy(a)));SystemClock.sleep(50);}assertFalse("Fixture search completes",running.get());InstrumentationRegistry.getInstrumentation().waitForIdleSync();}

    private static final class HeldWorker implements AutoCloseable {
        final CountDownLatch release=new CountDownLatch(1);
        HeldWorker(ActivityScenario<MainActivity> scene)throws Exception{AtomicReference<ExecutorService> executor=new AtomicReference<>();scene.onActivity(a->executor.set((ExecutorService)field(a,"work")));CountDownLatch entered=new CountDownLatch(1);executor.get().execute(()->{entered.countDown();try{release.await(15,TimeUnit.SECONDS);}catch(InterruptedException e){Thread.currentThread().interrupt();}});assertTrue("Worker barrier entered",entered.await(5,TimeUnit.SECONDS));}
        @Override public void close(){release.countDown();}
    }
    private CatalogRepository isolatedRepository(Fixture fetch)throws Exception{
        File folder=new File(context().getCacheDir(),"local-search-ui-"+UUID.randomUUID());CatalogRepository repository=new CatalogRepository(folder,fetch);
        repository.save(new CatalogRepository(context()).load());return repository;
    }
    static final class Fixture implements PublicHttp.Fetcher {
        final List<String> requests=Collections.synchronizedList(new ArrayList<>());
        @Override public PublicHttp.Document fetch(String url,AtomicBoolean cancelled)throws IOException{
            requests.add(url);PublicHttp.checkCancelled(cancelled);String body;
            if(url.contains("channels.json")){try{JSONArray rows=new JSONArray();for(int i=0;i<12;i++)rows.put(new JSONObject().put("id",i==0?"TVP1.pl":"Extra"+i+".pl").put("name",i==0?"TVP1":"Extra "+i).put("country","PL").put("website",i==0?"https://tvp1.example/":"").put("alt_names",new JSONArray()));body=rows.toString();}catch(Exception e){throw new IOException(e);}}
            else if(url.contains("bing.com"))body="<rss><channel/></rss>";
            else if(url.contains("duckduckgo.com"))body="<div class='no-results'>No results</div>";
            else if(url.endsWith("streams.json"))body="[{\"channel\":\"TVP1.pl\",\"title\":\"TVP1\",\"url\":\"https://additional.example/tvp1.m3u8\"}]";
            else if(url.endsWith("index.m3u")||url.contains("Free-TV/"))body="#EXTM3U\n#EXTINF:-1 tvg-id=\"TVP1.pl\",TVP1\nhttps://additional.example/tvp1.m3u8\n";
            else if(url.endsWith(".m3u8"))body="#EXTM3U\n#EXT-X-MEDIA-SEQUENCE:8\n#EXTINF:4,\nsegment.ts\n";
            else body="<title>TVP1</title>";
            return new PublicHttp.Document(url,body,"text/plain",System.currentTimeMillis(),0);
        }
    }

    @Test public void keyboardSearchFiltersSavedChannelsWithoutStartingDeepSearch()throws Exception{
        try(ActivityScenario<MainActivity> scene=launch()){
            Fixture fetch=new Fixture();CatalogRepository repository=isolatedRepository(fetch);scene.onActivity(a->setField(a,"catalog",repository));
            try(HeldWorker held=new HeldWorker(scene)){
                scene.onActivity(a->{search(a).setText("TVP1");search(a).onEditorAction(EditorInfo.IME_ACTION_SEARCH);assertFalse("Keyboard Search is local, even while worker is occupied",busy(a));});
                awaitCount(scene,1,"TVP 1");assertTrue("No discovery fetch was scheduled by keyboard Search",fetch.requests.isEmpty());
            }
        }
    }

    @Test public void partialWordsAndSeparatedNumbersMatchDownloadedNames()throws Exception{
        try(ActivityScenario<MainActivity> scene=launch(
            station("PL | Polsat HD","","PL","tv","polsat"),
            station("PolsatSport","","PL","tv","polsat-sport"),
            station("TVP1 Warszawa","TVP1Warszawa.pl","PL","tv","regional"),
            station("TVN24 BIS","TVN24BIS.pl","PL","tv","bis"))){
            query(scene,"pol",2,"PL | Polsat HD","PolsatSport");query(scene,"pol spo",1,"PolsatSport");query(scene,"TVP 1",2,"TVP 1","TVP1 Warszawa");query(scene,"TVP 10",1,"TVP 10");query(scene,"TVN 24",1,"TVN24 BIS");
            scene.onActivity(a->{assertFalse("Typing remains local",busy(a));assertEquals("TVN 24",search(a).getText().toString());});
        }
    }

    @Test public void savedUnknownCountryCanBeFoundWithoutChangingDiscoveryCountries()throws Exception{
        try(ActivityScenario<MainActivity> scene=launch(station("Imported Demo","","","tv","imported"),station("International Demo","International.de","DE","tv","foreign"))){
            query(scene,"Imported Demo",0);
            scene.onActivity(a->{View action=a.getWindow().getDecorView().findViewWithTag("show_all_downloaded");assertNotNull(action);assertTrue("Explain a match hidden by country",action.isShown());assertTrue(action.performClick());});
            awaitCount(scene,1,"Imported Demo");query(scene,"International Demo",1,"International Demo");
            scene.onActivity(a->{assertEquals("Explicit all-library tab",4,field(a,"tab"));assertEquals("Discovery countries must stay unchanged",Collections.singleton("PL"),field(a,"countries"));clickText(a,"TV");});
            awaitCount(scene,0);
        }
    }

    @Test public void radioHiddenByTvFilterIsRecoverableAndTabsRefilterImmediately()throws Exception{
        try(ActivityScenario<MainActivity> scene=launch()){
            query(scene,"testowy radio",0);
            scene.onActivity(a->{View action=a.getWindow().getDecorView().findViewWithTag("show_all_downloaded");assertNotNull(action);assertTrue(action.isShown());assertTrue(action.performClick());});awaitCount(scene,1,"Kanał testowy radio");
            scene.onActivity(a->clickText(a,"TV"));awaitCount(scene,0);
            scene.onActivity(a->clickText(a,"Radio"));awaitCount(scene,1,"Kanał testowy radio");
            scene.onActivity(a->clickText(a,"TV"));awaitCount(scene,0);
        }
    }

    @Test public void queuedDeepSearchKeepsItsRequestAndPreservesNewLocalQuery()throws Exception{
        try(ActivityScenario<MainActivity> scene=launch()){
            Fixture fetch=new Fixture();CatalogRepository repository=isolatedRepository(fetch);scene.onActivity(a->setField(a,"catalog",repository));
            try(HeldWorker held=new HeldWorker(scene)){
                scene.onActivity(a->{search(a).setText("TVP1");assertTrue(a.getWindow().getDecorView().findViewWithTag("deep_search").performClick());assertTrue(busy(a));search(a).setText("TVP10");countries(a,new HashSet<>(Collections.singleton("DE")));});
                awaitCount(scene,0);
            }
            awaitFinished(scene);awaitCount(scene,0);
            List<String> searches=new ArrayList<>();synchronized(fetch.requests){for(String request:fetch.requests)if(request.contains("bing.com")||request.contains("duckduckgo.com"))searches.add(URLDecoder.decode(request,"UTF-8"));}
            assertFalse("Actual fixture discovery requests were made",searches.isEmpty());assertTrue(searches.stream().anyMatch(q->q.contains("TVP1")&&q.contains("Poland")));
            assertTrue("Queued request uses its original query and countries",searches.stream().noneMatch(q->q.contains("TVP10")||q.contains("Germany")));
            scene.onActivity(a->{assertEquals("Later typing must not be overwritten on completion","TVP10",search(a).getText().toString());assertEquals(Collections.singleton("DE"),field(a,"countries"));View all=a.getWindow().getDecorView().findViewWithTag("tab_all");assertNotNull(all);assertTrue(all.performClick());});
            awaitCount(scene,1,"TVP 10");
            assertTrue("A narrow search must retain previously saved channels",repository.load().stream().anyMatch(s->s.name.equals("TVP 10")));
        }
    }

    @Test public void everySavedDisplayNameCanBeFoundInAllDownloaded()throws Exception{
        Station[] extra={station("TVP1 Warszawa","TVP1Warszawa.pl","PL","tv","regional"),station("PL | Polsat HD","","PL","tv","polsat"),station("Imported Demo","","","tv","imported"),station("DW English","DWEnglish.de","DE","tv","dw")};
        try(ActivityScenario<MainActivity> scene=launch(extra)){
            List<String> names=new ArrayList<>();for(Station station:new CatalogRepository(context()).load())names.add(station.name);
            scene.onActivity(a->{View all=a.getWindow().getDecorView().findViewWithTag("tab_all");assertNotNull(all);assertTrue(all.performClick());});
            for(String name:names){
                scene.onActivity(a->{search(a).setText(name);search(a).onEditorAction(EditorInfo.IME_ACTION_SEARCH);assertFalse(busy(a));});
                AtomicBoolean found=new AtomicBoolean();long end=SystemClock.elapsedRealtime()+4000;
                while(!found.get()&&SystemClock.elapsedRealtime()<end){scene.onActivity(a->{ListView rows=list(a);for(int i=0;i<rows.getCount();i++){MainActivity.Channel channel=(MainActivity.Channel)rows.getAdapter().getItem(i);if(channel.sources.stream().anyMatch(s->s.name.equals(name)))found.set(true);}});if(!found.get())SystemClock.sleep(50);}
                assertTrue("Saved display name must find that channel: "+name,found.get());
            }
        }
    }
}
