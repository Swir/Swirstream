package com.swir.stream;

import android.content.Context;
import android.content.pm.ActivityInfo;
import android.graphics.Bitmap;
import android.graphics.Rect;
import android.os.Build;
import android.os.Bundle;
import android.os.SystemClock;
import android.view.KeyEvent;
import android.view.View;
import android.view.ViewGroup;
import android.view.inputmethod.InputMethodManager;
import android.widget.EditText;
import android.widget.ListView;
import android.widget.TextView;
import androidx.media3.common.util.UnstableApi;
import androidx.test.core.app.ActivityScenario;
import androidx.test.ext.junit.runners.AndroidJUnit4;
import androidx.test.platform.app.InstrumentationRegistry;
import org.json.JSONObject;
import org.junit.Assume;
import org.junit.Test;
import org.junit.runner.RunWith;
import java.io.File;
import java.io.FileOutputStream;
import java.lang.reflect.Field;
import java.util.Arrays;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Set;
import java.util.concurrent.atomic.AtomicBoolean;
import java.util.concurrent.atomic.AtomicInteger;
import java.util.concurrent.atomic.AtomicReference;
import static org.junit.Assert.*;

/** Opt in with -e network true. Uses the real catalogue, real UI and real video surface. */
@UnstableApi
@RunWith(AndroidJUnit4.class)
public final class LiveUiSmokeTest {
    @Test public void discoversAndPlaysDwEnglishInPhoneAndProjectorLayouts()throws Exception{
        Assume.assumeTrue("Explicit live-network opt-in",Boolean.parseBoolean(InstrumentationRegistry.getArguments().getString("network","false")));
        Context context=InstrumentationRegistry.getInstrumentation().getTargetContext();Set<String> countries=new LinkedHashSet<>(Arrays.asList("PL","DE"));
        context.getSharedPreferences("interface",Context.MODE_PRIVATE).edit().putStringSet("countries",countries).putString("quality","Stabilność").commit();
        CatalogRepository repository=new CatalogRepository(context);long scanStarted=SystemClock.elapsedRealtime();List<Station> stations=repository.scanAll(countries,LiveUiSmokeTest::status,new AtomicBoolean());
        JSONObject evidence=new JSONObject().put("sdk",Build.VERSION.SDK_INT).put("catalogAddresses",stations.size()).put("catalogMs",SystemClock.elapsedRealtime()-scanStarted).put("dataAt",repository.dataAt());
        assertFalse("Actual public catalogue cannot be empty",stations.isEmpty());assertTrue("Mixed PL/DE scope",repository.matchesScope(countries));
        try(ActivityScenario<MainActivity> scene=ActivityScenario.launch(MainActivity.class)){
            AtomicBoolean ready=new AtomicBoolean();long deadline=SystemClock.elapsedRealtime()+15000;
            while(!ready.get()&&SystemClock.elapsedRealtime()<deadline){scene.onActivity(a->ready.set(a.getWindow().getDecorView().findViewWithTag("channel_list")!=null));SystemClock.sleep(100);}assertTrue("Cached gate opens",ready.get());
            // Search actual downloaded records using the visible field and the
            // keyboard action. This must stay local and retain the requested row.
            scene.onActivity(a->a.getWindow().getDecorView().findViewWithTag("tab_all").performClick());
            org.json.JSONArray localCases=new org.json.JSONArray();int checked=0;
            for(Station downloaded:stations){if(checked>=5)break;String requested=downloaded.name.toUpperCase(java.util.Locale.ROOT);long localStarted=SystemClock.elapsedRealtime();
                scene.onActivity(a->{EditText input=a.getWindow().getDecorView().findViewWithTag("channel_search");input.setText(requested);input.onEditorAction(android.view.inputmethod.EditorInfo.IME_ACTION_SEARCH);assertFalse("Keyboard lookup must not start web search",searching(a));ListView rows=a.getWindow().getDecorView().findViewWithTag("channel_list");boolean retained=false;for(int i=0;i<rows.getCount();i++){MainActivity.Channel channel=(MainActivity.Channel)rows.getAdapter().getItem(i);for(Station source:channel.sources)if(source.url.equals(downloaded.url))retained=true;}assertTrue("Downloaded channel must remain searchable: "+downloaded.name,retained);});
                localCases.put(new JSONObject().put("name",downloaded.name).put("query",requested).put("elapsedMs",SystemClock.elapsedRealtime()-localStarted));checked++;
            }
            assertEquals(5,checked);evidence.put("downloadedLocalQueries",localCases);scene.onActivity(a->a.getWindow().getDecorView().findViewWithTag("tab_0").performClick());
            scene.onActivity(a->{a.setRequestedOrientation(ActivityInfo.SCREEN_ORIENTATION_PORTRAIT);View root=a.getWindow().getDecorView();EditText field=root.findViewWithTag("channel_search");field.setText("DW English");InputMethodManager keyboard=(InputMethodManager)a.getSystemService(Context.INPUT_METHOD_SERVICE);keyboard.hideSoftInputFromWindow(field.getWindowToken(),0);});SystemClock.sleep(1200);
            // Public playlists change: the current DE list has DW Russian only.
            // Use the real search button to discover the English feed from broadcaster pages.
            AtomicInteger matches=new AtomicInteger();scene.onActivity(a->{ListView list=a.getWindow().getDecorView().findViewWithTag("channel_list");evidencePut(evidence,"catalogOnlyRows",list.getCount());View web=findText(a.getWindow().getDecorView(),"Deep Search");assertNotNull("Real web-search control",web);assertTrue(web.performClick());});
            long webStarted=SystemClock.elapsedRealtime();deadline=webStarted+240000;
            AtomicBoolean searching=new AtomicBoolean(true);
            while((searching.get()||matches.get()==0)&&SystemClock.elapsedRealtime()<deadline){scene.onActivity(a->{ListView list=a.getWindow().getDecorView().findViewWithTag("channel_list");matches.set(list.getCount());searching.set(searching(a));});SystemClock.sleep(200);}
            assertFalse("Actual web search finished before playback",searching.get());
            evidence.put("uiWebDiscoveryMs",SystemClock.elapsedRealtime()-webStarted);status(evidence.toString());
            scene.onActivity(a->{ListView list=a.getWindow().getDecorView().findViewWithTag("channel_list");if(list.getCount()>0)list.performItemClick(list.getChildAt(0),0,list.getAdapter().getItemId(0));});
            evidence.put("dwChannelRows",matches.get());assertTrue("Real DW English channel row",matches.get()>0);
            AtomicBoolean confirmed=new AtomicBoolean();AtomicReference<String> source=new AtomicReference<>("");long playStarted=SystemClock.elapsedRealtime();deadline=playStarted+65000;
            while(!confirmed.get()&&SystemClock.elapsedRealtime()<deadline){scene.onActivity(a->{SwirPlayer player=player(a);confirmed.set(player!=null&&player.isConfirmed());Station station=active(a);if(station!=null)source.set(station.url);});SystemClock.sleep(200);}
            evidence.put("actualFirstFrame",confirmed.get()).put("playbackMs",SystemClock.elapsedRealtime()-playStarted).put("selectedSource",source.get());status(evidence.toString());assertTrue("Actual rendered DW video: "+evidence,confirmed.get());SystemClock.sleep(5200);
            evidence.put("portraitScreenshot",screenshot(context,"swirstream-api"+Build.VERSION.SDK_INT+"-portrait.png"));
            scene.onActivity(a->{View root=a.getWindow().getDecorView();ListView list=root.findViewWithTag("channel_list");View row=list.getChildAt(0);assertNotNull("Portrait channel card is laid out",row);Rect shown=new Rect();assertTrue(row.getGlobalVisibleRect(shown));assertTrue("A whole channel card must be visible below the playing video: "+shown.height()+" / "+row.getHeight(),shown.height()>=row.getHeight()-2);View full=root.findViewWithTag("fullscreen");Rect control=new Rect();assertTrue(full.getGlobalVisibleRect(control));assertTrue("Fullscreen button must be visible without horizontal scrolling",control.width()>=full.getWidth()-2);evidencePut(evidence,"portraitVisibleChannelRowPixels",shown.height());});
            scene.onActivity(a->a.setRequestedOrientation(ActivityInfo.SCREEN_ORIENTATION_LANDSCAPE));SystemClock.sleep(1800);
            AtomicBoolean layout=new AtomicBoolean();scene.onActivity(a->{View root=a.getWindow().getDecorView(),list=root.findViewWithTag("channel_list"),video=root.findViewWithTag("swir_player");int[] l=new int[2],v=new int[2];list.getLocationOnScreen(l);video.getLocationOnScreen(v);layout.set(l[0]+list.getWidth()<=v[0]&&video.getWidth()>0);});assertTrue("Projector layout has list and video side by side",layout.get());scene.onActivity(a->{ListView list=a.getWindow().getDecorView().findViewWithTag("channel_list");View row=list.getChildAt(0);assertNotNull("Landscape channel card laid out",row);Rect shown=new Rect();assertTrue(row.getGlobalVisibleRect(shown));assertTrue("A whole landscape card remains visible: "+shown.height()+" / "+row.getHeight(),shown.height()>=row.getHeight()-2);evidencePut(evidence,"landscapeVisibleChannelRowPixels",shown.height());});
            InstrumentationRegistry.getInstrumentation().sendKeyDownUpSync(KeyEvent.KEYCODE_DPAD_DOWN);scene.onActivity(a->a.getWindow().getDecorView().findViewWithTag("countries").requestFocus());InstrumentationRegistry.getInstrumentation().sendKeyDownUpSync(KeyEvent.KEYCODE_DPAD_RIGHT);
            AtomicReference<String> focus=new AtomicReference<>("");scene.onActivity(a->focus.set(String.valueOf(a.getCurrentFocus()==null?null:a.getCurrentFocus().getTag())));evidence.put("remoteFocus",focus.get());assertEquals("refresh",focus.get());
            evidence.put("landscapeScreenshot",screenshot(context,"swirstream-api"+Build.VERSION.SDK_INT+"-landscape.png")).put("landscapeTwoPanes",layout.get());
            status("SWIR_LIVE_UI_EVIDENCE "+evidence);File report=new File(context.getExternalFilesDir("evidence"),"live-ui-api"+Build.VERSION.SDK_INT+".json");try(FileOutputStream out=new FileOutputStream(report)){out.write(evidence.toString(2).getBytes(java.nio.charset.StandardCharsets.UTF_8));}
        }
    }
    private static View findText(View view,String text){if(view instanceof TextView&&text.contentEquals(((TextView)view).getText()))return view;if(view instanceof ViewGroup){ViewGroup group=(ViewGroup)view;for(int i=0;i<group.getChildCount();i++){View found=findText(group.getChildAt(i),text);if(found!=null)return found;}}return null;}
    private static void evidencePut(JSONObject evidence,String key,int value){try{evidence.put(key,value);}catch(Exception e){throw new AssertionError(e);}}
    private static boolean searching(MainActivity activity){try{Field field=MainActivity.class.getDeclaredField("busy");field.setAccessible(true);return field.getBoolean(activity);}catch(Exception e){throw new AssertionError(e);}}
    private static SwirPlayer player(MainActivity activity){try{Field f=MainActivity.class.getDeclaredField("player");f.setAccessible(true);return(SwirPlayer)f.get(activity);}catch(Exception e){throw new AssertionError(e);}}
    private static Station active(MainActivity activity){try{Field f=MainActivity.class.getDeclaredField("activeSource");f.setAccessible(true);return(Station)f.get(activity);}catch(Exception e){throw new AssertionError(e);}}
    private static String screenshot(Context context,String name)throws Exception{Bitmap bitmap=InstrumentationRegistry.getInstrumentation().getUiAutomation().takeScreenshot();assertNotNull("Real screen capture",bitmap);File folder=context.getExternalFilesDir("evidence");assertNotNull(folder);folder.mkdirs();File file=new File(folder,name);try(FileOutputStream out=new FileOutputStream(file)){assertTrue(bitmap.compress(Bitmap.CompressFormat.PNG,100,out));}bitmap.recycle();return file.getAbsolutePath();}
    private static void status(String text){Bundle message=new Bundle();message.putString("stream",text+"\n");InstrumentationRegistry.getInstrumentation().sendStatus(0,message);}
}
