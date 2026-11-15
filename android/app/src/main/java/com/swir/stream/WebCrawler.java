package com.swir.stream;

import org.json.JSONObject;
import org.xmlpull.v1.XmlPullParser;
import org.xmlpull.v1.XmlPullParserFactory;
import java.io.File;
import java.io.IOException;
import java.io.StringReader;
import java.net.URI;
import java.net.URLEncoder;
import java.net.URLDecoder;
import java.util.ArrayDeque;
import java.util.ArrayList;
import java.util.Collections;
import java.util.HashMap;
import java.util.HashSet;
import java.util.LinkedHashMap;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.Set;
import java.util.concurrent.atomic.AtomicBoolean;
import java.util.regex.Matcher;
import java.util.regex.Pattern;

/** Bounded real search and public broadcaster-page crawler; not a whole-web index. */
final class WebCrawler {
    interface Log {void write(String text);}
    interface PlaylistSink {void accept(PublicHttp.Document document,List<Station> stations,int matching);}
    interface CanSave {boolean allowed();}
    static final class Hit {final String title,url;Hit(String title,String url){this.title=title;this.url=url;}}
    static final class Page {final String url;final Station station;final int depth;Page(String url,Station station,int depth){this.url=url;this.station=station;this.depth=depth;}}
    private final PublicHttp.Fetcher fetcher;private final Log log;private final File visitsFile;private final CatalogRepository.Ranking ranking;private final PlaylistSink playlists;private final CanSave canSave;private final Object historyLock;
    private long deadlineMillis=Long.MAX_VALUE;
    private final Map<String,Long> visits=new HashMap<>();
    WebCrawler(PublicHttp.Fetcher fetcher,Log log,File visitsFile,CatalogRepository.Ranking ranking,PlaylistSink playlists,CanSave canSave){this(fetcher,log,visitsFile,ranking,playlists,canSave,new Object());}
    WebCrawler(PublicHttp.Fetcher fetcher,Log log,File visitsFile,CatalogRepository.Ranking ranking,PlaylistSink playlists,CanSave canSave,Object historyLock){this.fetcher=fetcher;this.log=log;this.visitsFile=visitsFile;this.ranking=ranking;this.playlists=playlists;this.canSave=canSave;this.historyLock=historyLock;if(visitsFile!=null)try{JSONObject saved=CatalogRepository.readJson(visitsFile,4_000_000);java.util.Iterator<String> keys=saved.keys();while(keys.hasNext()){String key=keys.next();long time=saved.optLong(key);if(PublicHttp.allowedDocument(key)&&time<=System.currentTimeMillis()+60000)visits.put(key,time);}}catch(Exception ignored){}}
    private PublicHttp.Document fetch(String url,AtomicBoolean cancelled)throws IOException{PublicHttp.checkCancelled(cancelled);if(System.currentTimeMillis()>=deadlineMillis)throw new IOException("Limit czasu analizy WWW.");PublicHttp.Document doc=fetcher.fetch(url,cancelled);PublicHttp.checkCancelled(cancelled);if(System.currentTimeMillis()>deadlineMillis)throw new IOException("Limit czasu analizy WWW.");return doc;}
    static String host(String url){try{String host=new URI(url).getHost().toLowerCase(Locale.ROOT);return host.startsWith("www.")?host.substring(4):host;}catch(Exception e){return "";}}
    static boolean sameDomain(String url,String website){if(!PublicHttp.allowedDocument(url)||!PublicHttp.allowedDocument(website))return false;String a=host(url),b=host(website);return a.equals(b)||a.endsWith("."+b);}
    /** Index once per search; never validate every directory URL for every hit. */
    static final class BroadcasterIndex {
        private final Map<String,List<Station>> byHost=new HashMap<>(),byUrl=new HashMap<>();
        BroadcasterIndex(List<Station> directory){
            Map<String,String> hosts=new HashMap<>();
            for(Station station:directory){String site=station.url;if(site.isEmpty())continue;String domain=hosts.get(site);if(domain==null){domain=PublicHttp.allowedDocument(site)?host(site):"";hosts.put(site,domain);}if(domain.isEmpty())continue;
                List<Station> domainRows=byHost.get(domain);if(domainRows==null){domainRows=new ArrayList<>();byHost.put(domain,domainRows);}domainRows.add(station);
                List<Station> pageRows=byUrl.get(site);if(pageRows==null){pageRows=new ArrayList<>();byUrl.put(site,pageRows);}pageRows.add(station);
            }
        }
        List<Station> matchingHost(String url){
            List<Station> matches=new ArrayList<>();if(!PublicHttp.allowedDocument(url))return matches;String domain=host(url);
            while(!domain.isEmpty()){List<Station> rows=byHost.get(domain);if(rows!=null)matches.addAll(rows);int dot=domain.indexOf('.');if(dot<0)break;domain=domain.substring(dot+1);}return matches;
        }
        boolean sharedHomepage(String url){List<Station> rows=byUrl.get(url);if(rows==null||rows.size()<2)return false;String first=rows.get(0).key();for(int i=1;i<rows.size();i++)if(!first.equals(rows.get(i).key()))return true;return false;}
    }
    static Station copy(Station station){return Station.fromJson(station.toJson());}
    static List<Hit> searchResults(String xml)throws IOException{
        if(xml.length()>2_000_000||xml.contains("<!DOCTYPE")||xml.contains("<!ENTITY"))throw new IOException("Nieprawidłowe XML wyszukiwarki.");List<Hit> result=new ArrayList<>();
        try{XmlPullParser parser=XmlPullParserFactory.newInstance().newPullParser();parser.setInput(new StringReader(xml));boolean rss=false,item=false;String title="",url="";int event;
            while((event=parser.next())!=XmlPullParser.END_DOCUMENT){if(event==XmlPullParser.START_TAG){String name=parser.getName();if(name.equals("rss"))rss=true;if(name.equals("item")){item=true;title=url="";}else if(item&&name.equals("title"))title=parser.nextText();else if(item&&name.equals("link"))url=parser.nextText();}else if(event==XmlPullParser.END_TAG&&parser.getName().equals("item")){if(PublicHttp.allowedDocument(url)&&result.size()<10)result.add(new Hit(title,url));item=false;}}
            if(!rss)throw new IOException("Wyszukiwarka nie zwróciła RSS (blokada lub zmiana usługi).");return result;
        }catch(IOException e){throw e;}catch(Exception e){throw new IOException("Nieprawidłowy wynik wyszukiwarki.",e);}
    }
    static List<Hit> duckResults(String html)throws IOException{
        if(html.length()>2_000_000)throw new IOException("Za duża strona wyszukiwarki.");
        String lower=html.toLowerCase(Locale.ROOT);if(lower.contains("anomaly.js")||lower.contains("challenge-form")||lower.contains("g-recaptcha"))throw new IOException("DuckDuckGo wymaga weryfikacji; pomijam usługę.");
        List<Hit> result=new ArrayList<>();Matcher links=Pattern.compile("<a\\b([^>]{0,8192})>([\\s\\S]{0,8192}?)</a\\s*>",Pattern.CASE_INSENSITIVE).matcher(html);
        Pattern attribute=Pattern.compile("([a-z_-]+)\\s*=\\s*([\"'])(.*?)\\2",Pattern.CASE_INSENSITIVE),tags=Pattern.compile("<[^>]*>");
        while(links.find()&&result.size()<10){String classes="",href="";Matcher fields=attribute.matcher(links.group(1));while(fields.find()){if(fields.group(1).equalsIgnoreCase("class"))classes=fields.group(3);if(fields.group(1).equalsIgnoreCase("href"))href=M3u.entities(fields.group(3));}
            boolean validClass=false;for(String value:classes.split("\\s+"))if(value.equals("result__a"))validClass=true;if(!validClass)continue;
            String address=resolve("https://html.duckduckgo.com/",href);if(host(address).equals("duckduckgo.com"))try{URI uri=new URI(address);if(!uri.getPath().equals("/l/"))continue;String encoded=uri.getRawQuery();String decoded="";if(encoded!=null)for(String pair:encoded.split("&")){String[] parts=pair.split("=",2);if(parts.length==2&&parts[0].equals("uddg"))decoded=URLDecoder.decode(parts[1],"UTF-8");}address=decoded;}catch(Exception e){continue;}
            if(PublicHttp.allowedDocument(address)&&!host(address).endsWith("duckduckgo.com"))result.add(new Hit(M3u.entities(tags.matcher(links.group(2)).replaceAll(" ")).trim(),address));
        }
        if(result.isEmpty()&&!lower.contains("no-results")&&!lower.contains("no results"))throw new IOException("Nie rozpoznano wyników DuckDuckGo (blokada lub zmiana strony).");return result;
    }
    static Station identify(String title,String url,List<Station> directory){
        return identify(title,url,new BroadcasterIndex(directory));
    }
    static Station identify(String title,String url,BroadcasterIndex index){
        List<Station> same=index.matchingHost(url);Station best=null;int longest=-1;Station label=new Station();label.name=title;
        for(Station s:same){if(M3u.matches(s.name,label)&&s.name.length()>longest){best=s;longest=s.name.length();}for(String alias:s.aliases)if(M3u.matches(alias,label)&&alias.length()>longest){best=s;longest=alias.length();}}
        if(best!=null)return best;Set<String> identities=new HashSet<>();for(Station s:same)identities.add(s.key());return identities.size()==1?same.get(0):null;
    }
    static List<String> media(PublicHttp.Document document){
        String text=M3u.entities(document.text).replace("\\/","/").replace("\\u0026","&").replace("\\u003d","=").replace("\\u003D","=");List<String> result=new ArrayList<>();
        for(String pattern:new String[]{"(?:https?://|//)[^\\s<>\"'\\\\]+?\\.(?:m3u8|mpd|mp3|aac)(?:\\?[^\\s<>\"'\\\\]*)?","(?:src|file|url)\\s*[=:]\\s*[\"']([^\"']+\\.(?:m3u8|mpd|mp3|aac)(?:\\?[^\"']*)?)[\"']"}){Matcher m=Pattern.compile(pattern,Pattern.CASE_INSENSITIVE).matcher(text);while(m.find()&&result.size()<12){String candidate=m.groupCount()>0?m.group(1):m.group();String resolved=resolve(document.url,candidate);if(PublicHttp.allowedDocument(resolved)&&!result.contains(resolved))result.add(resolved);}}
        return result;
    }
    static String resolve(String base,String relative){try{return new URI(base).resolve(relative).toString();}catch(Exception e){return "";}}
    static List<String> links(PublicHttp.Document doc,boolean iframe){
        List<String> result=new ArrayList<>();String regex=iframe?"<iframe\\b[^>]*?\\bsrc\\s*=\\s*[\"']([^\"']+)[\"']":"(?:href|src)\\s*=\\s*[\"']([^\"']+)[\"']";
        Matcher m=Pattern.compile(regex,Pattern.CASE_INSENSITIVE).matcher(doc.text);while(m.find()){String address=resolve(doc.url,M3u.entities(m.group(1)));String lower=address.toLowerCase(Locale.ROOT);if(!PublicHttp.allowedDocument(address)||PublicHttp.browserOnly(address)||!lower.matches(".*(?:live|na-zywo|na_zywo|player|embed|stream).*"))continue;if(lower.matches(".*\\.(?:js|css|png|jpg|svg|mp4)(?:\\?.*)?"))continue;if(!iframe&&!host(address).equals(host(doc.url)))continue;if(iframe&&host(address).matches(".*(?:doubleclick|googleads|adservice|adsystem).*"))continue;if(!result.contains(address))result.add(address);}
        Collections.sort(result,(a,b)->Boolean.compare(!a.matches(".*(?:live-tv|/live,|ogladaj-na-zywo|/livestream|/player).*"),!b.matches(".*(?:live-tv|/live,|ogladaj-na-zywo|/livestream|/player).*")));
        return new ArrayList<>(result.subList(0,Math.min(result.size(),iframe?2:3)));
    }
    boolean isLive(String url,AtomicBoolean cancelled,int depth)throws IOException{
        if(depth>2||url.toLowerCase(Locale.ROOT).matches(".*(?:hlsvod|/vod/|\\.mp4(?:[./?]|$)).*"))return false;PublicHttp.Document doc=fetch(url,cancelled);String body=doc.text.trim().replace("\uFEFF","");
        if(body.startsWith("#EXTM3U")){if(body.contains("#EXT-X-ENDLIST")||body.contains("#EXT-X-PLAYLIST-TYPE:VOD"))return false;if(body.contains("#EXT-X-STREAM-INF:")){String[] lines=body.split("\\r?\\n");int tried=0;for(int i=0;i<lines.length-1&&tried<3;i++)if(lines[i].startsWith("#EXT-X-STREAM-INF:")){for(int j=i+1;j<lines.length;j++)if(!lines[j].trim().isEmpty()&&!lines[j].trim().startsWith("#")){String child=resolve(doc.url,lines[j].trim());if(PublicHttp.allowedDocument(child)){tried++;try{if(isLive(child,cancelled,depth+1))return true;}catch(IOException e){PublicHttp.checkCancelled(cancelled);}}break;}}return false;}return body.contains("#EXT-X-MEDIA-SEQUENCE:")&&body.contains("#EXTINF:");}
        return body.matches("(?s)^(?:<\\?xml[^>]*>\\s*)?<MPD\\b[^>]*\\btype\\s*=\\s*[\"']dynamic[\"'].*");
    }
    static String playlistUrl(String address){
        if(!PublicHttp.allowedDocument(address)||SourcePrivacy.publicCredentials(address))return null;try{URI uri=new URI(address);if(!uri.getPath().toLowerCase(Locale.ROOT).matches(".*\\.m3u8?$"))return null;String host=uri.getHost();if(host.equals("github.com")){String[] p=uri.getPath().split("/");if(p.length<6||!p[3].equals("blob"))return null;StringBuilder out=new StringBuilder("https://raw.githubusercontent.com/").append(p[1]).append('/').append(p[2]);for(int i=4;i<p.length;i++)out.append('/').append(p[i]);return out.toString();}String clean=uri.toASCIIString();int fragment=clean.indexOf('#');return fragment<0?clean:clean.substring(0,fragment);}catch(Exception e){return null;}
    }
    long last(Station s){Long value=visits.get(s.url);return value==null?0:value;}
    List<Station> ordered(List<Station> seeds){
        List<Station> oldest=new ArrayList<>(seeds);Collections.sort(oldest,(a,b)->{int date=Long.compare(last(a),last(b));return date!=0?date:a.name.compareToIgnoreCase(b.name);});
        if(ranking==null)return oldest;List<Station> preferred=new ArrayList<>(seeds);try{ranking.reorder(preferred);}catch(RuntimeException e){log.write("Uczenie kolejności niedostępne; zachowano zwykłe źródła.");return oldest;}
        // Only accept a reordering of the original scope. A callback cannot inject
        // another country/channel or silently remove a broadcaster.
        Map<String,Station> allowed=new LinkedHashMap<>();for(Station s:seeds)allowed.put(s.key()+"|"+s.url,s);List<Station> clean=new ArrayList<>();Set<String> used=new HashSet<>();for(Station s:preferred){String key=s.key()+"|"+s.url;if(allowed.containsKey(key)&&used.add(key))clean.add(allowed.get(key));}for(Map.Entry<String,Station> e:allowed.entrySet())if(used.add(e.getKey()))clean.add(e.getValue());
        List<Station> out=new ArrayList<>();used.clear();int p=0,e=0;while(out.size()<clean.size()){boolean explore=out.size()%4==3;List<Station> source=explore?oldest:clean;int index=explore?e:p;while(index<source.size()&&used.contains(source.get(index).url))index++;if(index>=source.size()){source=explore?clean:oldest;index=0;while(index<source.size()&&used.contains(source.get(index).url))index++;}if(index>=source.size())break;Station s=source.get(index);if(explore)e=index+1;else p=index+1;if(used.add(s.url))out.add(s);}return out;
    }
    List<Station> search(String query,Set<String> countries,List<Station> directory,CatalogRepository.Progress progress,AtomicBoolean cancelled)throws IOException{
        if(SourceLinkParser.importCandidate(query))throw new IOException("Dane źródła wymagają prywatnego importu. Nie uruchomiono wyszukiwania WWW.");
        query=query.trim();long deadline=System.currentTimeMillis()+DeepSearchPlan.CRAWL_MILLIS;deadlineMillis=deadline;BroadcasterIndex broadcasters=new BroadcasterIndex(directory);List<Station> seeds=new ArrayList<>(),exact=new ArrayList<>();String key=Station.normalizeName(query);
        for(Station s:directory)if((countries.isEmpty()||countries.contains(s.country))&&PublicHttp.allowedDocument(s.url)){boolean same=CatalogRepository.exactName(key,s);if(!key.isEmpty()&&same)exact.add(s);if(query.isEmpty()||M3u.matches(query,s)||Station.normalizeName(s.name).length()>=2&&key.startsWith(Station.normalizeName(s.name)))seeds.add(s);}
        List<Station> ranked=ordered(seeds);ArrayDeque<Page> homepages=new ArrayDeque<>(),searchPages=new ArrayDeque<>();Set<String> rootUrls=new HashSet<>();for(Station s:exact)if(rootUrls.size()<2&&rootUrls.add(s.url))homepages.add(new Page(s.url,s,0));for(Station s:ranked)if(rootUrls.size()<12&&rootUrls.add(s.url))homepages.add(new Page(s.url,s,0));
        List<Station> result=new ArrayList<>();List<String> queries=new ArrayList<>(DeepSearchPlan.build(query,countries,exact,ranked));String publicQuery=query.replace('\r',' ').replace('\n',' ');if(publicQuery.length()>160)publicQuery=publicQuery.substring(0,160);queries.add(1,"public TV playlist m3u "+publicQuery+" "+String.join(" ",countries));if(queries.size()>DeepSearchPlan.MAX_QUERIES)queries.remove(queries.size()-1);Set<String> playlistUrls=new LinkedHashSet<>();
        long searchDeadline=Math.min(deadline-35000,System.currentTimeMillis()+DeepSearchPlan.SEARCH_MILLIS);boolean bingAvailable=true,duckAvailable=true;int queryIndex=0;
        for(String search:queries){PublicHttp.checkCancelled(cancelled);if(System.currentTimeMillis()>=searchDeadline)break;int useful=0;
            CatalogRepository.progress(progress,"Internet: szukam stron i playlist ("+(++queryIndex)+"/"+queries.size()+")…");
            if(bingAvailable)try{String endpoint="https://www.bing.com/search?format=rss&q="+URLEncoder.encode(search,"UTF-8");List<Hit> hits=searchResults(fetch(endpoint,cancelled).text);log.write("Bing RSS: "+hits.size()+" stron dla "+search);useful=enqueueHits(hits,query,countries,broadcasters,searchPages,rootUrls,playlistUrls);}
                catch(IOException e){PublicHttp.checkCancelled(cancelled);bingAvailable=false;log.write("Bing niedostępny: "+e.getMessage()+". Sprawdzam niezależną wyszukiwarkę.");}
            // One independent lookup even when Bing works; additional fallback
            // requests are limited by the same shared 25-second search budget.
            if(duckAvailable&&(queryIndex==1||!bingAvailable||useful==0)&&System.currentTimeMillis()<searchDeadline)try{String endpoint="https://html.duckduckgo.com/html/?q="+URLEncoder.encode(search,"UTF-8");List<Hit> hits=duckResults(fetch(endpoint,cancelled).text);log.write("DuckDuckGo: "+hits.size()+" stron dla "+search);enqueueHits(hits,query,countries,broadcasters,searchPages,rootUrls,playlistUrls);}
                catch(IOException e){PublicHttp.checkCancelled(cancelled);duckAvailable=false;log.write("DuckDuckGo niedostępne: "+e.getMessage()+". Kontynuuję strony nadawców.");}
            if(!bingAvailable&&!duckAvailable)break;
        }
        CatalogRepository.DirectoryIndex playlistIndex=null;int lists=0;long playlistDeadline=Math.min(deadline-30000,System.currentTimeMillis()+20000);
        for(String address:playlistUrls){PublicHttp.checkCancelled(cancelled);if(lists++>=4||System.currentTimeMillis()>=playlistDeadline)break;try{CatalogRepository.progress(progress,"Internet: sprawdzam znalezioną publiczną playlistę…");PublicHttp.Document doc=fetch(address,cancelled);if(!CatalogRepository.usable(doc,CatalogRepository.MAX_AGE))throw new IOException("Nieaktualna playlista.");if(playlistIndex==null)playlistIndex=new CatalogRepository.DirectoryIndex(directory);List<Station> all=CatalogRepository.playlist(doc,playlistIndex,"");List<Station> matches=CatalogRepository.select(all,countries,query);result.addAll(matches);playlists.accept(doc,all,matches.size());log.write("Znaleziona playlista: "+all.size()+" adresów; pasuje "+matches.size()+".");}catch(IOException e){PublicHttp.checkCancelled(cancelled);log.write("Znaleziona playlista: "+e.getMessage());}}
        // Specific live pages discovered by either engine precede generic roots.
        List<Page> prioritized=new ArrayList<>(searchPages);Collections.sort(prioritized,(a,b)->Boolean.compare(!DeepSearchPlan.livePage(a.url),!DeepSearchPlan.livePage(b.url)));ArrayDeque<Page> queue=new ArrayDeque<>(prioritized);queue.addAll(homepages);
        Set<String> visited=new HashSet<>();
        while(!queue.isEmpty()&&visited.size()<DeepSearchPlan.MAX_PAGES&&System.currentTimeMillis()<deadline){PublicHttp.checkCancelled(cancelled);Page page=queue.remove();if(!visited.add(page.url))continue;if(page.depth==0)visits.put(page.station.url,System.currentTimeMillis());CatalogRepository.progress(progress,"Internet: analizuję "+host(page.url)+" ("+visited.size()+"/"+DeepSearchPlan.MAX_PAGES+")");
            try{PublicHttp.Document doc=fetch(page.url,cancelled);List<String> media=media(doc);Matcher titleMatch=Pattern.compile("<title[^>]*>(.*?)</title>",Pattern.CASE_INSENSITIVE|Pattern.DOTALL).matcher(doc.text);String title=titleMatch.find()?M3u.entities(titleMatch.group(1)):"";
                Station channel=broadcasters.sharedHomepage(page.station.url)?identify(title,doc.url,broadcasters):page.station;
                Matcher tvp=Pattern.compile("^https://vod\\.tvp\\.pl/live,\\d+/[^,]+,(\\d+)").matcher(doc.url);if(tvp.find())try{media.addAll(media(fetch("https://vod.tvp.pl/api/products/"+tvp.group(1)+"/videos/playlist?platform=BROWSER&videoType=LIVE",cancelled)));}catch(IOException e){log.write("Publiczne API nadawcy: "+e.getMessage());}
                if(channel!=null&&(countries.isEmpty()||countries.contains(channel.country)))for(String address:media){if(System.currentTimeMillis()>deadline)break;try{if(!isLive(address,cancelled,0))continue;Station found=copy(channel);found.url=address;found.origin=found.homepage=doc.url;found.referrer=doc.url;found.fetchedAt=doc.fetchedAt;found.modifiedAt=0;found.liveManifest=true;found.manifestCheckedAt=System.currentTimeMillis();List<String> aliases=new ArrayList<>();Collections.addAll(aliases,found.aliases);aliases.add(title);found.aliases=aliases.toArray(new String[0]);result.add(found);log.write("Manifest live: "+host(address)+"; odtwarzanie jeszcze niepotwierdzone.");}catch(IOException e){PublicHttp.checkCancelled(cancelled);log.write("Manifest: "+e.getMessage());}}
                if(page.depth<DeepSearchPlan.MAX_DEPTH){for(String link:links(doc,false))queue.add(new Page(link,page.station,page.depth+1));for(String link:links(doc,true))queue.add(new Page(link,page.station,page.depth+1));}log.write("Odczyt strony "+host(doc.url)+": "+media.size()+" adresów mediów.");
            }catch(IOException e){PublicHttp.checkCancelled(cancelled);log.write("Strona "+host(page.url)+": "+e.getMessage());}}
        PublicHttp.checkCancelled(cancelled);if(visitsFile!=null)try{JSONObject saved=new JSONObject();List<Map.Entry<String,Long>> entries=new ArrayList<>(visits.entrySet());Collections.sort(entries,(a,b)->Long.compare(b.getValue(),a.getValue()));for(int i=0;i<Math.min(entries.size(),10000);i++)saved.put(entries.get(i).getKey(),entries.get(i).getValue());synchronized(historyLock){if(canSave.allowed())CatalogRepository.writeJson(visitsFile,saved);}}catch(Exception e){log.write("Nie zapisano historii stron tego profilu.");}
        log.write("WWW: odczytano "+visited.size()+" stron; do 60 stron, głębokość 3, orientacyjny budżet 90s. To nie jest przeszukanie całego internetu.");return CatalogRepository.deduplicate(CatalogRepository.select(result,countries,query));
    }
    private static int enqueueHits(List<Hit> hits,String query,Set<String> countries,BroadcasterIndex broadcasters,ArrayDeque<Page> pages,Set<String> rootUrls,Set<String> playlistUrls){
        int useful=0;String key=Station.normalizeName(query);
        for(Hit hit:hits){if(SourcePrivacy.publicCredentials(hit.url))continue;String playlist=playlistUrl(hit.url);if(playlist!=null){playlistUrls.add(playlist);useful++;continue;}Station channel=identify(hit.title,hit.url,broadcasters);if(channel==null||!countries.isEmpty()&&!countries.contains(channel.country))continue;
            String canonical=Station.normalizeName(channel.name);if(!query.isEmpty()&&!M3u.matches(query,channel)&&!(canonical.length()>=2&&key.startsWith(canonical)))continue;
            useful++;if(rootUrls.size()<30&&rootUrls.add(hit.url))pages.add(new Page(hit.url,channel,0));
        }return useful;
    }
}
