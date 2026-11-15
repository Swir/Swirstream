package com.swir.stream;

import org.junit.Test;
import org.json.JSONObject;
import java.util.Arrays;
import java.util.List;
import static org.junit.Assert.*;

public class M3uStationTest {
    private Station station(String name,String id,String feed){Station s=new Station();s.name=name;s.id=id;s.tvgId=id;s.feed=feed;s.country="PL";s.url="https://cdn.example/live.m3u8";return s;}
    @Test public void identityPreservesLanguageRegionsAndTimeshifts(){
        assertEquals(station("News HD","News.pl","HD").key(),station("News SD","News.pl","SD").key());
        assertNotEquals(station("News","News.pl","EnglishHD").key(),station("News","News.pl","GermanHD").key());
        assertNotEquals(station("News","News.pl","EuropeHD").key(),station("News","News.pl","AsiaSD").key());
        assertNotEquals(station("Demo (English)","","").key(),station("Demo (German)","","").key());
        assertNotEquals(station("Demo +1","","").key(),station("Demo 1","","").key());
        assertEquals(station("Demo (1080p) [Not 24/7]","","").key(),station("Demo HD","","").key());
    }
    @Test public void jsonDeepCopyKeepsMetadataAndNormalizesNulls()throws Exception{
        Station s=station("News","News.pl","HD");s.aliases=new String[]{"Wiadomości"};s.userAgent="Fixture";s.logo="https://logo.example/a.png";s.fetchedAt=1234;
        Station copy=Station.fromJson(s.toJson());copy.aliases[0]="Changed";assertEquals("Wiadomości",s.aliases[0]);assertEquals("Fixture",copy.userAgent);assertEquals(s.key(),copy.key());assertEquals(1234,copy.fetchedAt);
        assertEquals("",Station.fromJson(new JSONObject().put("userAgent",JSONObject.NULL)).userAgent);assertEquals(s.sourceKey(),copy.sourceKey());copy.url+="?alternate=1";assertNotEquals(s.sourceKey(),copy.sourceKey());
    }
    @Test public void importExportPreservesPlaybackHeadersCountryLogoAndFeed(){
        String body="#EXTM3U x-tvg-url=\"https://epg.example/pl.xml\"\n#EXTINF:-1 tvg-id='TVP1.pl@HD' tvg-logo='https://logo.example/tvp.png' group-title='News',TVP 1\n#EXTVLCOPT:http-user-agent=FixtureAgent\n#EXTVLCOPT:http-referrer=https://tvp.example/\n../live/master.m3u8\n";
        List<Station> imported=M3u.parse(body,"https://cdn.example/lists/list.m3u");assertEquals(1,imported.size());Station s=imported.get(0);assertEquals("https://cdn.example/live/master.m3u8",s.url);assertEquals("PL",s.country);assertEquals("HD",s.feed);assertEquals("FixtureAgent",s.userAgent);assertEquals("https://tvp.example/",s.referrer);
        Station again=M3u.parse(M3u.export(imported),"https://export.example/list.m3u").get(0);assertEquals(s.key(),again.key());assertEquals(s.logo,again.logo);assertEquals(s.group,again.group);assertEquals(s.userAgent,again.userAgent);assertEquals(Arrays.asList("https://epg.example/pl.xml"),M3u.epgUrls(body));
    }
    @Test public void publicMediaPortsAllowedWithoutCrawlingPrivateHosts(){
        assertTrue(PublicHttp.allowedStream("https://cdn.example:1935/live.m3u8"));assertFalse(PublicHttp.allowedDocument("https://cdn.example:1935/page"));
        for(String bad:new String[]{"http://127.0.0.1/live.m3u8","http://192.168.1.4:8080/live.m3u8","https://user:password@example.org/live.m3u8","http://device.local/live.m3u8","https://youtube.com/watch?v=test","https://example.org/watch.html"})assertFalse(bad,PublicHttp.allowedStream(bad));
        assertEquals(1,M3u.parse("#EXTM3U\n#EXTINF:-1,Public\nhttps://cdn.example:8000/live.m3u8\n#EXTINF:-1,Private\nhttp://10.0.0.1/live.m3u8\n","https://lists.example/list.m3u").size());
    }
    @Test public void numericAndLanguageQueriesDoNotLeakToOtherChannels(){
        assertTrue(M3u.matches("TVP 1",station("TVP1 (1080p)","TVP1.pl","HD")));assertFalse(M3u.matches("TVP 1",station("TVP10","TVP10.pl","")));
        Station dw=station("DW","DW.de","English");dw.aliases=new String[]{"Live TV — DW English"};assertTrue(M3u.matches("DW English",dw));assertFalse(M3u.matches("DW German",dw));
        assertTrue(M3u.matches("Łódź",station("Na żywo: Łódź wieczorem","Local.pl","")));assertFalse(M3u.matches("Łódź",station("Kanał łódzki","Other.pl","")));assertTrue(M3u.containsWord("program +1 teraz","+1"));assertFalse(M3u.containsWord("program 10 teraz","1"));
    }
    @Test public void hlsManifestImportsAsSingleSourceNotSegments(){
        String hls="#EXTM3U\n#EXT-X-MEDIA-SEQUENCE:45\n#EXTINF:4,\nsegment.ts\n";assertTrue(M3u.isHls(hls));List<Station> imported=M3u.parse(hls,"https://cdn.example/live.m3u8");assertEquals(1,imported.size());assertEquals("https://cdn.example/live.m3u8",imported.get(0).url);
    }
}
