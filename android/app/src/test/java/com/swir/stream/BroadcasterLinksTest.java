package com.swir.stream;

import static org.junit.Assert.*;
import java.util.*;
import org.junit.Test;

public class BroadcasterLinksTest {
    @Test public void readsNavigationLabelsAndRanksLocalLivePages() {
        String home = "https://tv.example/";
        String html = "<a href='/director'>Director</a><a href='/replay/direct'>Direct</a>"
                + "<a href='https://other.example/live'>Live</a><a href='/live.png'>Live</a>"
                + "<a href='/watch?password=test'>Live</a>"
                + "<a href='/notiziari/rai'><span>LIVE</span></a>"
                + "<a href='/emision' aria-label='En directo'></a>"
                + "<a href='/fr/direct#player'>Direct TV</a><a href='/fr/direct'>Direct TV</a>";
        PublicHttp.Document doc = new PublicHttp.Document(home, html, "text/html", 0, 0);
        assertEquals(Arrays.asList(home + "fr/direct", home + "notiziari/rai", home + "emision"),
                WebCrawler.links(doc, false));
    }
    @Test public void targetsEachSelectedCountryInItsOwnQuery() {
        Set<String> countries = new LinkedHashSet<>(Arrays.asList("FR", "IT"));
        List<String> queries = DeepSearchPlan.build("News", countries, Collections.emptyList(), Collections.emptyList());
        assertTrue(queries.stream().anyMatch(q -> q.contains("TV diretta") && q.contains("Italy")));
        assertFalse(queries.stream().anyMatch(q -> q.contains("France") && q.contains("Italy")));
        assertTrue(queries.size() <= DeepSearchPlan.MAX_QUERIES);
    }
    @Test public void multilingualMarketsAndNativeLiveNavigation() {
        assertTrue(WorldMarkets.terms("JP",false).get(0).contains("生放送"));
        assertTrue(WorldMarkets.terms("EG",false).get(0).contains("بث مباشر"));
        assertTrue(WorldMarkets.terms("CA",false).stream().anyMatch(s -> s.contains("en direct")));
        assertEquals("United Kingdom", WorldMarkets.name("GB"));
        assertEquals("uk",WorldMarkets.tvCatalogCode("GB"));
        String home = "https://tv.example/";
        String html = "<a href='/one'>番組の生放送</a><a href='/two'>البث مباشر</a><a href='/old'>直播回放</a>";
        assertEquals(Arrays.asList(home+"one",home+"two"), WebCrawler.links(new PublicHttp.Document(home,html,"text/html",0,0),false));
    }
}
