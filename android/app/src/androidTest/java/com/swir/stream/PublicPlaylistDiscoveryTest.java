package com.swir.stream;

import androidx.test.ext.junit.runners.AndroidJUnit4;
import androidx.test.platform.app.InstrumentationRegistry;
import org.junit.Test;
import org.junit.runner.RunWith;
import org.json.JSONObject;
import java.io.File;
import java.io.IOException;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.Collections;
import java.util.List;
import java.util.concurrent.atomic.AtomicBoolean;
import static org.junit.Assert.*;

@RunWith(AndroidJUnit4.class)
public final class PublicPlaylistDiscoveryTest {
    static final String URL="https://broadcaster.example/public/channels.m3u";
    static final String BODY="#EXTM3U x-tvg-url=\"https://guide.example/epg.xml\"\n#EXTINF:-1 tvg-id=\"News.pl\" tvg-country=\"PL\",News\nhttps://cdn.example/live/news/hls/master.m3u8\n";
    static PublicHttp.Document doc(String url,String text){return new PublicHttp.Document(url,text,"application/x-mpegurl",System.currentTimeMillis(),0);}
    static File folder(){File file=new File(InstrumentationRegistry.getInstrumentation().getTargetContext().getCacheDir(),"public-playlist-"+java.util.UUID.randomUUID());assertTrue(file.mkdirs());return file;}
    static boolean rejected(String url,String body){try{return CatalogRepository.playlist(doc(url,body),new CatalogRepository.DirectoryIndex(Collections.emptyList()),"").isEmpty();}catch(IOException expected){return true;}}
    @Test public void arbitraryPublicDomainsRemainBoundedAndGithubIsRewritten(){
        assertEquals(URL,WebCrawler.playlistUrl(URL));assertEquals(URL,WebCrawler.playlistUrl(URL+"#same"));
        assertEquals("https://raw.githubusercontent.com/public/tv/main/list.m3u",WebCrawler.playlistUrl("https://github.com/public/tv/blob/main/list.m3u"));
        for(String url:Arrays.asList("http://127.0.0.1/list.m3u","http://10.0.0.1/list.m3u","http://localhost/list.m3u","https://public.example:8443/list.m3u","https://public.example/get.php","https://public.example/a.html"))assertNull(WebCrawler.playlistUrl(url));
    }
    @Test public void credentialUrlsAndEncodedNamesAreNotCandidates(){
        for(String url:Arrays.asList(URL+"?password=fixture-secret",URL+"?%2570assword=fixture-secret","https://name:password@public.example/list.m3u"))assertNull(WebCrawler.playlistUrl(url));
    }
    @Test public void ordinaryPublicPathsAreNotMistakenForAccountPaths(){
        assertFalse(SourcePrivacy.publicCredentials(BODY));assertFalse(SourcePrivacy.publicCredentials("https://cdn.example/live/channel/hls/master.m3u8?country=PL&format=hls"));assertFalse(SourcePrivacy.publicCredentials("#EXTINF:-1,The Password Show\nhttps://cdn.example/token/channel.m3u8"));assertNotNull(WebCrawler.playlistUrl(URL+"?country=PL&format=m3u"));
    }
    @Test public void accountPathsAndEpgCredentialsAreRejected()throws Exception{
        assertTrue(rejected(URL,BODY.replace("/live/news/hls/master.m3u8","/live/account/password/12345.ts")));
        SourcePrivacy.CleanPlaylist clean=SourcePrivacy.sanitizePublicPlaylist(URL,BODY.replace("epg.xml","epg.xml?username=test&password=fixture-secret"));assertTrue(clean.sanitized);assertFalse(clean.text.contains("fixture-secret"));assertEquals(1,CatalogRepository.playlist(doc(URL,clean.text),new CatalogRepository.DirectoryIndex(Collections.emptyList()),"").size());
    }
    @Test public void authorizationCookiesAndLicenseKeysAreRejected(){
        for(String header:Arrays.asList("#EXTVLCOPT:http-cookie=fixture-secret\n","#EXTHTTP:{\"Authorization\":\"Bearer fixture-secret\"}\n","#KODIPROP:inputstream.adaptive.stream_headers=Cookie%3Dfixture-secret\n","#KODIPROP:inputstream.adaptive.license_key=fixture-secret\n"))assertTrue(header,rejected(URL,BODY.replace("#EXTINF:",header+"#EXTINF:")));
    }
    @Test public void hlsAndMalformedDataAreNotChannelPlaylists(){
        assertTrue(rejected(URL,"#EXTM3U\n#EXT-X-TARGETDURATION:6\n#EXTINF:6,\nsegment.ts\n"));assertTrue(rejected(URL,"<html>No playlist</html>"));
    }
    @Test public void unsafeDocumentsNeverEnterFoundPlaylistCache(){
        File folder=folder();CatalogRepository repo=new CatalogRepository(folder,(u,c)->doc(u,BODY));repo.remember(doc(URL,BODY),1,1);assertEquals(1,repo.playlists().size());
        repo.remember(doc("https://unsafe.example/list.m3u",BODY+"https://cdn.example/a?password=fixture-secret"),1,1);assertEquals(2,repo.playlists().size());assertTrue(repo.playlists().stream().noneMatch(p->p.text.contains("fixture-secret")));assertTrue(repo.playlists().stream().anyMatch(p->p.sanitized&&p.removedEntries==1));
    }
    @Test public void legacyCredentialCacheCannotBeReopenedOrExported()throws Exception{
        File folder=folder();CatalogRepository repo=new CatalogRepository(folder,(u,c)->doc(u,BODY));repo.remember(doc(URL,BODY),1,1);File file=new File(folder,"found-playlists/"+Station.sha256(URL)+".json");JSONObject json=CatalogRepository.readJson(file,1_000_000);String unsafe=BODY+"https://cdn.example/a?password=fixture-secret";json.put("text",unsafe);json.put("sha256",Station.sha256(unsafe));CatalogRepository.writeJson(file,json);
        assertTrue(repo.playlists().isEmpty());assertFalse(file.exists());
    }
    @Test public void feedSanitizesCredentialEntryBeforeOrdinaryCacheWrite()throws Exception{
        File folder=folder();CatalogRepository repo=new CatalogRepository(folder,(u,c)->doc(u,BODY+"https://cdn.example/a?password=fixture-secret"));PublicHttp.Document safe=repo.feed("public-playlist",new String[]{URL},true,new AtomicBoolean());assertFalse(safe.text.contains("fixture-secret"));assertEquals(1,new File(folder,"feeds").listFiles().length);repo.remember(safe,1,1);CatalogRepository.FoundPlaylist found=repo.playlists().get(0);assertTrue(found.sanitized);assertEquals(1,found.removedEntries);assertNotEquals(found.sha256,found.originalSha256);
        CatalogRepository reopened=new CatalogRepository(folder,(u,c)->{throw new IOException("offline");});PublicHttp.Document cached=reopened.feed("public-playlist",new String[]{URL},false,new AtomicBoolean());reopened.remember(cached,1,1);assertEquals(found.originalSha256,reopened.playlists().get(0).originalSha256);
    }
    @Test public void searchUsesUnrestrictedQueryDeduplicatesAndSkipsAccessUrls()throws Exception{
        List<String> visited=new ArrayList<>(),notes=new ArrayList<>();int[] accepted={0};
        String rss="<rss><channel><item><title>Public TV</title><link>"+URL+"</link></item><item><title>Same</title><link>"+URL+"#copy</link></item><item><title>Account</title><link>https://public.example/hidden.m3u?password=fixture-secret</link></item></channel></rss>";
        PublicHttp.Fetcher fetch=(u,c)->{visited.add(u);return doc(u,u.equals(URL)?BODY:u.contains("bing.com")?rss:"<div class='no-results'>No results</div>");};
        WebCrawler crawler=new WebCrawler(fetch,notes::add,null,null,(d,all,matches)->accepted[0]++,()->false);
        List<Station> found=crawler.search("News",Collections.singleton("PL"),Collections.emptyList(),null,new AtomicBoolean());
        assertEquals(1,accepted[0]);assertEquals(1,found.size());assertEquals(1,Collections.frequency(visited,URL));assertFalse(visited.stream().anyMatch(u->u.contains("fixture-secret")));
        List<String> queries=new ArrayList<>();for(String u:visited)if(u.contains("bing.com"))queries.add(java.net.URLDecoder.decode(u,"UTF-8"));assertTrue(queries.size()<=DeepSearchPlan.MAX_QUERIES);assertTrue(queries.stream().anyMatch(q->q.contains("public TV playlist m3u")&&!q.contains("site:github.com")));
    }
    @Test public void staleSearchResultDoesNotReachCache()throws Exception{
        int[] accepted={0};PublicHttp.Fetcher fetch=(u,c)->u.equals(URL)?new PublicHttp.Document(u,BODY,"application/x-mpegurl",System.currentTimeMillis(),System.currentTimeMillis()-8*86400000L):doc(u,u.contains("bing.com")?"<rss><channel><item><title>TV</title><link>"+URL+"</link></item></channel></rss>":"<div class='no-results'>No results</div>");
        WebCrawler crawler=new WebCrawler(fetch,n->{},null,null,(d,all,m)->accepted[0]++,()->false);assertTrue(crawler.search("News",Collections.emptySet(),Collections.emptyList(),null,new AtomicBoolean()).isEmpty());assertEquals(0,accepted[0]);
    }
    @Test public void oneAccountEntryKeepsPublicAndSignedCdnChannels()throws Exception{
        String mixed=BODY+"#EXTINF:-1,Private account\nhttps://cdn.example/live/user/password/123.ts\n#EXTINF:-1,Public signed CDN\nhttps://cdn.example/signed.m3u8?token=public-token&key=public-key&signature=public-signature\n";
        SourcePrivacy.CleanPlaylist clean=SourcePrivacy.sanitizePublicPlaylist(URL,mixed);assertTrue(clean.sanitized);assertEquals(1,clean.removedEntries);assertEquals(2,CatalogRepository.playlist(doc(URL,clean.text),new CatalogRepository.DirectoryIndex(Collections.emptyList()),"").size());assertTrue(clean.text.contains("token=public-token"));assertFalse(clean.text.contains("/user/password/"));assertNotEquals(Station.sha256(clean.text),clean.originalSha256);
    }
    @Test public void cancellationDoesNotFetchOrPublish()throws Exception{
        int[] calls={0};WebCrawler crawler=new WebCrawler((u,c)->{calls[0]++;return doc(u,BODY);},n->{},null,null,(d,all,m)->calls[0]++,()->false);boolean cancelled=false;try{crawler.search("News",Collections.emptySet(),Collections.emptyList(),null,new AtomicBoolean(true));}catch(IOException expected){cancelled=true;}assertTrue(cancelled);assertEquals(0,calls[0]);
    }
}
