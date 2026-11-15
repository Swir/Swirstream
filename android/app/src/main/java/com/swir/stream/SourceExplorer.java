package com.swir.stream;

import android.content.Context;
import org.xmlpull.v1.XmlPullParser;
import org.xmlpull.v1.XmlPullParserFactory;
import java.io.IOException;
import java.io.StringReader;
import java.net.URI;
import java.net.URLEncoder;
import java.util.ArrayDeque;
import java.util.ArrayList;
import java.util.Collections;
import java.util.HashSet;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.Set;
import java.util.concurrent.atomic.AtomicBoolean;

/** Public pages only. Candidates are neither accounts nor playback confirmations. */
final class SourceExplorer {
    static final int MAX_QUERIES=4,MAX_PAGES=12,MAX_DEPTH=2,MAX_RESULTS=100;
    static final long BUDGET_MILLIS=60000;
    static final class Scan {
        final List<SourceCandidate> candidates;final List<String> notes;final int pages,requests,failed;
        Scan(List<SourceCandidate> candidates,List<String> notes,int pages,int requests,int failed){this.candidates=candidates;this.notes=notes;this.pages=pages;this.requests=requests;this.failed=failed;}
    }
    private static final class Page {final String url,title,evidence;final int depth;Page(String url,String title,String evidence,int depth){this.url=url;this.title=title;this.evidence=evidence;this.depth=depth;}}
    private final PublicHttp.Fetcher fetcher;
    SourceExplorer(Context context){this(new SourceExplorerHttp(context));}
    SourceExplorer(PublicHttp.Fetcher fetcher){this.fetcher=fetcher;}
    Scan analyze(String address,CatalogRepository.Progress progress,AtomicBoolean cancel)throws IOException{
        String url=SourceExplorerHttp.allowed(address);ArrayDeque<Page> pending=new ArrayDeque<>();pending.add(new Page(url,host(url),url,0));return crawl(pending,new ArrayList<>(),0,0,System.currentTimeMillis()+BUDGET_MILLIS,progress,cancel);
    }
    Scan search(String query,Set<String> countries,CatalogRepository.Progress progress,AtomicBoolean cancel)throws IOException{
        String clean=query==null?"":query.trim();if(clean.length()<2||clean.length()>160||SourceLinkParser.importCandidate(clean)||SourceExplorerRules.privateText(clean)||credentialIndex(clean))throw new IOException("Wpisz nazwę nadawcy, kanału lub temat, bez adresów, MAC i danych kont.");
        String area="";if(countries!=null&&!countries.isEmpty()){List<String> codes=new ArrayList<>();for(String country:countries)if(country.matches("[A-Z]{2}")&&codes.size()<12)codes.add(country);Collections.sort(codes);area=" "+String.join(" ",codes);}
        String[] queries={clean+area+" official live TV",clean+area+" public M3U playlist",clean+area+" Ministra Stalker portal provider",clean+area+" television watch live"};
        ArrayDeque<Page> pending=new ArrayDeque<>();List<String> notes=new ArrayList<>();int requests=0,failed=0,successful=0;long deadline=System.currentTimeMillis()+BUDGET_MILLIS;boolean bing=true,duck=true;
        for(int i=0;i<MAX_QUERIES&&i<queries.length&&System.currentTimeMillis()<deadline-20000;i++){
            PublicHttp.checkCancelled(cancel);if(progress!=null)progress.update("Eksplorator: publiczne wyniki WWW "+(i+1)+"/"+MAX_QUERIES+"…");List<WebCrawler.Hit> hits=new ArrayList<>();
            if(bing)try{requests++;PublicHttp.Document result=fetcher.fetch("https://www.bing.com/search?format=rss&q="+URLEncoder.encode(queries[i],"UTF-8"),cancel);hits.addAll(rssHits(result.text));successful++;notes.add("Bing: "+hits.size()+" publicznych wyników.");}catch(IOException error){PublicHttp.checkCancelled(cancel);failed++;bing=false;notes.add("Bing chwilowo niedostępny lub zmienił format odpowiedzi.");}
            if(duck&&(hits.isEmpty()||!bing))try{requests++;PublicHttp.Document result=fetcher.fetch("https://html.duckduckgo.com/html/?q="+URLEncoder.encode(queries[i],"UTF-8"),cancel);for(WebCrawler.Hit hit:duckHits(result.text))hits.add(hit);successful++;notes.add("DuckDuckGo: odczytano publiczne wyniki.");}catch(IOException error){PublicHttp.checkCancelled(cancel);failed++;duck=false;notes.add("DuckDuckGo niedostępny, zmienił format lub wymaga weryfikacji. Nie omijamy blokady.");}
            for(WebCrawler.Hit hit:hits)try{String url=SourceExplorerHttp.allowed(hit.url);if(!unsafeHit(hit.title,url)&&pending.size()<40)pending.add(new Page(url,hit.title,url,0));}catch(IOException ignored){}
            if(!bing&&!duck)break;
        }
        if(successful==0)throw new IOException("Wyszukiwarki są chwilowo niedostępne. Poprzednie wyniki zachowano; możesz przeanalizować publiczny adres samodzielnie.");
        return crawl(pending,notes,requests,failed,deadline,progress,cancel);
    }
    private Scan crawl(ArrayDeque<Page> pending,List<String> notes,int requests,int failed,long deadline,CatalogRepository.Progress progress,AtomicBoolean cancel)throws IOException{
        Map<String,SourceCandidate> results=new LinkedHashMap<>();Set<String> visited=new HashSet<>();int pages=0,successful=0;
        while(!pending.isEmpty()&&pages<MAX_PAGES&&results.size()<MAX_RESULTS&&System.currentTimeMillis()<deadline){
            PublicHttp.checkCancelled(cancel);Page page=pending.removeFirst();String url;try{url=SourceExplorerHttp.allowed(page.url);}catch(IOException blocked){continue;}if(!visited.add(url))continue;
            String extension=SourceExplorerRules.extension(url);if(extension.matches("mp4|mp3|aac")){put(results,new SourceCandidate(url,page.title,page.evidence,SourceExplorerRules.Kind.DirectStream,"Link do materiału — obrazu/dźwięku jeszcze nie sprawdzono",System.currentTimeMillis(),0,0,0,false));successful++;continue;}
            pages++;requests++;if(progress!=null)progress.update("Eksplorator: analizuję publiczną stronę "+pages+"/"+MAX_PAGES+" • "+host(url));
            try{
                PublicHttp.Document document=fetcher.fetch(url,cancel);PublicHttp.checkCancelled(cancel);String actual=SourceExplorerHttp.allowed(document.url);String body=document.text.trim().replace("\uFEFF","");String title=SourceExplorerRules.title(document.text,page.title);long found=System.currentTimeMillis();
                if(document.modifiedAt<0||document.modifiedAt>System.currentTimeMillis()+600000||document.fetchedAt<=0||document.fetchedAt>System.currentTimeMillis()+60000)throw new IOException("Nieprawidłowa data dokumentu.");
                if(body.startsWith("#EXTM3U")||body.startsWith("#EXTINF:")){
                    if(M3u.isHls(body)){validateManifest(body,false);successful++;boolean drm=SourceExplorerRules.protectedManifest(body);put(results,new SourceCandidate(actual,title,page.evidence,SourceExplorerRules.Kind.DirectStream,drm?"Manifest wskazuje DRM — potrzebna własna licencja":"Odczytano HLS — odtwarzanie niepotwierdzone",found,document.fetchedAt,document.modifiedAt,0,drm));}
                    else{if(!CatalogRepository.usable(document,CatalogRepository.MAX_AGE))throw new IOException("Playlista ma dane starsze niż 7 dni lub datę z przyszłości.");SourcePrivacy.CleanPlaylist clean=SourcePrivacy.sanitizePublicPlaylist(actual,body);List<Station> stations=M3u.parse(clean.text,actual);int count=stations.size();if(count>0){successful++;put(results,new SourceCandidate(actual,title,page.evidence,SourceExplorerRules.Kind.PublicPlaylist,"Odczytano playlistę; "+count+" adresów"+(clean.removedEntries>0?"; pominięto wpisy z danymi kont":""),found,document.fetchedAt,document.modifiedAt,count,false));}else notes.add("Playlista bez publicznych kanałów; pominięto wynik.");}
                    continue;
                }
                if(SourceExplorerRules.extension(actual).equals("mpd")||document.contentType.contains("dash+xml")||body.matches("(?s)^(?:<\\?xml[^>]*>\\s*)?<MPD\\b.*")){validateManifest(body,true);successful++;boolean drm=SourceExplorerRules.protectedManifest(body);put(results,new SourceCandidate(actual,title,page.evidence,SourceExplorerRules.Kind.DirectStream,drm?"Manifest wskazuje DRM — potrzebna własna licencja":"Odczytano DASH — odtwarzanie niepotwierdzone",found,document.fetchedAt,document.modifiedAt,0,drm));continue;}
                if(!document.contentType.contains("html")&&!body.toLowerCase(Locale.ROOT).contains("<html")&&!body.toLowerCase(Locale.ROOT).contains("<!doctype html")){notes.add("Dokument nie jest rozpoznaną stroną, playlistą ani manifestem.");continue;}
                if(SourceExplorerRules.privateText(visibleText(document.text))||credentialIndex(visibleText(document.text)))throw new IOException("Pominięto stronę zawierającą dane kont.");successful++;boolean portal=SourceExplorerRules.portalFingerprint(actual,document.text);put(results,new SourceCandidate(actual,title,page.evidence,portal?SourceExplorerRules.Kind.PortalCandidate:SourceExplorerRules.Kind.ProviderPage,portal?"Rozpoznano stronę portalu — wymaga własnego zarejestrowanego MAC":"Strona / informacje — nie potwierdzono transmisji",found,document.fetchedAt,document.modifiedAt,0,false));
                if(portal)continue;
                if(page.depth<MAX_DEPTH){List<String> links=SourceExplorerRules.links(actual,document.text);for(String link:links){if(visited.contains(link)||SourceExplorerRules.preferredLink(link)||pending.size()>=40)continue;boolean relevant=link.toLowerCase(Locale.ROOT).matches(".*(?:live|stream|player|television|telewiz|na-zywo|na_zywo|watch|portal).*");if(host(link).equals(host(actual))&&relevant)pending.addLast(new Page(link,title,actual,page.depth+1));}
                    for(int i=links.size()-1;i>=0;i--){String link=links.get(i);if(!visited.contains(link)&&SourceExplorerRules.preferredLink(link)){if(pending.size()>=40)pending.removeLast();pending.addFirst(new Page(link,title,actual,page.depth+1));}}
                }
            }catch(IOException error){PublicHttp.checkCancelled(cancel);failed++;notes.add("Strona "+host(url)+": "+safeError(error));put(results,new SourceCandidate(url,page.title,page.evidence,SourceExplorerRules.Kind.ProviderPage,"Nie zweryfikowano strony — brak dostępu lub błąd pobierania",System.currentTimeMillis(),0,0,0,false));}
        }
        PublicHttp.checkCancelled(cancel);if(successful==0)throw new IOException("Nie udało się przeanalizować publicznych stron. Poprzednie wyniki pozostały zapisane.");notes.add("Sprawdzono "+pages+" stron; błędy: "+failed+". Limit: "+MAX_PAGES+" stron, głębokość "+MAX_DEPTH+", orientacyjny budżet 60 s. Wynik nie potwierdza odtwarzania ani uprawnień.");List<SourceCandidate> ordered=new ArrayList<>(results.values());ordered.sort((a,b)->{int checked=Boolean.compare(a.checkedAt==0,b.checkedAt==0);if(checked!=0)return checked;int kind=Integer.compare(priority(a.kind),priority(b.kind));return kind!=0?kind:a.title.compareToIgnoreCase(b.title);});return new Scan(ordered,notes,pages,requests,failed);
    }
    private static int priority(SourceExplorerRules.Kind kind){return kind==SourceExplorerRules.Kind.PublicPlaylist?0:kind==SourceExplorerRules.Kind.DirectStream?1:kind==SourceExplorerRules.Kind.PortalCandidate?2:3;}
    private static void put(Map<String,SourceCandidate> results,SourceCandidate candidate){SourceCandidate old=results.get(candidate.id);if(old==null||candidate.checkedAt>old.checkedAt)results.put(candidate.id,candidate);}
    private static String host(String url){try{return new URI(url).getHost();}catch(Exception error){return "strona";}}
    private static String safeError(IOException error){String value=error.getMessage();return value!=null&&!value.contains("://")&&!SourceExplorerRules.privateText(value)&&value.length()<220?value:"Nie można potwierdzić dostępności.";}
    static boolean credentialIndex(String text){String value=SourceExplorerRules.entities(text==null?"":text).toLowerCase(Locale.ROOT);return value.matches("(?s).*(?:\\bmac\\b|\\bpassword\\b|\\bhasło\\b|\\bhaslo\\b|\\blogin\\b|xtream.code).*")&&value.matches("(?s).*(?:free|working|fresh|active|daily|leak|dump|darmow|aktualn|lista|list|kody|codes).*");}
    private static boolean unsafeHit(String title,String url){return SourceExplorerRules.privateText(title)||SourceExplorerRules.privateText(url)||credentialIndex(title+" "+url);}
    static String visibleText(String html){return SourceExplorerRules.entities(html.replaceAll("(?s)<!--.*?-->","").replaceAll("(?is)<(?:script|style|template)\\b[^>]*>.*?</(?:script|style|template)\\s*>"," ").replaceAll("<[^>]*>"," "));}
    static List<WebCrawler.Hit> duckHits(String html)throws IOException{WebCrawler.duckResults(html);List<WebCrawler.Hit> hits=new ArrayList<>();java.util.regex.Matcher blocks=java.util.regex.Pattern.compile("(?is)<div[^>]*class=[\"'][^\"']*result(?: results_links| web-result)[^\"']*[\"'][^>]*>(.*?)(?=<div[^>]*class=[\"'][^\"']*result(?: results_links| web-result)|$)").matcher(html);while(blocks.find()){String block=blocks.group(1);if(SourceExplorerRules.privateText(visibleText(block))||credentialIndex(visibleText(block)))continue;for(WebCrawler.Hit hit:WebCrawler.duckResults(block))if(!unsafeHit(hit.title,hit.url))hits.add(hit);}return hits;}
    static void validateManifest(String body,boolean dash)throws IOException{
        if(SourceExplorerRules.privateText(body)||SourcePrivacy.publicCredentials(body))throw new IOException("Manifest zawiera dane kont; pominięto źródło.");
        if(!dash)return;if(body.length()>SourceExplorerHttp.MAX_BYTES||body.matches("(?is).*<!\\s*(?:DOCTYPE|ENTITY).*"))throw new IOException("Nieprawidłowy manifest DASH.");
        try{XmlPullParser parser=XmlPullParserFactory.newInstance().newPullParser();parser.setFeature(XmlPullParser.FEATURE_PROCESS_NAMESPACES,true);parser.setInput(new StringReader(body));int event,depth=0;boolean root=false,closed=false;while((event=parser.next())!=XmlPullParser.END_DOCUMENT){if(event==XmlPullParser.START_TAG){if(depth++==0){if(root||!"MPD".equals(parser.getName()))throw new IOException();root=true;}}else if(event==XmlPullParser.END_TAG){if(--depth==0)closed=true;}}if(!root||!closed||depth!=0)throw new IOException();}catch(Exception error){throw new IOException("Nieprawidłowy manifest DASH.");}
    }
    static List<WebCrawler.Hit> rssHits(String xml)throws IOException{
        if(xml.length()>2_000_000||xml.contains("<!DOCTYPE")||xml.contains("<!ENTITY"))throw new IOException("Nieprawidłowy wynik wyszukiwarki.");List<WebCrawler.Hit> hits=new ArrayList<>();
        try{XmlPullParser parser=XmlPullParserFactory.newInstance().newPullParser();parser.setInput(new StringReader(xml));boolean rss=false,item=false;String title="",url="",description="";int event;while((event=parser.next())!=XmlPullParser.END_DOCUMENT){if(event==XmlPullParser.START_TAG){String name=parser.getName();if(name.equals("rss"))rss=true;if(name.equals("item")){item=true;title=url=description="";}else if(item&&name.equals("title"))title=parser.nextText();else if(item&&name.equals("link"))url=parser.nextText();else if(item&&name.equals("description"))description=parser.nextText();}else if(event==XmlPullParser.END_TAG&&parser.getName().equals("item")){if(hits.size()<10&&!unsafeHit(title+" "+description,url)&&SourceExplorerRules.allowedPage(url))hits.add(new WebCrawler.Hit(SourceExplorerRules.entities(title),url));item=false;}}if(!rss)throw new IOException("Wyszukiwarka nie zwróciła RSS.");return hits;}catch(IOException error){throw error;}catch(Exception error){throw new IOException("Nieprawidłowy wynik wyszukiwarki.");}
    }
}
