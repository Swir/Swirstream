package com.swir.stream;

import androidx.test.platform.app.InstrumentationRegistry;
import org.json.JSONObject;
import org.junit.*;
import java.io.*;
import java.net.*;
import java.security.*;
import java.util.Locale;
import static org.junit.Assert.*;

public class GithubReleaseNetworkTest {
    @Test public void publicReleaseMetadataPointsToSignedArtifactWithMatchingDigest()throws Exception {
        Assume.assumeTrue("true".equals(InstrumentationRegistry.getArguments().getString("network")));
        HttpURLConnection metadata=(HttpURLConnection)new URL(ReleaseUpdates.API).openConnection();
        metadata.setConnectTimeout(10000);metadata.setReadTimeout(30000);metadata.setRequestProperty("User-Agent","SwirStream-Updater/1");
        ReleaseUpdates.Asset asset;
        try(InputStream in=metadata.getInputStream();ByteArrayOutputStream buffer=new ByteArrayOutputStream()){
            byte[] block=new byte[8192];int n;
            while((n=in.read(block))!=-1){assertTrue(buffer.size()+n<=2097152);buffer.write(block,0,n);}
            JSONObject release=new JSONObject(buffer.toString("UTF-8"));asset=ReleaseUpdates.select(release,"0.0.0");
            assertNotNull(asset);
        }finally{metadata.disconnect();}
        HttpURLConnection download=(HttpURLConnection)new URL(asset.url).openConnection();
        download.setConnectTimeout(10000);download.setReadTimeout(30000);
        MessageDigest hash=MessageDigest.getInstance("SHA-256");long count=0;
        try(InputStream in=download.getInputStream()){byte[] block=new byte[65536];int n;while((n=in.read(block))!=-1){count+=n;assertTrue(count<=asset.size);hash.update(block,0,n);}}finally{download.disconnect();}
        assertEquals(asset.size,count);StringBuilder hex=new StringBuilder();for(byte b:hash.digest())hex.append(String.format(Locale.ROOT,"%02x",b&255));assertEquals(asset.digest,hex.toString());
    }
}
