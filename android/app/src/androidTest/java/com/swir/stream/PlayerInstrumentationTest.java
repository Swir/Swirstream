package com.swir.stream;

import android.content.Context;
import android.content.ContextWrapper;
import android.content.Intent;
import android.net.Uri;
import android.os.SystemClock;
import android.view.WindowManager;
import androidx.lifecycle.Lifecycle;
import androidx.test.core.app.ActivityScenario;
import androidx.test.ext.junit.runners.AndroidJUnit4;
import androidx.test.platform.app.InstrumentationRegistry;
import androidx.media3.common.util.UnstableApi;
import androidx.media3.ui.PlayerView;
import org.junit.Test;
import org.junit.After;
import org.junit.runner.RunWith;
import java.io.*;
import java.util.concurrent.atomic.AtomicBoolean;
import java.util.concurrent.atomic.AtomicLong;
import static org.junit.Assert.*;

@UnstableApi
@RunWith(AndroidJUnit4.class)
public final class PlayerInstrumentationTest {
    @After public void restoreCatalogue()throws Exception{UiTestData.restore(InstrumentationRegistry.getInstrumentation().getTargetContext());}
    private File media(String name)throws Exception{
        Context tests=InstrumentationRegistry.getInstrumentation().getContext();File file=new File(InstrumentationRegistry.getInstrumentation().getTargetContext().getCacheDir(),name);
        try(InputStream input=tests.getAssets().open(name);OutputStream output=new FileOutputStream(file)){byte[] bytes=new byte[16384];int n;while((n=input.read(bytes))!=-1)output.write(bytes,0,n);}return file;
    }
    private ActivityScenario<MainActivity> launch()throws Exception{
        UiTestData.seed(InstrumentationRegistry.getInstrumentation().getTargetContext());ActivityScenario<MainActivity> scenario=ActivityScenario.launch(MainActivity.class);AtomicBoolean ready=new AtomicBoolean();long end=SystemClock.elapsedRealtime()+10000;
        while(SystemClock.elapsedRealtime()<end&&!ready.get()){scenario.onActivity(a->ready.set(a.getWindow().getDecorView().findViewWithTag("swir_player")!=null));SystemClock.sleep(100);}assertTrue("Cached catalogue gate completes",ready.get());return scenario;
    }
    private SwirPlayer nativePlayer(MainActivity activity){try{java.lang.reflect.Field field=MainActivity.class.getDeclaredField("player");field.setAccessible(true);return (SwirPlayer)field.get(activity);}catch(Exception e){throw new AssertionError(e);}}
    private void attach(MainActivity activity,Station station,SwirPlayer.Listener listener){try{PlayerView video=activity.getWindow().getDecorView().findViewWithTag("swir_player");SwirPlayer player=new SwirPlayer(activity,video,station,"Stabilność",listener);java.lang.reflect.Field field=MainActivity.class.getDeclaredField("player");field.setAccessible(true);field.set(activity,player);}catch(Exception e){throw new AssertionError(e);}}
    private Station station(File file){Station station=new Station();station.id="native-test";station.name="Local decoder test";station.kind="tv";station.url=Uri.fromFile(file).toString();return station;}
    private void startThroughActivity(MainActivity activity,Station station){try{
        // Exercise the production listener without adding local test media to the user's profile.
        File directory=new File(activity.getCacheDir(),"screen-flag-test-"+SystemClock.elapsedRealtime());assertTrue(directory.mkdirs());
        Context isolated=new ContextWrapper(activity){@Override public File getFilesDir(){return directory;}};
        ProfileStore profiles=new ProfileStore(isolated);java.lang.reflect.Field profileField=MainActivity.class.getDeclaredField("profiles"),aiField=MainActivity.class.getDeclaredField("ai");profileField.setAccessible(true);aiField.setAccessible(true);profileField.set(activity,profiles);aiField.set(activity,new PersonalAi(isolated,profiles.currentId()));
        MainActivity.Channel channel=new MainActivity.Channel();channel.sources.add(station);java.lang.reflect.Method play=MainActivity.class.getDeclaredMethod("playChannel",MainActivity.Channel.class,Station.class);play.setAccessible(true);play.invoke(activity,channel,station);
    }catch(Exception e){throw new AssertionError(e);}}
    private boolean waitScreenState(ActivityScenario<MainActivity> activity,boolean active,long timeout){AtomicBoolean ready=new AtomicBoolean();long end=SystemClock.elapsedRealtime()+timeout;while(!ready.get()&&SystemClock.elapsedRealtime()<end){activity.onActivity(a->{SwirPlayer p=nativePlayer(a);boolean screen=(a.getWindow().getAttributes().flags&WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)!=0;ready.set(p!=null&&p.isConfirmed()&&p.isPlaying()==active&&screen==active);});SystemClock.sleep(100);}return ready.get();}
    private static boolean until(AtomicBoolean condition,long millis){long end=SystemClock.elapsedRealtime()+millis;while(SystemClock.elapsedRealtime()<end&&!condition.get())SystemClock.sleep(100);return condition.get();}
    @Test public void rendersActualVideoThenPauseFreezesProgress()throws Exception{
        Station station=station(media("local-test.mp4"));AtomicBoolean frame=new AtomicBoolean(),failure=new AtomicBoolean();AtomicLong watched=new AtomicLong(),position=new AtomicLong();
        try(ActivityScenario<MainActivity> activity=launch()){
            activity.onActivity(a->attach(a,station,new SwirPlayer.Listener(){public void state(String s){}public void confirmed(Station s,long ms){frame.set(true);}public void failed(Station s,String m,long ms){failure.set(true);}public void watchedThirtySeconds(Station s){}}));
            assertTrue("Real first-frame callback",until(frame,10000));assertFalse(failure.get());SystemClock.sleep(1200);
            activity.onActivity(a->nativePlayer(a).pause());
            long deadline=SystemClock.elapsedRealtime()+3000;AtomicLong last=new AtomicLong(-1),stable=new AtomicLong();
            while(SystemClock.elapsedRealtime()<deadline&&stable.get()<4){SystemClock.sleep(100);activity.onActivity(a->{SwirPlayer p=nativePlayer(a);long now=p.position();stable.set(!p.isPlaying()&&!p.wantsPlayback()&&last.get()>=0&&Math.abs(now-last.get())<=20?stable.get()+1:0);last.set(now);});}
            assertTrue("Pause command must settle and stop advancing within 3 seconds",stable.get()>=4);
            activity.onActivity(a->{position.set(nativePlayer(a).position());watched.set(nativePlayer(a).watchedMillis());});SystemClock.sleep(1200);
            activity.onActivity(a->{assertTrue("Paused clock stays frozen: "+position.get()+" to "+nativePlayer(a).position(),Math.abs(nativePlayer(a).position()-position.get())<50);assertEquals("Paused frames cannot teach preferences",watched.get(),nativePlayer(a).watchedMillis());});
        }
    }
    @Test public void stoppedVideoWithContinuingAudioCannotAccrueTvLearning()throws Exception{
        Station station=station(media("player-stalled-video.mp4"));AtomicBoolean frame=new AtomicBoolean(),learned=new AtomicBoolean();AtomicLong watched=new AtomicLong();
        try(ActivityScenario<MainActivity> activity=launch()){
            activity.onActivity(a->attach(a,station,new SwirPlayer.Listener(){public void state(String s){}public void confirmed(Station s,long ms){frame.set(true);}public void failed(Station s,String m,long ms){}public void watchedThirtySeconds(Station s){learned.set(true);}}));
            assertTrue(until(frame,10000));SystemClock.sleep(4200);activity.onActivity(a->watched.set(nativePlayer(a).watchedMillis()));
            assertTrue("Only advancing video contributes, not four seconds of continuing audio",watched.get()<2000);assertFalse(learned.get());
        }
    }
    @Test public void productionPlaybackKeepsScreenOnAfterPauseAndResume()throws Exception{
        Station station=station(media("local-test.mp4"));
        try(ActivityScenario<MainActivity> activity=launch()){
            activity.onActivity(a->startThroughActivity(a,station));assertTrue("Actual playing video keeps the screen awake",waitScreenState(activity,true,10000));
            activity.onActivity(a->nativePlayer(a).pause());assertTrue("Pause releases the screen flag",waitScreenState(activity,false,3000));
            activity.onActivity(a->nativePlayer(a).toggle());assertTrue("Resume restores the screen flag without another first-frame confirmation",waitScreenState(activity,true,3000));
            activity.onActivity(a->{try{java.lang.reflect.Method stop=MainActivity.class.getDeclaredMethod("stopPlayback");stop.setAccessible(true);stop.invoke(a);}catch(Exception e){throw new AssertionError(e);}assertEquals("Stop clears the screen flag",0,a.getWindow().getAttributes().flags&WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);});
        }
    }
    @Test public void leavingForegroundReleasesPlayback()throws Exception{
        Station station=station(media("local-test.mp4"));AtomicBoolean frame=new AtomicBoolean();AtomicLong before=new AtomicLong(),after=new AtomicLong();
        try(ActivityScenario<MainActivity> activity=launch()){
            activity.onActivity(a->attach(a,station,new SwirPlayer.Listener(){public void state(String s){}public void confirmed(Station s,long ms){frame.set(true);}public void failed(Station s,String m,long ms){}public void watchedThirtySeconds(Station s){}}));
            assertTrue("First frame before lifecycle transition",until(frame,10000));java.util.concurrent.atomic.AtomicReference<SwirPlayer> released=new java.util.concurrent.atomic.AtomicReference<>();activity.onActivity(a->released.set(nativePlayer(a)));activity.moveToState(Lifecycle.State.CREATED);
            assertTrue("onStop releases native player",released.get().isClosed());assertFalse(released.get().isPlaying());before.set(released.get().watchedMillis());SystemClock.sleep(1100);after.set(released.get().watchedMillis());assertEquals("Released player cannot accrue background watch time",before.get(),after.get());
        }
    }
}
