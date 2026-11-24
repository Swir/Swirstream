package com.swir.stream;

import java.util.ArrayList;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Locale;
import java.util.Set;

/** Small deterministic expansion; never creates media URLs or changes identity. */
final class DeepSearchPlan {
    static final int MAX_QUERIES=6,MAX_PAGES=60,MAX_DEPTH=3;
    static final long SEARCH_MILLIS=25_000,CRAWL_MILLIS=90_000;
    static List<String> build(String query,Set<String> countries,List<Station> exact,List<Station> seeds){
        String requested=clean(query);Station primary=!exact.isEmpty()?exact.get(0):!seeds.isEmpty()?seeds.get(0):null;
        LinkedHashSet<String> names=new LinkedHashSet<>();names.add(requested);
        if(primary!=null)for(String name:M3u.searchNames(primary))if(!clean(name).isEmpty())names.add(clean(name));
        String country = primary != null ? primary.country : countries.isEmpty() ? "" : countries.iterator().next();
        String countryWords = WorldMarkets.name(country), nativeWords = WorldMarkets.terms(country,false).get(0);
        LinkedHashSet<String> queries=new LinkedHashSet<>();
        queries.add(requested+" TV live official "+(countries.size()==1?countryWords:""));
        // Published playlists get a query before less important name variants.
        queries.add("site:github.com "+requested+" public playlist m3u "+countryWords);
        if(primary!=null&&!WebCrawler.host(primary.url).isEmpty())queries.add("site:"+WebCrawler.host(primary.url)+" "+(requested.isEmpty()?clean(primary.name):requested)+" "+nativeWords);
        Set<String> markets = new LinkedHashSet<>();
        for (Station station : seeds) if (!station.country.isEmpty() && (countries.isEmpty() || countries.contains(station.country))) markets.add(station.country);
        markets.addAll(countries);
        for (String selected : markets) {
            if (selected.equals(country)) continue;
            if (queries.size() >= MAX_QUERIES - 2) break;
            queries.add(requested + " " + WorldMarkets.terms(selected,false).get(0) + " " + WorldMarkets.name(selected));
        }
        queries.add(requested+" "+nativeWords+" "+countryWords);
        for(String name:names)if(!name.equalsIgnoreCase(requested)&&queries.size()<MAX_QUERIES)queries.add(name+" "+nativeWords+" "+countryWords);
        for(String phrase:WorldMarkets.terms(country,false))if(queries.size()<MAX_QUERIES)queries.add(requested+" "+phrase+" "+countryWords);
        if(queries.size()<MAX_QUERIES)queries.add(requested+" livestream m3u8 "+countryWords);
        List<String> result=new ArrayList<>();for(String value:queries){if(result.size()>=MAX_QUERIES)break;result.add(value.trim());}return result;
    }
    private static String clean(String value){if(value==null)return "";String clean=value.replace('\r',' ').replace('\n',' ').trim();return clean.length()>160?clean.substring(0,160):clean;}
    static boolean livePage(String url){String value=url.toLowerCase(Locale.ROOT);for(String token:new String[]{"live","na-zywo","na_zywo","player","embed","stream","diretta","en-direct","ao-vivo","zive"})if(value.contains(token))return true;return false;}
}
