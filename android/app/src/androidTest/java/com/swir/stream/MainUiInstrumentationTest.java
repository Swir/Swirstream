package com.swir.stream;

import android.content.pm.ActivityInfo;
import android.content.res.Configuration;
import android.os.SystemClock;
import android.view.KeyEvent;
import android.view.View;
import android.widget.EditText;
import android.widget.ListView;
import androidx.test.core.app.ActivityScenario;
import androidx.test.ext.junit.runners.AndroidJUnit4;
import androidx.test.platform.app.InstrumentationRegistry;
import org.junit.Test;
import org.junit.After;
import org.junit.runner.RunWith;
import java.util.concurrent.atomic.AtomicBoolean;
import static org.junit.Assert.*;

@RunWith(AndroidJUnit4.class)
public final class MainUiInstrumentationTest {
    @After public void restoreCatalogue()throws Exception{UiTestData.restore(InstrumentationRegistry.getInstrumentation().getTargetContext());}
    private ActivityScenario<MainActivity> launch()throws Exception{
        UiTestData.seed(InstrumentationRegistry.getInstrumentation().getTargetContext());ActivityScenario<MainActivity> scene=ActivityScenario.launch(MainActivity.class);AtomicBoolean ready=new AtomicBoolean();long end=SystemClock.elapsedRealtime()+10000;
        while(!ready.get()&&SystemClock.elapsedRealtime()<end){scene.onActivity(a->ready.set(a.getWindow().getDecorView().findViewWithTag("channel_list")!=null));SystemClock.sleep(100);}assertTrue(ready.get());return scene;
    }
    @Test public void stableSearchAndDpadNavigation()throws Exception{
        try(ActivityScenario<MainActivity> scene=launch()){
            scene.onActivity(a->{View root=a.getWindow().getDecorView();ListView list=root.findViewWithTag("channel_list");assertEquals(3,list.getCount());((EditText)root.findViewWithTag("channel_search")).setText("sport");});SystemClock.sleep(400);
            InstrumentationRegistry.getInstrumentation().sendKeyDownUpSync(KeyEvent.KEYCODE_DPAD_DOWN);
            scene.onActivity(a->{View root=a.getWindow().getDecorView();ListView list=root.findViewWithTag("channel_list");assertEquals(1,list.getCount());assertTrue("Remote focus can start on Countries",root.findViewWithTag("countries").requestFocus());});
            InstrumentationRegistry.getInstrumentation().sendKeyDownUpSync(KeyEvent.KEYCODE_DPAD_RIGHT);
            scene.onActivity(a->{assertNotNull(a.getCurrentFocus());assertEquals("refresh",a.getCurrentFocus().getTag());});SystemClock.sleep(500);
            scene.onActivity(a->{ListView list=a.getWindow().getDecorView().findViewWithTag("channel_list");assertEquals("Filtered channel remains visible after background callbacks",1,list.getCount());});
        }
    }
    @Test public void landscapeUsesTwoPanesAndFullscreenIsReversible()throws Exception{
        try(ActivityScenario<MainActivity> scene=launch()){
            scene.onActivity(a->a.setRequestedOrientation(ActivityInfo.SCREEN_ORIENTATION_LANDSCAPE));SystemClock.sleep(1400);
            scene.onActivity(a->{assertEquals(Configuration.ORIENTATION_LANDSCAPE,a.getResources().getConfiguration().orientation);View root=a.getWindow().getDecorView(),list=root.findViewWithTag("channel_list"),video=root.findViewWithTag("swir_player");int[] l=new int[2],v=new int[2];list.getLocationOnScreen(l);video.getLocationOnScreen(v);assertTrue("List and video side by side",l[0]+list.getWidth()<=v[0]);root.findViewWithTag("fullscreen").performClick();});SystemClock.sleep(300);
            scene.onActivity(a->{View root=a.getWindow().getDecorView(),video=root.findViewWithTag("swir_player");assertTrue(video.getWidth()>root.getWidth()*.8);});InstrumentationRegistry.getInstrumentation().sendKeyDownUpSync(KeyEvent.KEYCODE_BACK);SystemClock.sleep(300);
            scene.onActivity(a->{View list=a.getWindow().getDecorView().findViewWithTag("channel_list");assertTrue(list.isShown());a.setRequestedOrientation(ActivityInfo.SCREEN_ORIENTATION_UNSPECIFIED);});
        }
    }
    @Test public void channelAliasesAndNumericIdentityAreRespected()throws Exception{
        try(ActivityScenario<MainActivity> scene=launch()){
            scene.onActivity(a->((EditText)a.getWindow().getDecorView().findViewWithTag("channel_search")).setText("TVP1"));SystemClock.sleep(400);
            scene.onActivity(a->{ListView list=a.getWindow().getDecorView().findViewWithTag("channel_list");assertEquals("TVP1 must not match TVP10",1,list.getCount());((EditText)a.getWindow().getDecorView().findViewWithTag("channel_search")).setText("AliasSport");});SystemClock.sleep(400);
            scene.onActivity(a->{ListView list=a.getWindow().getDecorView().findViewWithTag("channel_list");assertEquals("Alternate channel name can be searched",1,list.getCount());});
        }
    }
}
