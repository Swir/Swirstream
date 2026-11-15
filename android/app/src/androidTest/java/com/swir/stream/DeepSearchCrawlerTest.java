package com.swir.stream;

import androidx.test.ext.junit.runners.AndroidJUnit4;
import org.junit.Test;
import org.junit.runner.RunWith;
import java.io.IOException;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.Collections;
import java.util.HashSet;
import java.util.List;
import java.util.concurrent.atomic.AtomicBoolean;
import static org.junit.Assert.*;

@RunWith(AndroidJUnit4.class)
public class DeepSearchCrawlerTest {
    static final String LIVE="#EXTM3U\n#EXT-X-MEDIA-SEQUENCE:19\n#EXTINF:4,\nsegment.ts\n";
    static Station channel(String name,String url,String country){Station s=new Station();s.name=name;s.url=s.homepage=url;s.country=country;s.id=name.replace(" ","")+"."+country.toLowerCase();return s;}
    static PublicHttp.Document doc(String url,String text){return new PublicHttp.Document(url,text,"text/plain",System.currentTimeMillis(),0);}
    static WebCrawler crawler(PublicHttp.Fetcher fetch,List<String> notes){return new WebCrawler(fetch,notes::add,null,null,(d,all,matching)->{},()->false);}
    static String rss(String entries){return "<rss><channel>"+entries+"</channel></rss>";}
    static String item(String title,String url){return "<item><title>"+title+"</title><link>"+url+"</link></item>";}
    static boolean engine(String url){return url.contains("bing.com")||url.contains("duckduckgo.com");}

    @Test public void planUsesCanonicalAliasesCountryLanguageAndLegacyId(){
        Station ard=channel("Das Erste","https://daserste.example/","DE");ard.aliases=new String[]{"ARD","Erstes Deutsches Fernsehen"};List<String> plan=DeepSearchPlan.build("ARD",Collections.singleton("DE"),Arrays.asList(ard),Arrays.asList(ard));
        assertTrue(plan.size()<=6);assertEquals(plan.size(),new HashSet<>(plan).size());assertTrue(plan.stream().anyMatch(q->q.contains("Das Erste")));assertTrue(plan.stream().anyMatch(q->q.contains("Fernsehen Livestream")));assertTrue(plan.stream().anyMatch(q->q.contains("Germany")));assertTrue(plan.get(1).contains("site:github.com"));assertTrue(plan.stream().anyMatch(q->q.contains("site:daserste.example")));
        Station dw=channel("DW","https://dw.example/","DE");dw.id="DWEnglish.de";dw.feed="English";assertTrue(DeepSearchPlan.build("Deutsche Welle",Collections.singleton("DE"),Arrays.asList(dw),Arrays.asList(dw)).stream().anyMatch(q->q.contains("DW English")||q.contains("DWEnglish")));
    }
    @Test public void duckResultsDecodePublishedRedirectsAndRejectChallengeAndPrivateUrls()throws Exception{
        String body="<a class='result__a' href='//duckduckgo.com/l/?uddg=https%3A%2F%2Fnews.example%2Flive&amp;rut=abc'>News <b>live</b></a><a class='result__a' href='https://127.0.0.1/live'>private</a><a class='result__a' href='https://user:pass@news.example/live'>credentials</a><a class='advertisement' href='https://ads.example/live'>ad</a>";
        List<WebCrawler.Hit> hits=WebCrawler.duckResults(body);assertEquals(1,hits.size());assertEquals("https://news.example/live",hits.get(0).url);assertTrue(hits.get(0).title.contains("News"));
        try{WebCrawler.duckResults("<form id='challenge-form' action='/anomaly.js'>verify</form>");fail("Challenge must not be treated as search results");}catch(IOException expected){assertTrue(expected.getMessage().contains("weryfikacji"));}
    }
    @Test public void independentSearchFindsLivePageBeforeHomepageAndKeepsManifestEvidence()throws Exception{
        Station news=channel("News","https://news.example/","PL");List<String> visited=new ArrayList<>(),notes=new ArrayList<>();
        PublicHttp.Fetcher fetch=(url,cancel)->{visited.add(url);if(url.contains("bing.com"))throw new IOException("HTTP 503");if(url.contains("duckduckgo.com"))return doc(url,"<a class='result__a' href='https://news.example/live'>News live</a><a class='result__a' href='https://unknown.example/live'>News live</a>");if(url.endsWith(".m3u8"))return doc(url,LIVE);return doc(url,url.endsWith("/live")?"<title>News live</title><source src='https://cdn.example/live.m3u8'>":"<title>News</title>");};
        List<Station> result=crawler(fetch,notes).search("News",Collections.singleton("PL"),Arrays.asList(news),null,new AtomicBoolean());assertEquals(1,result.size());assertTrue(result.get(0).liveManifest);assertTrue(result.get(0).manifestCheckedAt>0);assertEquals("https://news.example/live",result.get(0).origin);assertEquals("https://news.example/live",visited.stream().filter(url->!engine(url)).findFirst().get());assertFalse(visited.contains("https://unknown.example/live"));assertEquals(1,visited.stream().filter(url->url.contains("bing.com")).count());assertTrue(notes.stream().anyMatch(n->n.startsWith("DuckDuckGo:")));
    }
    @Test public void foundPlaylistIsReadBeforePagesAndCountryScopeIsPreserved()throws Exception{
        Station news=channel("News","https://news.example/","PL"),foreign=channel("News Foreign","https://foreign.example/","DE");String listUrl="https://raw.githubusercontent.com/public/channels/main/news.m3u";List<String> visited=new ArrayList<>();int[] counts={0,0};
        PublicHttp.Fetcher fetch=(url,cancel)->{visited.add(url);if(url.contains("bing.com"))return doc(url,rss(item("Public playlist",listUrl)+item("News live","https://news.example/live")));if(url.contains("duckduckgo.com"))return doc(url,"<div class='no-results'>No results</div>");if(url.equals(listUrl))return doc(url,"#EXTM3U\n#EXTINF:-1 tvg-id='News.pl' tvg-country='PL',News\nhttps://cdn.example/playlist.m3u8\n#EXTINF:-1 tvg-id='NewsForeign.de' tvg-country='DE',News Foreign\nhttps://cdn.example/foreign.m3u8\n");return doc(url,"<title>News</title>");};
        WebCrawler search=new WebCrawler(fetch,n->{},null,null,(document,all,matching)->{assertEquals(listUrl,document.url);counts[0]=all.size();counts[1]=matching;},()->false);List<Station> found=search.search("News",Collections.singleton("PL"),Arrays.asList(news,foreign),null,new AtomicBoolean());
        assertEquals(listUrl,visited.stream().filter(url->!engine(url)).findFirst().get());assertEquals(2,counts[0]);assertEquals(1,counts[1]);assertEquals(1,found.size());assertEquals("PL",found.get(0).country);assertFalse(found.get(0).liveManifest);
    }
    @Test public void thirdLevelPlayerIsReachedButFourthLevelIsNot()throws Exception{
        Station news=channel("News","https://news.example/","PL");List<String> visited=new ArrayList<>();PublicHttp.Fetcher fetch=(url,cancel)->{visited.add(url);if(url.contains("bing.com"))return doc(url,rss(""));if(url.contains("duckduckgo.com"))return doc(url,"<div class='no-results'>No results</div>");if(url.endsWith(".m3u8"))return doc(url,LIVE);String body=url.equals(news.url)?"<a href='/live'>live</a>":url.endsWith("/live")?"<a href='/live/player'>player</a>":url.endsWith("/player")?"<iframe src='/live/player/embed'></iframe>":"<source src='https://cdn.example/live.m3u8'><a href='/live/player/embed/too-deep'>later</a>";return doc(url,body);};
        List<Station> found=crawler(fetch,new ArrayList<>()).search("News",Collections.singleton("PL"),Arrays.asList(news),null,new AtomicBoolean());assertEquals(1,found.size());assertTrue(visited.contains("https://news.example/live/player/embed"));assertFalse(visited.contains("https://news.example/live/player/embed/too-deep"));
    }
    @Test public void blockedEnginesDoNotPreventKnownBroadcasterOrTriggerRepeatedChallenges()throws Exception{
        Station news=channel("News","https://news.example/","PL");List<String> visited=new ArrayList<>();PublicHttp.Fetcher fetch=(url,cancel)->{visited.add(url);if(url.contains("bing.com"))return doc(url,"<html>unavailable</html>");if(url.contains("duckduckgo.com"))return doc(url,"<form id='challenge-form'></form>");return doc(url,url.endsWith(".m3u8")?LIVE:"<source src='https://cdn.example/live.m3u8'>");};
        assertEquals(1,crawler(fetch,new ArrayList<>()).search("News",Collections.singleton("PL"),Arrays.asList(news),null,new AtomicBoolean()).size());assertEquals(1,visited.stream().filter(url->url.contains("bing.com")).count());assertEquals(1,visited.stream().filter(url->url.contains("duckduckgo.com")).count());
    }
}
