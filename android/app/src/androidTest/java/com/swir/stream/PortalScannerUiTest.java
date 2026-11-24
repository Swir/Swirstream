package com.swir.stream;
import androidx.test.ext.junit.runners.AndroidJUnit4;
import org.junit.Test;
import org.junit.runner.RunWith;
import static org.junit.Assert.*;
@RunWith(AndroidJUnit4.class)
public class PortalScannerUiTest {
    @Test public void releaseScannerDoesNotConnectToImportedEntries() throws Exception {
        okhttp3.OkHttpClient client=new okhttp3.OkHttpClient.Builder().addInterceptor(chain->{throw new AssertionError("Unexpected connection");}).build();
        PortalTxt.Import input=PortalTxt.read(new java.io.ByteArrayInputStream("https://fixture.example/c/".getBytes("UTF-8")));
        assertEquals(PortalScanner.Status.NOT_CHECKED,PortalScanner.network(client).check(input.entries.get(0),new java.util.concurrent.atomic.AtomicBoolean(),android.os.SystemClock.elapsedRealtime()+1000).status);
    }
}
