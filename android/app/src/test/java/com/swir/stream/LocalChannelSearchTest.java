package com.swir.stream;

import org.junit.Test;
import java.util.Locale;
import static org.junit.Assert.*;

public class LocalChannelSearchTest {
    private static Station station(String name,String id,String feed) {
        Station station=new Station();station.name=name;station.id=station.tvgId=id;station.feed=feed;
        station.country="PL";station.url="https://cdn.example/live.m3u8";return station;
    }
    private static boolean matches(String query,String name,String id) {
        return LocalChannelSearch.prepare(query).matches(station(name,id,""));
    }

    @Test public void lettersAndNumbersCanBeSpacedWithoutMatchingDifferentNumbers() {
        assertTrue(matches("TVP 1","TVP1 Warszawa","TVP1Warszawa.pl"));
        assertTrue(matches("TVN 24","TVN24 BIS","TVN24BIS.pl"));
        assertTrue(matches("sport 2","Polsat Sport2","PolsatSport2.pl"));
        assertTrue(matches("HBO 2","HBO2 Europe",""));
        assertTrue(matches("TVP1 War","TVP 1 Warszawa",""));
        assertFalse(matches("TVP 1","TVP10","TVP10.pl"));
        assertFalse(matches("TVN 24","TVN240","TVN240.pl"));
        assertFalse(matches("sport 2","Polsat Sport20","PolsatSport20.pl"));
        Station wrong=station("TVP10","TVP10.pl","");wrong.aliases=new String[]{"1 HD"};
        assertFalse("Unrelated alias tokens cannot create TVP1",LocalChannelSearch.prepare("TVP 1").matches(wrong));
    }

    @Test public void camelCaseNamesWorkWithoutRequiringAnIdentifier() {
        assertTrue(matches("sport","PolsatSport",""));
        assertTrue(matches("sport 2","PolsatSport2",""));
        assertTrue(matches("Polsat S","PolsatSportExtra",""));
        assertTrue(matches("news","BBCNews",""));
        assertTrue(matches("eur","HBOEurope",""));
    }

    @Test public void wordPrefixesWorkAfterDecorationsWithAccentsAndPunctuation() {
        assertTrue(matches("pol","PL | Polsat HD",""));
        assertTrue(matches("spo","[PL] Polsat Sport HD",""));
        assertTrue(matches("lodz","Łódź — telewizja lokalna",""));
        assertTrue(matches("tele lok","Łódź — Telewizja Lokalna",""));
        assertTrue(matches("tvp-info","PL / TVP Info",""));
        assertFalse(matches("sat","Polsat",""));
    }

    @Test public void languageAndAliasFeedQueriesStayWithinOneStation() {
        Station english=station("DW","DW.de","English");english.aliases=new String[]{"Deutsche Welle"};
        Station german=station("DW","DW.de","German");german.aliases=new String[]{"Deutsche Welle"};
        assertTrue(LocalChannelSearch.prepare("DW English").matches(english));
        assertTrue(LocalChannelSearch.prepare("Deutsche Welle English").matches(english));
        assertFalse(LocalChannelSearch.prepare("DW English").matches(german));
        assertFalse(LocalChannelSearch.prepare("DW German").matches(english));
        assertTrue(matches("DW English","DW","DWEnglish.de"));
        assertFalse(LocalChannelSearch.prepare("DW English").matches(station("DW","","")));
        assertFalse(LocalChannelSearch.prepare("DW English").matches(station("English News","","")));
    }

    @Test public void plusBrandAndTimeshiftNumbersRemainDistinct() {
        assertTrue(matches("CANAL+","CANAL+ SPORT",""));
        assertTrue(matches("CANAL + SPORT","CANAL+SPORT",""));
        assertFalse(matches("CANAL+","CANAL SPORT",""));
        assertTrue(matches("Demo +1","Demo +1",""));
        assertTrue(matches("Demo + 1","Demo+1",""));
        assertFalse(matches("Demo +1","Demo 1",""));
        assertFalse(matches("Demo 1","Demo +1",""));
        assertFalse(matches("Demo +1","Demo +10",""));
    }

    @Test public void EmptyAndQualityQueriesDoNotBecomeTheSameSearch() {
        Station normal=station("Ordinary News","OrdinaryNews.pl","");
        assertTrue(LocalChannelSearch.prepare("  ").matches(normal));
        assertTrue(LocalChannelSearch.prepare(null).matches(normal));
        assertFalse(LocalChannelSearch.prepare("HD").matches(normal));
        assertTrue(matches("HD","Polsat HD",""));
        assertFalse(LocalChannelSearch.prepare("...").matches(normal));
        assertTrue(matches("bbc","BBC News",""));
        assertFalse(matches("bbx","BBC News",""));
    }

    @Test public void EveryDisplayedNameCanFindItselfIncludingNonLatinNames() {
        for(String name:new String[]{"TVP1 Warszawa","TVN24 BIS","PolsatSport2","PL | Polsat HD",
                "HBO2 Europe","CANAL+SPORT","Demo +1","DW (English) [Not 24/7]","3sat",
                "Łódź — Telewizja Lokalna","Al Jazeera English","ČT24","Das Erste (1080p)",
                "NHK WORLD-JAPAN","France 24","TV5Monde","Київ TV","凤凰卫视","العربية",
                "\uD840\uDC00 TV"}) {
            assertTrue("Displayed name must be searchable: "+name,matches(name,name,""));
        }
    }

    @Test public void playbackUrlsAndHeadersAreNeverSearchTermsAndQueriesAreReusable() {
        Station source=station("Local News","LocalNews.pl","");source.aliases=new String[]{"City Television"};
        source.url="https://sports.example/EnglishFootball.m3u8";source.referrer="https://German.example/";
        source.userAgent="SecretAgentCinema";source.group="news;local";
        assertFalse(LocalChannelSearch.prepare("Football").matches(source));
        assertFalse(LocalChannelSearch.prepare("German").matches(source));
        assertFalse(LocalChannelSearch.prepare("SecretAgent").matches(source));
        LocalChannelSearch.Query query=LocalChannelSearch.prepare("city tele");
        assertTrue(query.matches(source));assertFalse(query.matchesText("Country Television"));
        assertTrue(query.matchesText("City Television"));assertTrue(query.matches(source));
        assertTrue(LocalChannelSearch.prepare("wiad").matchesText("Wiadomości"));
    }

    @Test public void indexedEntriesAreStableSnapshotsAndNewCataloguesCanReplaceThem() {
        Station station=station("PolsatSport2","","");LocalChannelSearch.Entry indexed=LocalChannelSearch.index(station);
        LocalChannelSearch.Query query=LocalChannelSearch.prepare("sport 2");
        assertTrue(query.matches(indexed));assertTrue(query.matches(station));
        station.name="Other News";
        assertTrue("Typing must use the prepared snapshot",query.matches(indexed));
        assertFalse(query.matches(station));assertFalse(query.matches(LocalChannelSearch.index(station)));
        assertFalse(query.matches((LocalChannelSearch.Entry)null));
        assertFalse(LocalChannelSearch.prepare("").matches(LocalChannelSearch.index(null)));
    }

    @Test public void everyDisplayedNameIsSearchableInUpperAndLowerCase() {
        for(String name:new String[]{"PolsatSport","BBCNews","TVN24 BIS","PolsatSport2","TV5Monde",
                "PL | Polsat HD","CANAL+SPORT","Demo +1","DW English","Łódź TV","ČT24",
                "Straße TV","ΟσNews","ΟΣ TV","İstanbul TV","Київ TV","凤凰卫视","العربية"}) {
            LocalChannelSearch.Entry entry=LocalChannelSearch.index(station(name,"",""));
            assertTrue("Uppercase: "+name,LocalChannelSearch.prepare(name.toUpperCase(Locale.ROOT)).matches(entry));
            assertTrue("Lowercase: "+name,LocalChannelSearch.prepare(name.toLowerCase(Locale.ROOT)).matches(entry));
            assertTrue("Uppercase catalogue name: "+name,matches(name,name.toUpperCase(Locale.ROOT),""));
            assertTrue("Lowercase catalogue name: "+name,matches(name,name.toLowerCase(Locale.ROOT),""));
        }
    }

    @Test public void joinedSpellingMatchesSpacedNamesWithoutChangingNumericIdentity() {
        assertTrue(matches("TVPINFO","TVP Info",""));
        assertTrue(matches("tvn24bis","TVN 24 BIS",""));
        assertTrue(matches("POLSATSPORT2","Polsat Sport 2",""));
        assertTrue(matches("bbcnews","BBC News Europe",""));
        assertTrue(matches("TVP1","TVP 1 Warszawa",""));
        assertTrue(matches("TVP Info","TVPINFO",""));
        assertFalse(matches("TVP1","TVP 10",""));
        assertFalse(matches("TVP 1 0","TVP 10",""));
        assertFalse(matches("TVP10","TVP 1 0",""));
        assertFalse(matches("TVN24","TVN 240 BIS",""));
        assertFalse(matches("Canal+1","Canal +10",""));
        assertFalse(matches("Demo1","Demo +1",""));
        Station source=station("BBC","","");source.aliases=new String[]{"News"};
        assertFalse("Spelling cannot be assembled from separate aliases",LocalChannelSearch.prepare("BBCNEWS").matches(source));
    }
}
