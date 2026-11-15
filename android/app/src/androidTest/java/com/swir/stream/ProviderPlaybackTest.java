package com.swir.stream;

import android.content.Context;
import android.os.SystemClock;
import androidx.media3.common.util.UnstableApi;
import androidx.media3.ui.PlayerView;
import androidx.test.core.app.ActivityScenario;
import androidx.test.ext.junit.runners.AndroidJUnit4;
import androidx.test.platform.app.InstrumentationRegistry;
import org.json.JSONObject;
import org.junit.After;
import org.junit.Test;
import org.junit.runner.RunWith;
import java.io.File;
import java.io.FileOutputStream;
import java.lang.reflect.Field;
import java.nio.charset.StandardCharsets;
import java.util.Collections;
import java.util.concurrent.atomic.AtomicBoolean;
import java.util.concurrent.atomic.AtomicLong;
import java.util.concurrent.atomic.AtomicReference;
import static org.junit.Assert.*;

/** Official Google/Shaka licensed sample, not operator or public-log credentials. */
@UnstableApi @RunWith(AndroidJUnit4.class)
public final class ProviderPlaybackTest {
    static final String MPD="https://storage.googleapis.com/shaka-demo-assets/angel-one-clearkey/dash.mpd";
    static final String LICENSE="https://cwip-shaka-proxy.appspot.com/clearkey?_u3wDe7erb7v8Lqt8A3QDQ=ABEiM0RVZneImaq7zN3u_w";
    private Context context(){return InstrumentationRegistry.getInstrumentation().getTargetContext();}
    @After public void restore()throws Exception{UiTestData.restore(context());}
    private static void set(MainActivity a,String name,Object value){try{Field f=MainActivity.class.getDeclaredField(name);f.setAccessible(true);f.set(a,value);}catch(Exception e){throw new AssertionError(e);}}
    private ActivityScenario<MainActivity> launch()throws Exception{
        UiTestData.seed(context());ActivityScenario<MainActivity> scene=ActivityScenario.launch(MainActivity.class);AtomicBoolean ready=new AtomicBoolean();long end=SystemClock.elapsedRealtime()+10000;
        while(!ready.get()&&SystemClock.elapsedRealtime()<end){scene.onActivity(a->ready.set(a.getWindow().getDecorView().findViewWithTag("swir_player")!=null));SystemClock.sleep(50);}assertTrue(ready.get());return scene;
    }
    @Test public void officialClearKeyLicenseProducesMovingVideo()throws Exception{
        assertEquals("Must explicitly request network test", "true", InstrumentationRegistry.getArguments().getString("network"));
        Station station=new Station();station.name="Official Shaka ClearKey test";station.url=MPD;station.drmScheme="clearkey";station.drmLicenseUrl=LICENSE;
        AtomicBoolean confirmed=new AtomicBoolean();AtomicLong position=new AtomicLong(),watched=new AtomicLong(),startup=new AtomicLong(),video=new AtomicLong(),audio=new AtomicLong();AtomicReference<String> failure=new AtomicReference<>("");
        try(ActivityScenario<MainActivity> scene=launch()){
            scene.onActivity(a->{PlayerView view=a.getWindow().getDecorView().findViewWithTag("swir_player");SwirPlayer p=new SwirPlayer(a,view,station,"Oszczędzaj transfer",new SwirPlayer.Listener(){public void state(String text){}public void confirmed(Station s,long ms){startup.set(ms);confirmed.set(true);}public void watchedThirtySeconds(Station s){}public void failed(Station s,String message,long ms){failure.set(message);}});set(a,"player",p);});
            long until=SystemClock.elapsedRealtime()+20000;while(!confirmed.get()&&failure.get().isEmpty()&&SystemClock.elapsedRealtime()<until)SystemClock.sleep(100);
            assertTrue("License and decoded first video frame: "+failure.get(),confirmed.get());
            long encryptedDeadline=SystemClock.elapsedRealtime()+20000;
            while((position.get()<12000||watched.get()<10000)&&failure.get().isEmpty()&&SystemClock.elapsedRealtime()<encryptedDeadline){
                SystemClock.sleep(200);scene.onActivity(a->{try{Field f=MainActivity.class.getDeclaredField("player");f.setAccessible(true);SwirPlayer p=(SwirPlayer)f.get(a);assertTrue("MediaDrm accepted the license keys",p.hasDrmKeys());position.set(p.position());watched.set(p.watchedMillis());video.set(p.verifiedVideoMillis());audio.set(p.verifiedAudioMillis());assertTrue(p.selectedVideo());assertTrue(p.selectedAudio());}catch(Exception e){throw new AssertionError(e);}});
            }
            // This exact Shaka MP4 has clear fragments through 8s. Advancing video,
            // not just the media/audio clock, must continue through encrypted ones.
            assertTrue("Video advances beyond the 8-second clear lead",watched.get()>=10000);assertTrue(position.get()>=12000);assertTrue(video.get()>=12000);assertTrue(audio.get()>=12000);assertTrue(failure.get().isEmpty());
        }
        JSONObject evidence=new JSONObject().put("verifiedAt",System.currentTimeMillis()).put("sdk",android.os.Build.VERSION.SDK_INT).put("manifest",MPD).put("licenseHost","cwip-shaka-proxy.appspot.com").put("provenance","https://github.com/shaka-project/shaka-player/blob/main/demo/common/assets.js").put("drm","ClearKey").put("drmKeysLoaded",true).put("firstFrame",confirmed.get()).put("movingVideoMs",watched.get()).put("positionMs",position.get()).put("startupMs",startup.get()).put("videoProgressMs",video.get()).put("audioProgressMs",audio.get()).put("confirmationPolicyMs",12000).put("failure",failure.get());
        File dir=new File(context().getExternalFilesDir(null),"evidence");assertTrue(dir.isDirectory()||dir.mkdirs());try(FileOutputStream out=new FileOutputStream(new File(dir,"provider-clearkey-playback.json"))){out.write(evidence.toString(2).getBytes(StandardCharsets.UTF_8));}
    }
    @Test public void failedLicenseCannotClaimPlayback()throws Exception{
        Station station=new Station();station.name="Invalid license test";station.url=MPD;station.drmScheme="clearkey";station.drmLicenseUrl="https://storage.googleapis.com/shaka-demo-assets/does-not-exist-license";
        AtomicBoolean confirmed=new AtomicBoolean(),failed=new AtomicBoolean();
        try(ActivityScenario<MainActivity> scene=launch()){
            scene.onActivity(a->{PlayerView view=a.getWindow().getDecorView().findViewWithTag("swir_player");SwirPlayer p=new SwirPlayer(a,view,station,"Stabilność",new SwirPlayer.Listener(){public void state(String text){}public void confirmed(Station s,long ms){confirmed.set(true);}public void watchedThirtySeconds(Station s){}public void failed(Station s,String message,long ms){failed.set(true);}});set(a,"player",p);});
            long until=SystemClock.elapsedRealtime()+20000;while(!failed.get()&&SystemClock.elapsedRealtime()<until)SystemClock.sleep(100);assertTrue("Invalid license is surfaced",failed.get());assertFalse("Manifest availability is not DRM playback",confirmed.get());
        }
    }
    @Test public void resolvedProviderCannotSerializeSecrets()throws Exception{
        Station station=new Station();station.providerId=String.join("",Collections.nCopies(32,"a"));station.providerChannelId=String.join("",Collections.nCopies(64,"b"));station.providerProfileId="default";
        station.name="Private sample";station.url="https://example.com/live/private-user/private-password/2.ts";station.logo="https://example.com/?token=secret-logo";station.homepage="https://example.com/?secret-home";station.referrer="secret-referer";station.userAgent="secret-agent";station.drmLicenseUrl="https://example.com/?secret-license";station.requestHeaders.put("Authorization","Bearer secret-header");station.drmHeaders.put("Cookie","secret-cookie");
        String text=station.toJson().toString();assertFalse(text.contains("secret"));assertFalse(text.contains("private-password"));assertFalse(text.contains("private-user"));assertTrue(station.toJson().getString("url").startsWith("swir://provider/"));
        Station restored=Station.fromJson(station.toJson());assertTrue(restored.isProvider());assertEquals(station.key(),restored.key());assertEquals(station.sourceKey(),restored.sourceKey());assertTrue(restored.requestHeaders.isEmpty());assertTrue(restored.drmHeaders.isEmpty());assertTrue(restored.drmLicenseUrl.isEmpty());
    }
}
