package com.swir.stream;

import android.app.Application;

/** Initializes HTTP compatibility before catalogue, EPG and player connections. */
public final class SwirApplication extends Application {
    @Override public void onCreate(){super.onCreate();LegacyTls.install(this);}
}
