package com.swir.stream;

import android.app.Activity;
import android.os.Bundle;
import androidx.media3.common.util.UnstableApi;
import androidx.media3.ui.PlayerView;

/** Exists only in the instrumentation APK. */
@UnstableApi
public final class PlayerTestActivity extends Activity {
    PlayerView video;
    SwirPlayer player;
    @Override public void onCreate(Bundle state){super.onCreate(state);video=new PlayerView(this);setContentView(video);}
    @Override protected void onPause(){if(player!=null)player.pause();super.onPause();}
    @Override protected void onDestroy(){if(player!=null)player.close();super.onDestroy();}
}
