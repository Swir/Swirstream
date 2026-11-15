package com.swir.stream;

import android.content.Context;
import android.content.ContextWrapper;
import android.net.Uri;
import android.os.SystemClock;
import android.widget.TextView;
import androidx.media3.common.util.UnstableApi;
import androidx.media3.ui.PlayerView;
import androidx.test.core.app.ActivityScenario;
import androidx.test.ext.junit.runners.AndroidJUnit4;
import androidx.test.platform.app.InstrumentationRegistry;
import org.json.JSONObject;
import org.junit.After;
import org.junit.Test;
import org.junit.runner.RunWith;
import java.io.*;
import java.lang.reflect.*;
import java.nio.charset.StandardCharsets;
import java.util.*;
import java.util.concurrent.atomic.*;
import static org.junit.Assert.*;

/** Real renderer clocks and public ClearKey fixture, never an operator account. */
@UnstableApi @RunWith(AndroidJUnit4.class)
public final class PlaybackEvidenceInstrumentationTest {
    private boolean catalogueSeeded;
    static final String WRONG_KEY="AAECAwQFBgcICQoLDA0ODw";
    static final String KID="_u3wDe7erb7v8Lqt8A3QDQ";
    static final String WRONG_LICENSE="https://cwip-shaka-proxy.appspot.com/clearkey?"+KID+"="+WRONG_KEY;
    private Context context(){return InstrumentationRegistry.getInstrumentation().getTargetContext();}
    @After public void restore()throws Exception{UiTestData.restore(context());catalogueSeeded=false;}
    private static Object get(Object target,String name){try{Field f=target.getClass().getDeclaredField(name);f.setAccessible(true);return f.get(target);}catch(Exception e){throw new AssertionError(e);}}
    private static void set(Object target,String name,Object value){try{Field f=target.getClass().getDeclaredField(name);f.setAccessible(true);f.set(target,value);}catch(Exception e){throw new AssertionError(e);}}
    private SwirPlayer player(MainActivity a){return (SwirPlayer)get(a,"player");}
    private ActivityScenario<MainActivity> launch()throws Exception{
        if(!catalogueSeeded){UiTestData.seed(context());catalogueSeeded=true;}ActivityScenario<MainActivity> scene=ActivityScenario.launch(MainActivity.class);AtomicBoolean ready=new AtomicBoolean();long until=SystemClock.elapsedRealtime()+15000;
        while(!ready.get()&&SystemClock.elapsedRealtime()<until){scene.onActivity(a->ready.set(a.getWindow().getDecorView().findViewWithTag("swir_player")!=null));SystemClock.sleep(50);}assertTrue(ready.get());return scene;
    }
    private Station local(String name)throws Exception{
        File file=new File(context().getCacheDir(),name);try(InputStream input=InstrumentationRegistry.getInstrumentation().getContext().getAssets().open(name);OutputStream out=new FileOutputStream(file)){byte[] b=new byte[8192];int n;while((n=input.read(b))!=-1)out.write(b,0,n);}
        Station station=new Station();station.id="clock-fixture";station.name="Synthetic renderer fixture";station.kind="tv";station.url=Uri.fromFile(file).toString();return station;
    }
    private void attach(MainActivity a,Station station,boolean muted,AtomicBoolean learned){
        PlayerView view=a.getWindow().getDecorView().findViewWithTag("swir_player");SwirPlayer p=new SwirPlayer(a,view,station,"Stabilność",new SwirPlayer.Listener(){public void state(String text){}public void confirmed(Station s,long ms){}public void failed(Station s,String reason,long ms){}public void watchedThirtySeconds(Station s){learned.set(true);}});set(a,"player",p);if(muted)p.mute();
    }
    private JSONObject observeLocal(String file,boolean video,boolean audio,boolean muted)throws Exception{
        Station station=local(file);AtomicBoolean learned=new AtomicBoolean();AtomicLong watched=new AtomicLong();AtomicBoolean confirmed=new AtomicBoolean();JSONObject result=new JSONObject();
        try(ActivityScenario<MainActivity> scene=launch()){
            scene.onActivity(a->attach(a,station,muted,learned));long until=SystemClock.elapsedRealtime()+12000;
            while(watched.get()<1500&&SystemClock.elapsedRealtime()<until){SystemClock.sleep(100);scene.onActivity(a->{SwirPlayer p=player(a);assertFalse("Synthetic valid media must not fail",p.isFailed());confirmed.set(p.isConfirmed());watched.set(p.watchedMillis());});}
            assertTrue("Actual selected renderer progress confirms",confirmed.get());assertTrue(watched.get()>=1500);scene.onActivity(a->{assertEquals(video,player(a).selectedVideo());assertEquals(audio,player(a).selectedAudio());});
            result.put("asset",file).put("videoSelected",video).put("audioSelected",audio).put("muted",muted).put("confirmed",true).put("movingSelectedTracksMs",watched.get());
        }return result;
    }
    @Test public void blackMutedVideoAndAudioStillConfirm()throws Exception{write("playback-black-muted.json",observeLocal("player-black-av.mp4",true,true,true));}
    @Test public void audioOnlyAndStaticVideoOnlyUseSelectedTracks()throws Exception{
        JSONObject evidence=new JSONObject().put("audioOnly",observeLocal("player-audio-only.m4a",false,true,false)).put("staticVideoOnly",observeLocal("player-static-video.mp4",true,false,false));write("playback-single-tracks.json",evidence);
    }
    @Test public void stoppedAudioWithContinuingVideoCannotAccrueTvLearning()throws Exception{
        Station station=local("player-stalled-audio.mp4");AtomicBoolean learned=new AtomicBoolean();AtomicLong watched=new AtomicLong(),video=new AtomicLong(),audio=new AtomicLong(),initialVideo=new AtomicLong(),readyAfter=new AtomicLong();
        try(ActivityScenario<MainActivity> scene=launch()){
            scene.onActivity(a->attach(a,station,false,learned));AtomicBoolean rendered=new AtomicBoolean();long startupStart=SystemClock.elapsedRealtime(),startupDeadline=startupStart+12000;
            while(!rendered.get()&&SystemClock.elapsedRealtime()<startupDeadline){SystemClock.sleep(50);scene.onActivity(a->{SwirPlayer p=player(a);assertFalse("Fixture startup must not fail",p.isFailed());rendered.set(p.selectedVideo()&&p.selectedAudio()&&p.verifiedVideoMillis()>0&&p.verifiedAudioMillis()>0);if(rendered.get())initialVideo.set(p.verifiedVideoMillis());});}assertTrue("Actual selected video starts before observation",rendered.get());readyAfter.set(SystemClock.elapsedRealtime()-startupStart);
            SystemClock.sleep(4200);scene.onActivity(a->{SwirPlayer p=player(a);assertTrue(p.selectedVideo());assertTrue(p.selectedAudio());watched.set(p.watchedMillis());video.set(p.verifiedVideoMillis());audio.set(p.verifiedAudioMillis());});assertTrue("Video really continues",video.get()>2000);assertTrue("Ended audio cannot accrue video-only watch",watched.get()<1600);assertFalse(learned.get());
            assertTrue("Video continues during observation window",video.get()-initialVideo.get()>2000);
        }write("playback-stalled-audio.json",new JSONObject().put("videoProgressMs",video.get()).put("audioProgressMs",audio.get()).put("watchMs",watched.get()).put("watchPreference",learned.get()).put("readinessDelayMs",readyAfter.get()).put("observationWindowMs",4200).put("videoWindowDeltaMs",video.get()-initialVideo.get()));
    }
    /** Diagnostic only: retains the original 4.2-second predicate, with no altered runtime or success threshold. */
    @Test public void stalledAudioClockDiagnosticOnly()throws Exception{
        Station station=local("player-stalled-audio.mp4");AtomicBoolean learned=new AtomicBoolean();org.json.JSONArray samples=new org.json.JSONArray();AtomicReference<JSONObject> atOriginalDeadline=new AtomicReference<>();
        try(ActivityScenario<MainActivity> scene=launch()){
            scene.onActivity(a->attach(a,station,false,learned));long start=SystemClock.elapsedRealtime();
            while(SystemClock.elapsedRealtime()-start<8500){SystemClock.sleep(200);scene.onActivity(a->{try{SwirPlayer p=player(a);androidx.media3.exoplayer.ExoPlayer nativePlayer=(androidx.media3.exoplayer.ExoPlayer)get(p,"player");long elapsed=SystemClock.elapsedRealtime()-start;JSONObject row=new JSONObject().put("elapsedMs",elapsed).put("selectedVideo",p.selectedVideo()).put("selectedAudio",p.selectedAudio()).put("playing",p.isPlaying()).put("nativeState",nativePlayer.getPlaybackState()).put("nativePositionMs",p.position()).put("rawVideoUs",((AtomicLong)get(p,"videoPresentationUs")).get()).put("rawAudioUs",((AtomicLong)get(p,"audioPositionUs")).get()).put("verifiedVideoMs",p.verifiedVideoMillis()).put("verifiedAudioMs",p.verifiedAudioMillis()).put("watchedMs",p.watchedMillis()).put("confirmed",p.isConfirmed()).put("failed",p.isFailed()).put("learned",learned.get());samples.put(row);if(elapsed>=4200&&atOriginalDeadline.get()==null)atOriginalDeadline.set(row);}catch(Exception error){throw new AssertionError(error);}});}
        }write("playback-stalled-audio-diagnostic.json",new JSONObject().put("diagnosticOnly",true).put("originalThresholdVideoMs",2000).put("originalDeadlineMs",4200).put("atOriginalDeadline",atOriginalDeadline.get()).put("samples",samples));
    }
    @Test public void naturalShortEndDoesNotBecomeTimeoutFailure()throws Exception{
        Station station=local("player-short-ended.mp4");AtomicBoolean ended=new AtomicBoolean(),failed=new AtomicBoolean(),confirmed=new AtomicBoolean(),learned=new AtomicBoolean();AtomicReference<String> state=new AtomicReference<>("");
        try(ActivityScenario<MainActivity> scene=launch()){
            scene.onActivity(a->{PlayerView view=a.getWindow().getDecorView().findViewWithTag("swir_player");SwirPlayer p=new SwirPlayer(a,view,station,"Stabilność",new SwirPlayer.Listener(){public void state(String text){state.set(text);}public void confirmed(Station s,long ms){confirmed.set(true);}public void failed(Station s,String reason,long ms){failed.set(true);}public void watchedThirtySeconds(Station s){learned.set(true);}});set(a,"player",p);});
            long until=SystemClock.elapsedRealtime()+10000;while(!ended.get()&&SystemClock.elapsedRealtime()<until){SystemClock.sleep(50);scene.onActivity(a->ended.set(((androidx.media3.exoplayer.ExoPlayer)get(player(a),"player")).getPlaybackState()==androidx.media3.common.Player.STATE_ENDED));}
            assertTrue("Real short file reaches native ENDED",ended.get());assertFalse("Too short for proof",confirmed.get());scene.onActivity(a->{assertTrue("Ended source still has playWhenReady",player(a).wantsPlayback());set(player(a),"activeStartupMs",36001L);});
            SystemClock.sleep(1100);scene.onActivity(a->{assertFalse("Natural end must not turn into timeout",player(a).isFailed());assertEquals(0,player(a).watchedMillis());assertEquals(androidx.media3.common.Player.STATE_ENDED,((androidx.media3.exoplayer.ExoPlayer)get(player(a),"player")).getPlaybackState());});assertFalse(failed.get());assertFalse(learned.get());assertTrue(state.get().contains("zakończona"));
        }write("playback-natural-ended.json",new JSONObject().put("nativeEnded",ended.get()).put("budgetForcedMs",36001).put("confirmed",confirmed.get()).put("failedCallback",failed.get()).put("watchPreference",learned.get()).put("watchMs",0).put("naturalEndStatus",true));
    }
    private void startThroughProduction(MainActivity a,Station station){
        try{MainActivity.Channel channel=new MainActivity.Channel();channel.sources.add(station);Method method=MainActivity.class.getDeclaredMethod("playChannel",MainActivity.Channel.class,Station.class);method.setAccessible(true);method.invoke(a,channel,station);}catch(Exception e){throw new AssertionError(e);}
    }
    private static int positiveSignals(Context isolated,String profile)throws Exception{
        File file=new File(ProfileStore.directory(isolated,profile),"personal-ai.json");if(!ProfileStore.hasSnapshot(file))return 0;JSONObject signals=ProfileStore.readJson(file,2*1024*1024).getJSONObject("sourceSignals");int count=0;Iterator<String> keys=signals.keys();while(keys.hasNext())if(signals.getJSONObject(keys.next()).getBoolean("positive"))count++;return count;
    }
    @Test public void syntacticallyValidWrongClearKeyCannotTeachOrEnterHistoryAndGoodKeyRecovers()throws Exception{
        assertEquals("true",InstrumentationRegistry.getArguments().getString("network"));
        okhttp3.Request request=new okhttp3.Request.Builder().url(WRONG_LICENSE).post(okhttp3.RequestBody.create("{\"kids\":[\""+KID+"\"],\"type\":\"temporary\"}",okhttp3.MediaType.get("application/json"))).build();String body;
        try(okhttp3.Response response=ProviderHttp.client(context()).newCall(request).execute()){assertEquals("Fixture license POST must succeed",200,response.code());assertNotNull(response.body());body=response.body().string();assertTrue(body.length()<16384);}
        JSONObject key=new JSONObject(body).getJSONArray("keys").getJSONObject(0);assertEquals("oct",key.getString("kty"));assertEquals(KID,key.getString("kid").replace("=",""));assertEquals(WRONG_KEY,key.getString("k").replace("=",""));
        File isolatedRoot=new File(context().getCacheDir(),"drm-evidence-"+UUID.randomUUID());assertTrue(isolatedRoot.mkdirs());Context isolated=new ContextWrapper(context()){@Override public File getFilesDir(){return isolatedRoot;}};ProfileStore profiles=new ProfileStore(isolated);PersonalAi ai=new PersonalAi(isolated,profiles.currentId());
        Station station=new Station();station.id="public-clearkey-fixture";station.name="Official ClearKey fixture";station.url=ProviderPlaybackTest.MPD;station.drmScheme="clearkey";station.drmLicenseUrl=WRONG_LICENSE;
        AtomicBoolean failed=new AtomicBoolean(),stopped=new AtomicBoolean(),confirmed=new AtomicBoolean();AtomicLong negativeWatch=new AtomicLong(),goodVideo=new AtomicLong(),goodAudio=new AtomicLong(),goodWatch=new AtomicLong();String[] ui={""};long started=SystemClock.elapsedRealtime();
        try(ActivityScenario<MainActivity> scene=launch()){
            scene.onActivity(a->{set(a,"profiles",profiles);set(a,"ai",ai);startThroughProduction(a,station);});long until=SystemClock.elapsedRealtime()+40000;
            while(!failed.get()&&SystemClock.elapsedRealtime()<until){SystemClock.sleep(100);scene.onActivity(a->{SwirPlayer p=player(a);confirmed.set(confirmed.get()||p.isConfirmed());negativeWatch.set(Math.max(negativeWatch.get(),p.watchedMillis()));failed.set(p.isFailed());stopped.set(!p.isPlaying());ui[0]=((TextView)get(a,"playStatus")).getText().toString();});assertFalse("Wrong JWK must never confirm before/after clear lead",confirmed.get());assertTrue("Wrong JWK must never enter history",profiles.history().isEmpty());assertEquals(0,ai.preferenceCount());assertEquals(0,positiveSignals(isolated,profiles.currentId()));}
            assertTrue("Wrong key surfaces error or verification timeout",failed.get());assertTrue("Wrong key stops playback",stopped.get());assertEquals("Unconfirmed DRM cannot accrue watch",0,negativeWatch.get());InstrumentationRegistry.getInstrumentation().waitForIdleSync();scene.onActivity(a->ui[0]=((TextView)get(a,"playStatus")).getText().toString());assertTrue("Failure is visible",ui[0].contains("niedostępne")||ui[0].contains("Nie potwierdzono"));
            long failedAfter=SystemClock.elapsedRealtime()-started;Station good=Station.fromJson(station.toJson());good.drmScheme="clearkey";good.drmLicenseUrl=ProviderPlaybackTest.LICENSE;scene.onActivity(a->startThroughProduction(a,good));until=SystemClock.elapsedRealtime()+40000;confirmed.set(false);
            while(!confirmed.get()&&SystemClock.elapsedRealtime()<until){SystemClock.sleep(100);scene.onActivity(a->{SwirPlayer p=player(a);assertFalse("Correct key recovers after wrong one",p.isFailed());confirmed.set(p.isConfirmed());goodVideo.set(p.verifiedVideoMillis());goodAudio.set(p.verifiedAudioMillis());goodWatch.set(p.watchedMillis());if(!p.isConfirmed()){assertTrue(profiles.history().isEmpty());assertEquals(0,ai.preferenceCount());}});}
            assertTrue("Good key confirmed beyond policy window",confirmed.get());assertTrue(goodVideo.get()>=12000);assertTrue(goodAudio.get()>=12000);assertTrue(goodWatch.get()>=12000);assertEquals(1,profiles.history().size());assertEquals(1,positiveSignals(isolated,profiles.currentId()));assertEquals(0,ai.preferenceCount());
            write("provider-wrong-key-recovery.json",new JSONObject().put("licenseHttpAndJsonValid",true).put("sameKidWrong16ByteKey",true).put("badConfirmed",false).put("badHistoryCount",0).put("badPositiveSourceSignals",0).put("badWatchPreferences",0).put("badWatchMs",negativeWatch.get()).put("badFailed",failed.get()).put("badStopped",stopped.get()).put("badElapsedMs",failedAfter).put("goodRecovered",confirmed.get()).put("goodVideoMs",goodVideo.get()).put("goodAudioMs",goodAudio.get()).put("goodSharedWatchMs",goodWatch.get()).put("goodHistoryCount",profiles.history().size()).put("confirmationPolicyMs",12000).put("policyDoesNotProveEveryClearLead",true));
        }
    }
    private void write(String name,JSONObject value)throws Exception{value.put("verifiedAt",System.currentTimeMillis()).put("sdk",android.os.Build.VERSION.SDK_INT);File folder=new File(context().getExternalFilesDir(null),"evidence");assertTrue(folder.isDirectory()||folder.mkdirs());try(FileOutputStream out=new FileOutputStream(new File(folder,name))){out.write(value.toString(2).getBytes(StandardCharsets.UTF_8));}}
}
