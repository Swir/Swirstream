package com.swir.stream;

import java.net.URI;
import java.net.URLDecoder;
import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.regex.Matcher;
import java.util.regex.Pattern;

public final class M3u {
    private static final Pattern ATTRIBUTE=Pattern.compile("([\\w-]+)\\s*=\\s*(?:\"([^\"]*)\"|'([^']*)')");
    private static final Pattern MARKS=Pattern.compile("\\p{M}+"),WORDS=Pattern.compile("[\\p{L}\\p{N}+]+");
    public static boolean isHls(String text){return Pattern.compile("(?m)^\\s*#EXT-X-(?:TARGETDURATION|STREAM-INF|MEDIA-SEQUENCE|VERSION):").matcher(text).find();}
    public static String entities(String value){return value.replace("&amp;","&").replace("&quot;","\"").replace("&#39;","'").replace("&lt;","<").replace("&gt;",">");}
    static Map<String,String> attributes(String text){Map<String,String> result=new LinkedHashMap<>();Matcher m=ATTRIBUTE.matcher(text);while(m.find())result.put(m.group(1).toLowerCase(Locale.ROOT),entities(m.group(2)!=null?m.group(2):m.group(3)));return result;}
    static int comma(String line){char quote=0;for(int i=0;i<line.length();i++){char c=line.charAt(i);if(quote!=0){if(c==quote)quote=0;}else if(c=='\"'||c=='\'')quote=c;else if(c==',')return i;}return -1;}
    static String attr(Map<String,String> values,String key){String value=values.get(key);return value==null?"":value;}
    public static List<Station> parse(String text,String source){
        LinkedHashMap<String,Station> results=new LinkedHashMap<>();long now=System.currentTimeMillis();
        if(isHls(text)){if(PublicHttp.allowedStream(source)){Station s=new Station();s.url=source;s.name="Transmisja HLS";s.origin=source;s.fetchedAt=now;results.put(s.sourceKey(),s);}return new ArrayList<>(results.values());}
        Station station=new Station();String inheritedGroup="";
        for(String raw:text.split("\\r?\\n")){
            String line=raw.trim().replace("\uFEFF","");if(line.isEmpty())continue;
            if(line.regionMatches(true,0,"#EXTINF:",0,8)){
                station=new Station();Map<String,String> a=attributes(line);int comma=comma(line);station.name=comma<0?"":entities(line.substring(comma+1).trim());
                if(!attr(a,"tvg-name").isEmpty())station.name=attr(a,"tvg-name");String[] identity=attr(a,"tvg-id").split("@",2);station.id=station.tvgId=identity[0];station.feed=identity.length>1?identity[1]:"";
                station.group=attr(a,"group-title");if(station.group.isEmpty())station.group=inheritedGroup;station.logo=attr(a,"tvg-logo");station.country=attr(a,"tvg-country").toUpperCase(Locale.ROOT);
                Matcher country=Pattern.compile("\\.([a-zA-Z]{2})$").matcher(station.id);if(station.country.isEmpty()&&country.find())station.country=country.group(1).toUpperCase(Locale.ROOT);
                if(!station.country.matches("[A-Z]{2}"))station.country=""; else station.country=WorldMarkets.canonical(station.country);
            }else if(line.regionMatches(true,0,"#EXTGRP:",0,8)){inheritedGroup=line.substring(8);station.group=inheritedGroup;}
            else if(line.regionMatches(true,0,"#EXTVLCOPT:http-user-agent=",0,"#EXTVLCOPT:http-user-agent=".length()))station.userAgent=line.substring(line.indexOf('=')+1);
            else if(line.regionMatches(true,0,"#EXTVLCOPT:http-referrer=",0,"#EXTVLCOPT:http-referrer=".length()))station.referrer=line.substring(line.indexOf('=')+1);
            else if(!line.startsWith("#")){
                String[] parts=line.split("\\|",2);String address=entities(parts[0].trim());
                if(parts.length>1)for(String header:parts[1].split("&")){String[] pair=header.split("=",2);if(pair.length!=2)continue;try{String value=URLDecoder.decode(pair[1],"UTF-8");if(pair[0].equalsIgnoreCase("User-Agent"))station.userAgent=value;if(pair[0].equalsIgnoreCase("Referer")||pair[0].equalsIgnoreCase("Referrer"))station.referrer=value;}catch(Exception ignored){}}
                try{URI resolved=new URI(address);if(!resolved.isAbsolute()&&PublicHttp.allowedDocument(source))address=new URI(source).resolve(resolved).toString();}catch(Exception ignored){address="";}
                if(PublicHttp.allowedStream(address)){
                    station.url=address;station.origin=source;station.fetchedAt=now;if(station.name.isEmpty())station.name=address;
                    if(!PublicHttp.allowedDocument(station.referrer))station.referrer="";station.userAgent=clean(station.userAgent);if(!results.containsKey(station.sourceKey()))results.put(station.sourceKey(),station);
                    if(results.size()>=30000)break;
                }station=new Station();
            }
        }
        return new ArrayList<>(results.values());
    }
    static String clean(String text){return text==null?"":text.replace('\r',' ').replace('\n',' ');}
    static String quoted(String text){return clean(text).replace('"','\'');}
    public static String export(List<Station> stations){
        StringBuilder text=new StringBuilder("#EXTM3U\n");Map<String,Boolean> seen=new LinkedHashMap<>();
        for(Station s:stations){if(!PublicHttp.allowedStream(s.url)||seen.put(s.sourceKey(),true)!=null)continue;
            text.append("#EXTINF:-1 tvg-id=\"").append(quoted(s.tvgId.isEmpty()?s.id:s.tvgId)).append(s.feed.isEmpty()?"":"@"+quoted(s.feed)).append("\" tvg-country=\"").append(quoted(s.country)).append("\" tvg-logo=\"").append(quoted(s.logo)).append("\" group-title=\"").append(quoted(s.group)).append("\",").append(clean(s.name)).append('\n');
            if(!s.userAgent.isEmpty())text.append("#EXTVLCOPT:http-user-agent=").append(clean(s.userAgent)).append('\n');if(PublicHttp.allowedDocument(s.referrer))text.append("#EXTVLCOPT:http-referrer=").append(clean(s.referrer)).append('\n');
            if(!s.origin.isEmpty())text.append("#SWIRSTREAM-SOURCE: ").append(clean(s.origin)).append('\n');text.append(s.url).append('\n');
        }return text.toString();
    }
    public static List<String> epgUrls(String text){
        List<String> urls=new ArrayList<>();String header=text.trim().replace("\uFEFF","").split("\\r?\\n",2)[0];Map<String,String> a=attributes(header);
        for(String key:new String[]{"x-tvg-url","url-tvg","tvg-url"})for(String url:attr(a,key).split(",")){url=url.trim();if(PublicHttp.allowedDocument(url)&&!urls.contains(url)&&urls.size()<8)urls.add(url);}return urls;
    }
    public static boolean matches(String query,Station station){
        String needle=Station.normalizeName(query);if(needle.isEmpty())return true;List<String> names=searchNames(station);
        boolean numeric=false;for(int i=0;i<needle.length();i++)if(Character.isDigit(needle.charAt(i))){numeric=true;break;}for(String name:names){String candidate=Station.normalizeName(name);if(candidate.equals(needle)||!numeric&&candidate.startsWith(needle))return true;}
        StringBuilder haystack=new StringBuilder(station.group);for(String name:names)haystack.append(' ').append(name);
        String hay=fold(haystack.toString());Matcher words=WORDS.matcher(fold(query));boolean any=false;
        while(words.find()){any=true;if(!containsWord(hay,words.group()))return false;}return any;
    }
    /** Identifiers often retain a feed's name after the directory renames it. */
    static List<String> searchNames(Station station){
        List<String> names=new ArrayList<>();names.add(station.name);for(String alias:station.aliases)names.add(alias);
        if(!station.feed.isEmpty())names.add(station.name+" "+identifierName(station.feed));
        if("tv".equals(station.kind)){for(String id:new String[]{station.id,station.tvgId}){String name=identifierName(id);if(!name.isEmpty()&&!names.contains(name))names.add(name);}}
        return names;
    }
    static String identifierName(String id){
        if(id==null||id.isEmpty())return "";int at=id.indexOf('@');if(at>=0)id=id.substring(0,at)+" "+id.substring(at+1);
        int dot=id.lastIndexOf('.');if(dot>=0&&id.length()-dot==3&&Character.isLetter(id.charAt(dot+1))&&Character.isLetter(id.charAt(dot+2)))id=id.substring(0,dot);
        StringBuilder name=new StringBuilder();for(int i=0;i<id.length();i++){char c=id.charAt(i);if(i>0&&Character.isUpperCase(c)&&(Character.isLowerCase(id.charAt(i-1))||i+1<id.length()&&Character.isUpperCase(id.charAt(i-1))&&Character.isLowerCase(id.charAt(i+1))))name.append(' ');name.append(c=='_'?' ':c);}return name.toString();
    }
    static boolean containsWord(String text,String word){for(int at=text.indexOf(word);at>=0;at=text.indexOf(word,at+1)){int end=at+word.length();if((at==0||!Character.isLetterOrDigit(text.codePointBefore(at)))&&(end==text.length()||!Character.isLetterOrDigit(text.codePointAt(end))))return true;}return false;}
    static String fold(String text){return MARKS.matcher(java.text.Normalizer.normalize(text,java.text.Normalizer.Form.NFKD)).replaceAll("").replace('ł','l').replace('Ł','L').toLowerCase(Locale.ROOT);}
}
