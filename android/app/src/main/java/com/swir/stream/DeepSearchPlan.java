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
        if(requested.isEmpty())requested=primary==null?"public TV":clean(primary.name);
        LinkedHashSet<String> names=new LinkedHashSet<>();names.add(requested);
        if(primary!=null)for(String name:M3u.searchNames(primary))if(!clean(name).isEmpty())names.add(clean(name));
        String countryWords=countryWords(countries),nativeWords=nativeWords(countries,primary);
        LinkedHashSet<String> queries=new LinkedHashSet<>();
        queries.add(requested+" TV live official "+countryWords);
        // Published playlists get a query before less important name variants.
        queries.add("site:github.com "+requested+" public playlist m3u "+countryWords);
        if(primary!=null&&!WebCrawler.host(primary.url).isEmpty())queries.add("site:"+WebCrawler.host(primary.url)+" "+requested+" "+nativeWords);
        queries.add(requested+" "+nativeWords+" "+countryWords);
        for(String name:names)if(!name.equalsIgnoreCase(requested)&&queries.size()<MAX_QUERIES)queries.add(name+" "+nativeWords+" "+countryWords);
        if(queries.size()<MAX_QUERIES)queries.add(requested+" livestream m3u8 "+countryWords);
        List<String> result=new ArrayList<>();for(String value:queries){if(result.size()>=MAX_QUERIES)break;result.add(value.trim());}return result;
    }
    private static String clean(String value){if(value==null)return "";String clean=value.replace('\r',' ').replace('\n',' ').trim();return clean.length()>160?clean.substring(0,160):clean;}
    private static String countryWords(Set<String> countries){StringBuilder words=new StringBuilder();int count=0;for(String code:countries){if(count++>=3)break;if(words.length()>0)words.append(' ');words.append(new Locale("",code).getDisplayCountry(Locale.ENGLISH));}return words.toString();}
    private static String nativeWords(Set<String> countries,Station primary){String country=primary!=null?primary.country:countries.isEmpty()?"":countries.iterator().next();switch(country){case "PL":return "telewizja na żywo";case "DE":case "AT":case "CH":return "Fernsehen Livestream";case "FR":case "BE":return "télévision en direct";case "ES":case "MX":case "AR":return "televisión en directo";case "IT":return "TV diretta";case "PT":case "BR":return "TV ao vivo";case "NL":return "televisie live kijken";case "CZ":return "televize živě";case "UA":return "телебачення наживо";default:return "live TV";}}
    static boolean livePage(String url){String value=url.toLowerCase(Locale.ROOT);for(String token:new String[]{"live","na-zywo","na_zywo","player","embed","stream","diretta","en-direct","ao-vivo","zive"})if(value.contains(token))return true;return false;}
}
