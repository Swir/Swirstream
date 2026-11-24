package com.swir.stream;

import android.app.AlertDialog;
import android.content.Context;
import android.content.SharedPreferences;
import android.widget.*;
import androidx.test.core.app.ActivityScenario;
import androidx.test.ext.junit.runners.AndroidJUnit4;
import androidx.test.platform.app.InstrumentationRegistry;
import java.util.*;
import java.util.concurrent.atomic.AtomicReference;
import org.junit.Test;
import org.junit.runner.RunWith;
import static org.junit.Assert.*;

@RunWith(AndroidJUnit4.class)
public class InternationalUiTest {
    @Test public void regionalDefaultsAndFallbackLanguageAreIndependent() {
        assertEquals(Collections.singleton("JP"),UiLanguage.initialCountries(Locale.JAPAN));
        assertTrue(UiLanguage.initialCountries(new Locale("pl")).isEmpty());
        assertEquals("Full screen",new UiLanguage("system",Locale.JAPAN).t("Pełny ekran"));
        assertEquals("Pełny ekran",new UiLanguage("pl",Locale.JAPAN).t("Pełny ekran"));
    }
    @Test public void mainScreenAndSearchablePickerKeepExistingSelection() throws Exception {
        Context ctx=InstrumentationRegistry.getInstrumentation().getTargetContext();
        SharedPreferences prefs=ctx.getSharedPreferences("interface",Context.MODE_PRIVATE);
        String old=prefs.getString("ui_language",null);
        prefs.edit().putString("ui_language","en").commit();
        UiTestData.seed(ctx);
        try(ActivityScenario<MainActivity> scenario=ActivityScenario.launch(MainActivity.class)) {
            for(int i=0;i<60;i++) {
                boolean[] ready={false};
                scenario.onActivity(a->ready[0]=a.getWindow().getDecorView().findViewWithTag("fullscreen")!=null);
                if(ready[0])break;
                android.os.SystemClock.sleep(100);
            }
            AtomicReference<Set<String>> result=new AtomicReference<>();
            scenario.onActivity(a->{
                Button full=a.getWindow().getDecorView().findViewWithTag("fullscreen");
                assertNotNull(full);assertEquals("Full screen",full.getText().toString());
                EditText query=a.getWindow().getDecorView().findViewWithTag("channel_search");
                assertEquals("Search downloaded channels",query.getHint().toString());
                AlertDialog dialog=CountryPicker.show(a,new UiLanguage("en",Locale.US),"Search countries",
                    Arrays.asList("GB","PL","JP"),Arrays.asList("United Kingdom (GB)","Poland (PL)","Japan (JP)"),
                    Collections.singleton("PL"),result::set);
                EditText filter=dialog.getWindow().getDecorView().findViewWithTag("country_filter");
                ListView options=dialog.getWindow().getDecorView().findViewWithTag("country_options");
                filter.setText("Brytania");assertEquals(1,options.getCount());
                options.performItemClick(null,0,options.getItemIdAtPosition(0));
                filter.setText("日本");assertEquals(1,options.getCount());
                dialog.getButton(AlertDialog.BUTTON_POSITIVE).performClick();
            });
            InstrumentationRegistry.getInstrumentation().waitForIdleSync();
            assertEquals(new HashSet<>(Arrays.asList("PL","GB")),result.get());
        } finally {
            prefs.edit().putString("ui_language",old).commit();UiTestData.restore(ctx);
        }
    }
}
