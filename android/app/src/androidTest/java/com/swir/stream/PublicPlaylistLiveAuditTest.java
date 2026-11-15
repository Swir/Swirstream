package com.swir.stream;

import androidx.test.ext.junit.runners.AndroidJUnit4;
import androidx.test.platform.app.InstrumentationRegistry;
import org.junit.Test;
import org.junit.runner.RunWith;
import org.json.JSONArray;
import org.json.JSONObject;
import java.io.File;
import java.util.Collections;
import java.util.List;
import java.util.concurrent.atomic.AtomicBoolean;
import static org.junit.Assert.*;

/** Explicit network audit: actual public responses through the real repository. */
@RunWith(AndroidJUnit4.class)
public final class PublicPlaylistLiveAuditTest {
    static int signed(List<Station> rows){int count=0;for(Station station:rows)if(station.url.matches("(?i).*[?&](token|key|signature|sig)=.*")&&!SourcePrivacy.publicCredentials(station.url))count++;return count;}
    @Test public void livePublicFeedsKeepPublicAndSignedChannels()throws Exception{
        org.junit.Assume.assumeTrue(Boolean.parseBoolean(InstrumentationRegistry.getArguments().getString("network","false")));
        android.content.Context context=InstrumentationRegistry.getInstrumentation().getTargetContext();JSONArray stats=new JSONArray();
        for(String url:new String[]{"https://iptv-org.github.io/iptv/index.m3u","https://raw.githubusercontent.com/Free-TV/IPTV/master/playlist.m3u8","https://iptv-org.github.io/iptv/countries/pl.m3u"}){
            PublicHttp.Document raw=PublicHttp.FETCHER.fetch(url,new AtomicBoolean());List<Station> before=CatalogRepository.deduplicate(M3u.parse(raw.text,url));
            File folder=new File(context.getCacheDir(),"public-live-"+java.util.UUID.randomUUID());CatalogRepository repo=new CatalogRepository(folder,(u,c)->raw);
            PublicHttp.Document clean=repo.feed("live-audit",new String[]{url},true,new AtomicBoolean());List<Station> after=CatalogRepository.deduplicate(CatalogRepository.playlist(clean,new CatalogRepository.DirectoryIndex(Collections.emptyList()),""));repo.remember(clean,after.size(),after.size());CatalogRepository.FoundPlaylist saved=repo.playlists().get(0);
            JSONObject row=new JSONObject();row.put("source",url);row.put("before",before.size());row.put("after",after.size());row.put("signedBefore",signed(before));row.put("signedAfter",signed(after));row.put("removedEntries",saved.removedEntries);row.put("sanitized",saved.sanitized);row.put("originalSha256",saved.originalSha256);row.put("cleanSha256",saved.sha256);stats.put(row);
            JSONObject report=new JSONObject();report.put("sources",stats);report.put("at",System.currentTimeMillis());report.put("sdk",android.os.Build.VERSION.SDK_INT);File evidence=new File(context.getExternalFilesDir(null),"evidence");evidence.mkdirs();CatalogRepository.writeJson(new File(evidence,"public-playlist-live-audit.json"),report);
            assertTrue("Unexpected public-channel loss",after.size()>=before.size()*.95);assertEquals("Public signed CDN channels must remain",signed(before),signed(after));assertFalse(SourcePrivacy.publicCredentials(saved.text));
        }
    }
}
