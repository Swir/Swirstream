package com.swir.stream;

import android.content.Context;
import android.util.AtomicFile;
import android.util.JsonReader;
import android.util.JsonToken;
import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;
import java.io.File;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.StringReader;
import java.net.URI;
import java.net.InetAddress;
import java.net.URLEncoder;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.Arrays;
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
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.Future;
import java.util.concurrent.TimeUnit;

public final class CatalogRepository {
    public interface Progress {void update(String message);}
    public interface Ranking {void reorder(List<Station> stations);}
    static final long DAY=86_400_000L,MAX_AGE=7*DAY;
    static final String FREE_TV="https://raw.githubusercontent.com/Free-TV/IPTV/master/playlist.m3u8";
    static final String[] METADATA={"https://iptv-org.github.io/api/channels.json","https://raw.githubusercontent.com/iptv-org/api/gh-pages/channels.json"};
    // Bootstrap only; the service's authoritative all.api DNS list supplies live mirrors.
    static final String[] RADIO={"https://de1.api.radio-browser.info"};
    public static final class FoundPlaylist {
        public String name="",source="",text="";public long fetchedAt,modifiedAt;public int sourceCount,matchingCount;
        public String sha256="",originalSha256="";public int removedEntries;public boolean sanitized;
        public List<String> epgUrls(){return M3u.epgUrls(text);}
        public boolean stale(){return System.currentTimeMillis()-fetchedAt>MAX_AGE||modifiedAt>0&&System.currentTimeMillis()-modifiedAt>MAX_AGE;}
    }
    static final class RankingConfig {final String profile;final Ranking ranking;RankingConfig(String profile,Ranking ranking){this.profile=profile;this.ranking=ranking;}}
    private final File folder,feeds,found;
    private final PublicHttp.Fetcher fetcher;
    private final boolean discoverNetworkServers;
    private final Object lock=new Object();
    private final List<String> notes=new ArrayList<>();
    static final class PlaylistOrigin {
        final String cleanSha,originalSha;final int removed;final boolean sanitized;
        PlaylistOrigin(String cleanSha,String originalSha,int removed,boolean sanitized){this.cleanSha=cleanSha;this.originalSha=originalSha;this.removed=removed;this.sanitized=sanitized;}
    }
    private final Map<String,PlaylistOrigin> playlistOrigins=new HashMap<>();
    private final Map<String,Long> historyEpochs=new HashMap<>();
    private volatile List<Station> directory=new ArrayList<>();
    private volatile long dataAt;
    private volatile Set<String> savedCountries=new LinkedHashSet<>();
    private volatile boolean savedRadio,savedMixed;
    private volatile RankingConfig rankingConfig=new RankingConfig("default",null);
    public CatalogRepository(Context context){this(new File(context.getFilesDir(),"catalog"),PublicHttp.FETCHER,true);}
    CatalogRepository(File folder,PublicHttp.Fetcher fetcher){this(folder,fetcher,false);}
    private CatalogRepository(File folder,PublicHttp.Fetcher fetcher,boolean discoverNetworkServers){this.folder=folder;this.feeds=new File(folder,"feeds");this.found=new File(folder,"found-playlists");this.fetcher=fetcher;this.discoverNetworkServers=discoverNetworkServers;folder.mkdirs();feeds.mkdirs();found.mkdirs();}
    public void setDiscoveryRanking(String profileId,Ranking ranking){if(profileId==null||!profileId.matches("[a-zA-Z0-9_-]{1,80}"))throw new IllegalArgumentException("Nieprawidłowy profil.");rankingConfig=new RankingConfig(profileId,ranking);}
    public void setDiscoveryRanking(Ranking ranking){setDiscoveryRanking("default",ranking);}
    public void resetDiscoveryLearning(String profileId){if(profileId==null||!profileId.matches("[a-zA-Z0-9_-]{1,80}"))throw new IllegalArgumentException("Nieprawidłowy profil.");synchronized(lock){historyEpochs.put(profileId,historyEpoch(profileId)+1);new AtomicFile(new File(folder,"visits-"+profileId+".json")).delete();}}
    private long historyEpoch(String profile){synchronized(lock){Long value=historyEpochs.get(profile);return value==null?0:value;}}
    public List<String> diagnostics(){synchronized(lock){return new ArrayList<>(notes);}}
    void note(String message){synchronized(lock){if(notes.size()>=1000)notes.remove(0);notes.add(message);}}
    static void progress(Progress progress,String message){if(progress!=null)try{progress.update(message);}catch(RuntimeException ignored){}}
    public long dataAt(){return dataAt;}
    public boolean matchesScope(Set<String> countries,boolean radio){return (savedMixed||savedRadio==radio)&&savedCountries.equals(cleanCountries(countries));}
    public boolean matchesScope(Set<String> countries){return savedMixed&&savedCountries.equals(cleanCountries(countries));}
    public List<Station> load(){
        synchronized(lock){try{
            File file=new File(folder,"catalog.json");if(!file.exists()&&!new File(file.getPath()+".bak").exists())return new ArrayList<>();JSONObject json=readJson(file,45_000_000);
            if(json.optInt("schema")!=1)throw new IOException("Nieznany format katalogu.");
            List<Station> stations=new ArrayList<>();long oldest=Long.MAX_VALUE,now=System.currentTimeMillis();
            for(Station station:parseStations(json.optJSONArray("stations"))){long at=sourceDate(station);if(at>0&&at<=now+60000&&now-at<=MAX_AGE&&station.modifiedAt<=now+600000){stations.add(station);oldest=Math.min(oldest,at);}}
            if(stations.isEmpty()){dataAt=0;note("Zapis katalogu nie zawiera źródeł z ostatnich7dni. Wymaga odświeżenia.");return stations;}
            dataAt=oldest;savedRadio=json.optBoolean("radio");savedMixed=json.optBoolean("mixed");savedCountries=jsonCountries(json.optJSONArray("countries"));return stations;
        }catch(IOException|JSONException e){note("Nie wczytano uszkodzonego katalogu; zapisane profile i playlisty pozostają bez zmian.");return new ArrayList<>();}}
    }
    public void save(List<Station> stations){
        synchronized(lock){try{
            List<Station> valid=deduplicate(stations);long oldest=Long.MAX_VALUE;JSONArray rows=new JSONArray();
            for(Station station:valid){rows.put(station.toJson());long at=sourceDate(station);if(at>0)oldest=Math.min(oldest,at);}
            if(valid.isEmpty())return;if(oldest==Long.MAX_VALUE)oldest=dataAt;
            JSONObject json=new JSONObject();json.put("schema",1);json.put("dataAt",oldest);json.put("savedAt",System.currentTimeMillis());json.put("stations",rows);json.put("countries",strings(savedCountries));json.put("radio",savedRadio);json.put("mixed",savedMixed);
            writeJson(new File(folder,"catalog.json"),json);dataAt=oldest;
        }catch(IOException|JSONException e){note("Nie udało się zapisać katalogu: "+e.getMessage());}}
    }
    public List<Station> scan(Set<String> countries,boolean radio,Progress progress,AtomicBoolean cancelled){
        return scanInternal(countries,radio,progress,cancelled,true);
    }
    public List<Station> scanAll(Set<String> countries,Progress progress,AtomicBoolean cancelled){
        synchronized(lock){notes.clear();}
        List<Station> previouslyDownloaded=load();
        List<Station> results=scanInternal(countries,false,progress,cancelled,false);
        results.addAll(scanInternal(countries,true,progress,cancelled,false));
        if(cancelled.get()||Thread.currentThread().isInterrupted())throw new IllegalStateException("Anulowano wyszukiwanie.");
        boolean hasScopedResults=!results.isEmpty();
        // A country refresh updates that scope; it must not delete the other
        // countries or imported candidates already in the unexpired library.
        // Retained foreign rows alone do not prove that the requested scope was
        // collected. Keep its previous cache marker so a later startup can retry.
        results.addAll(previouslyDownloaded);results=deduplicate(results);if(hasScopedResults&&!results.isEmpty()){savedCountries=cleanCountries(countries);savedMixed=true;savedRadio=false;save(results);}return results;
    }
    private List<Station> scanInternal(Set<String> countries,boolean radio,Progress progress,AtomicBoolean cancelled,boolean persist){
        Set<String> selected=cleanCountries(countries);
        if(persist)synchronized(lock){notes.clear();}long deadline=System.currentTimeMillis()+90000;List<Station> collected=new ArrayList<>();List<Station> previous=load();
        try{
            PublicHttp.checkCancelled(cancelled);DirectoryIndex index=new DirectoryIndex(new ArrayList<>());
            if(!radio){progress(progress,"Odczytuję spis kanałów i krajów…");try{index=new DirectoryIndex(metadata(true,cancelled));}catch(IOException e){PublicHttp.checkCancelled(cancelled);note("Spis nadawców: "+e.getMessage()+". Korzystam z identyfikatorów playlist.");}}
            Set<String> regions=selected.isEmpty()||!radio&&selected.size()>12?Collections.singleton(""):selected;
            String[] radioMirrors=radio?radioMirrors(cancelled):new String[0];
            for(String country:regions){
                PublicHttp.checkCancelled(cancelled);if(System.currentTimeMillis()>deadline){note("Limit90s katalogów; zachowano odebrane wyniki.");break;}
                progress(progress,(radio?"Szukam radia: ":"Szukam telewizji: ")+(country.isEmpty()?"świat":country));
                try{
                    if(radio){String tail="/json/stations/search?hidebroken=true&limit=200&order=clickcount&reverse=true"+(country.isEmpty()?"":"&countrycode="+country);String[] urls=new String[radioMirrors.length];for(int i=0;i<urls.length;i++)urls[i]=radioMirrors[i]+tail;PublicHttp.Document doc=feed("radio:"+country,urls,true,cancelled);collected.addAll(parseRadio(doc,country));}
                    else {String path=country.isEmpty()?"index.m3u":"countries/"+WorldMarkets.tvCatalogCode(country)+".m3u";PublicHttp.Document doc=feed("tv:"+country,new String[]{"https://iptv-org.github.io/iptv/"+path,"https://raw.githubusercontent.com/iptv-org/iptv/gh-pages/"+path},true,cancelled);List<Station> stations=playlist(doc,index,country);collected.addAll(select(stations,selected,""));remember(doc,stations.size(),select(stations,selected,"").size());}
                }catch(IOException|JSONException e){PublicHttp.checkCancelled(cancelled);note((radio?"Radio ":"TV ")+country+": "+e.getMessage());}
            }
            if(!radio&&System.currentTimeMillis()<deadline){
                progress(progress,"Sprawdzam publiczną playlistę Free-TV…");try{PublicHttp.Document doc=feed("free-tv",new String[]{FREE_TV},true,cancelled);List<Station> all=playlist(doc,index,"");List<Station> matched=select(all,selected,"");collected.addAll(matched);remember(doc,all.size(),matched.size());}catch(IOException e){PublicHttp.checkCancelled(cancelled);note("Free-TV: "+e.getMessage());}
            }
            PublicHttp.checkCancelled(cancelled);collected=deduplicate(collected);
            if(collected.isEmpty()){note("Nie uzyskano nowych źródeł. Zachowano poprzedni, nadal aktualny zapis.");return selectKind(select(previous,selected,""),radio);}
            // Keep older source alternatives when one provider temporarily fails; their
            // original timestamps stay attached and never gain another retention period.
            for(Station station:selectKind(select(previous,selected,""),radio))if(sourceDate(station)>0&&System.currentTimeMillis()-sourceDate(station)<=MAX_AGE)collected.add(station);
            collected=deduplicate(collected);if(persist){savedCountries=selected;savedRadio=radio;savedMixed=false;save(collected);}note("Zebrano "+collected.size()+" adresów. Odtwarzanie potwierdza dopiero player.");return collected;
        }catch(IOException e){if(cancelled.get()||Thread.currentThread().isInterrupted())throw new IllegalStateException("Anulowano wyszukiwanie.",e);note(e.getMessage());return selectKind(select(previous,selected,""),radio);}
    }
    public List<Station> searchWeb(String query,Set<String> countries,Progress progress,AtomicBoolean cancelled){
        if(SourceLinkParser.importCandidate(query)){note("Adresy i dane dostawcy dodaj przez Wklej link. Nie wysłano ich do publicznego wyszukiwania.");return new ArrayList<>();}
        RankingConfig config=rankingConfig;long epoch=historyEpoch(config.profile);Set<String> selected=cleanCountries(countries);
        synchronized(lock){notes.clear();}
        try{
            PublicHttp.checkCancelled(cancelled);List<Station> channels;
            progress(progress,"Deep Search 1/3: nazwy kanałów i globalne playlisty…");
            try{channels=metadata(false,cancelled);}catch(IOException e){PublicHttp.checkCancelled(cancelled);channels=new ArrayList<>();note("Spis nadawców niedostępny; nadal przeszukuję playlisty z własnymi identyfikatorami i krajami.");}
            DirectoryIndex index=new DirectoryIndex(channels);List<Station> result=new ArrayList<>();
            // Search the worldwide files even when a country catalogue omits an
            // international feed. The country filter still applies to each result.
            String[][] providers={{"tv:global-deep","https://iptv-org.github.io/iptv/index.m3u","https://raw.githubusercontent.com/iptv-org/iptv/gh-pages/index.m3u"},{"free-tv",FREE_TV}};
            List<String[]> deepProviders=new ArrayList<>(Arrays.asList(providers));
            deepProviders.addAll(RegionalPlaylists.seeds(selected));
            for(String[] provider:deepProviders){PublicHttp.checkCancelled(cancelled);try{String[] urls=new String[provider.length-1];System.arraycopy(provider,1,urls,0,urls.length);PublicHttp.Document doc=feed(provider[0],urls,false,cancelled);List<Station> all=playlist(doc,index,"");List<Station> matches=select(all,selected,query);result.addAll(matches);remember(doc,all.size(),matches.size());note("Deep Search / "+provider[0]+": przeanalizowano "+all.size()+" adresów; pasuje "+matches.size()+".");}catch(IOException e){PublicHttp.checkCancelled(cancelled);note("Deep Search / "+provider[0]+": "+e.getMessage());}}
            PublicHttp.checkCancelled(cancelled);
            if(!channels.isEmpty())try{PublicHttp.Document doc=feed("streams",new String[]{"https://iptv-org.github.io/api/streams.json","https://raw.githubusercontent.com/iptv-org/api/gh-pages/streams.json"},false,cancelled);List<Station> all=StreamIndex.read(doc,index);List<Station> matches=select(all,selected,query);result.addAll(matches);note("Deep Search / indeks transmisji: "+all.size()+" rozpoznanych adresów; pasuje "+matches.size()+".");}catch(IOException e){PublicHttp.checkCancelled(cancelled);note("Indeks transmisji: "+e.getMessage());}
            progress(progress,"Deep Search 2/3: różne nazwy i strony nadawców…");
            WebCrawler crawler=new WebCrawler(fetcher,this::note,new File(folder,"visits-"+config.profile+".json"),config.ranking,(doc,all,matching)->remember(doc,all.size(),matching),()->historyEpoch(config.profile)==epoch,lock);
            result.addAll(crawler.search(query,selected,channels,progress,cancelled));result=deduplicate(result);
            if(config.ranking!=null)config.ranking.reorder(result);
            progress(progress,"Deep Search 3/3: sprawdzam transmisje znalezionych źródeł…");
            new SourceProbe(fetcher,this::note).check(result,progress,cancelled);
            PublicHttp.checkCancelled(cancelled);int ready=0;for(Station station:result)if(station.freshManifest())ready++;
            note("Deep Search: "+result.size()+" adresów, "+ready+" aktualnych manifestów live. Obraz potwierdza dopiero odtwarzacz.");
            if(result.isEmpty())note("Nie znaleziono publicznego źródła pasującego do nazwy i krajów. Możesz dołączyć własną playlistę.");return result;
        }catch(IOException e){if(cancelled.get()||Thread.currentThread().isInterrupted())throw new IllegalStateException("Anulowano wyszukiwanie.",e);note("Wyszukiwanie WWW: "+e.getMessage());return new ArrayList<>();}
    }
    public List<Station> importM3u(String text,String source){if(text.length()>12_000_000)throw new IllegalArgumentException("Playlista przekracza12MB.");hydrateDirectory();List<Station> parsed=M3u.parse(text,source);new DirectoryIndex(directory).enrich(parsed,"");return parsed;}
    public String exportM3u(List<Station> stations){return M3u.export(stations);}
    public List<FoundPlaylist> playlists(){
        synchronized(lock){List<FoundPlaylist> result=new ArrayList<>();File[] files=found.listFiles((dir,name)->name.endsWith(".json"));if(files==null)return result;List<File> ordered=new ArrayList<>();Collections.addAll(ordered,files);Collections.sort(ordered,(a,b)->Long.compare(b.lastModified(),a.lastModified()));long total=0;
            for(File file:ordered){if(result.size()>=20||total+file.length()>25_000_000)break;try{JSONObject json=readJson(file,15_000_000);FoundPlaylist value=new FoundPlaylist();value.source=json.optString("source");value.text=json.optString("text");value.name=json.optString("name");value.fetchedAt=json.optLong("fetchedAt");value.modifiedAt=json.optLong("modifiedAt");value.sourceCount=json.optInt("sourceCount");value.matchingCount=json.optInt("matchingCount");value.sha256=json.optString("sha256");value.originalSha256=json.optString("originalSha256",value.sha256);value.removedEntries=json.optInt("removedEntries");value.sanitized=json.optBoolean("sanitized");
                    if(SourcePrivacy.publicCredentials(value.source)||SourcePrivacy.publicCredentials(value.text)){new AtomicFile(file).delete();throw new IOException("Pominięto zapis z danymi dostępu.");}
                    if(json.optInt("schema")!=1||!PublicHttp.allowedDocument(value.source)||!value.sha256.equals(Station.sha256(value.text))||!value.text.trim().replace("\uFEFF","").startsWith("#EXTM3U")||M3u.isHls(value.text)||value.fetchedAt>System.currentTimeMillis()+60000)throw new IOException("Nieprawidłowy zapis playlisty.");result.add(value);total+=file.length();
                }catch(IOException|JSONException e){note("Pominięto uszkodzony zapis playlisty.");}}
            return result;}
    }
    String refreshFoundPlaylist(FoundPlaylist previous, Set<String> countries, AtomicBoolean cancelled) throws IOException {
        if (!PublicHttp.allowedDocument(previous.source) || SourcePrivacy.publicCredentials(previous.source))
            throw new IOException("Nieprawidłowy adres publicznej playlisty.");
        PublicHttp.Document doc = fetcher.fetch(previous.source, cancelled);
        PublicHttp.checkCancelled(cancelled);
        if (!usable(doc,MAX_AGE) || SourcePrivacy.publicCredentials(doc.url) ||
                doc.modifiedAt > 0 && doc.modifiedAt < previous.modifiedAt)
            throw new IOException("Serwer nie udostępnił aktualnej publicznej playlisty.");
        List<Station> stations = playlist(doc, new DirectoryIndex(directory), "");
        if (stations.isEmpty()) throw new IOException("Odpowiedź nie zawiera kanałów.");
        Set<String> before = new HashSet<>(), after = new HashSet<>();
        for (Station station : M3u.parse(previous.text,previous.source)) before.add(station.url);
        for (Station station : stations) after.add(station.url);
        int kept = 0;
        for (String url : after) if (before.contains(url)) kept++;
        synchronized(lock) {
            PublicHttp.checkCancelled(cancelled);
            File oldFile = new File(found, Station.sha256(previous.source) + ".json");
            try {
                JSONObject saved = readJson(oldFile,15_000_000);
                if (!previous.sha256.equals(saved.optString("sha256")) || saved.optLong("fetchedAt") > doc.fetchedAt)
                    throw new IOException("Lista zmieniła się w trakcie pobierania. Otwórz jej najnowszy zapis.");
            } catch (JSONException invalid) { throw new IOException("Nie można odczytać zapisu playlisty.", invalid); }
            if (!remember(doc, stations.size(), select(stations,countries,"").size()))
                throw new IOException("Nie udało się zapisać nowej playlisty.");
            if (!doc.url.equals(previous.source)) new AtomicFile(oldFile).delete();
        }
        return "Odświeżono playlistę. Dodane adresy: " + (after.size()-kept) + ", usunięte: " + (before.size()-kept) + ".";
    }
    boolean remember(PublicHttp.Document doc,int count,int matching){
        if(count==0||M3u.isHls(doc.text))return false;
        synchronized(lock){try{
            SourcePrivacy.CleanPlaylist clean=SourcePrivacy.sanitizePublicPlaylist(doc.url,doc.text);
            String cleanSha=Station.sha256(clean.text);PlaylistOrigin origin=playlistOrigins.get(doc.url);
            if(clean.sanitized||origin==null||!origin.cleanSha.equals(cleanSha))origin=new PlaylistOrigin(cleanSha,clean.originalSha256,clean.removedEntries,clean.sanitized);
            count=M3u.parse(clean.text,doc.url).size();if(count==0)return false;
            JSONObject json=new JSONObject();json.put("schema",1);json.put("source",doc.url);json.put("name",new URI(doc.url).getHost()+" / "+new File(new URI(doc.url).getPath()).getName());json.put("text",clean.text);json.put("fetchedAt",doc.fetchedAt);json.put("modifiedAt",doc.modifiedAt);json.put("sourceCount",count);json.put("matchingCount",Math.min(matching,count));json.put("sha256",cleanSha);json.put("originalSha256",origin.originalSha);json.put("removedEntries",origin.removed);json.put("sanitized",origin.sanitized);writeJson(new File(found,Station.sha256(doc.url)+".json"),json);prune(found,20);
            return true;
        }catch(Exception e){note("Nie zapisano publicznej playlisty.");return false;}}
    }
    List<Station> metadata(boolean refresh,AtomicBoolean cancelled)throws IOException{
        if(!refresh&&!directory.isEmpty()&&System.currentTimeMillis()-directory.get(0).fetchedAt<6*60*60*1000L&&System.currentTimeMillis()-sourceDate(directory.get(0))<=MAX_AGE)return directory;
        PublicHttp.Document doc=feed("channels",METADATA,refresh,cancelled);directory=parseDirectory(doc,true);return directory;
    }
    private void hydrateDirectory(){if(!directory.isEmpty())return;try{JSONObject cached=readJson(new File(feeds,Station.sha256("channels")+".json"),45_000_000);String text=cached.optString("text");if(!cached.optString("sha256").equals(Station.sha256(text)))return;PublicHttp.Document doc=new PublicHttp.Document(cached.optString("url"),text,cached.optString("type"),cached.optLong("fetchedAt"),cached.optLong("modifiedAt"));if(usable(doc,MAX_AGE))directory=parseDirectory(doc,true);}catch(Exception ignored){}}
    static String readString(JsonReader reader)throws IOException{if(reader.peek()==JsonToken.STRING)return reader.nextString();reader.skipValue();return "";}
    static List<Station> parseDirectory(PublicHttp.Document doc,boolean retainAll)throws IOException{
        List<Station> rows=new ArrayList<>();Map<String,String> successors=new HashMap<>();
        // The production directory has >30k entries. Stream fields instead of
        // allocating a full JSONObject tree on memory-limited Android devices.
        try(JsonReader reader=new JsonReader(new StringReader(doc.text))){reader.setLenient(false);reader.beginArray();while(reader.hasNext()){
                if(reader.peek()!=JsonToken.BEGIN_OBJECT){reader.skipValue();continue;}Station s=new Station();boolean adult=false;String replacedBy="";List<String> aliases=new ArrayList<>();reader.beginObject();
                while(reader.hasNext()){String key=reader.nextName();switch(key){case "id":s.id=s.tvgId=readString(reader);break;case "name":s.name=readString(reader);break;case "country":s.country=WorldMarkets.canonical(readString(reader));break;
                    case "is_nsfw":if(reader.peek()==JsonToken.BOOLEAN)adult=reader.nextBoolean();else reader.skipValue();break;
                    case "replaced_by":replacedBy=readString(reader);break;
                    case "website":if(retainAll){String site=readString(reader);if(PublicHttp.allowedDocument(site))s.url=s.homepage=site;}else reader.skipValue();break;
                    case "categories":if(retainAll&&reader.peek()==JsonToken.BEGIN_ARRAY){StringBuilder group=new StringBuilder();reader.beginArray();while(reader.hasNext()){String value=readString(reader);if(!value.isEmpty()){if(group.length()>0)group.append(';');group.append(value);}}reader.endArray();s.group=group.toString();}else reader.skipValue();break;
                    case "alt_names":if(retainAll&&reader.peek()==JsonToken.BEGIN_ARRAY){reader.beginArray();while(reader.hasNext()){String value=readString(reader);if(!value.isEmpty()&&aliases.size()<30)aliases.add(value);}reader.endArray();}else reader.skipValue();break;
                    default:reader.skipValue();}}
                reader.endObject();if(!adult&&!s.id.isEmpty()&&!s.name.isEmpty()&&s.country.length()==2&&s.country.charAt(0)>='A'&&s.country.charAt(0)<='Z'&&s.country.charAt(1)>='A'&&s.country.charAt(1)<='Z'){s.aliases=aliases.toArray(new String[0]);s.origin=doc.url;s.fetchedAt=doc.fetchedAt;s.modifiedAt=doc.modifiedAt;if(retainAll&&rows.size()<50000||!retainAll&&rows.size()<10){rows.add(s);if(retainAll&&!replacedBy.isEmpty())successors.put(s.id,replacedBy);}}}
            reader.endArray();if(reader.peek()!=JsonToken.END_DOCUMENT)throw new IOException("Dane po końcu spisu kanałów.");
        }catch(IllegalStateException e){throw new IOException("Niezgodny schemat spisu kanałów.",e);}
        if(rows.size()<10)throw new IOException("Niepełny spis nadawców.");
        if(retainAll&&!successors.isEmpty()){Map<String,Station> ids=new HashMap<>();for(Station s:rows)ids.put(s.id,s);for(Station predecessor:rows){String next=successors.get(predecessor.id);Set<String> visited=new HashSet<>();visited.add(predecessor.id);Station target=null;while(next!=null&&visited.size()<=8&&visited.add(next)){target=ids.get(next);if(target==null||!target.country.equals(predecessor.country)){target=null;break;}if(!successors.containsKey(next))break;next=successors.get(next);target=null;}if(target!=null){LinkedHashSet<String> aliases=new LinkedHashSet<>();Collections.addAll(aliases,target.aliases);aliases.add(predecessor.name);aliases.add(M3u.identifierName(predecessor.id));for(String old:predecessor.aliases)if(aliases.size()<30)aliases.add(old);target.aliases=aliases.toArray(new String[0]);}}}
        return rows;
    }
    PublicHttp.Document feed(String key,String[] urls,boolean refresh,AtomicBoolean cancelled)throws IOException{
        File file=new File(feeds,Station.sha256(key)+".json");PublicHttp.Document cached=null;
        try{JSONObject json=readJson(file,45_000_000);if(json.optInt("schema")==1&&json.optString("sha256").equals(Station.sha256(json.optString("text"))))cached=new PublicHttp.Document(json.optString("url"),json.optString("text"),json.optString("type"),json.optLong("fetchedAt"),json.optLong("modifiedAt"));if(cached!=null){synchronized(lock){playlistOrigins.put(cached.url,new PlaylistOrigin(Station.sha256(cached.text),json.optString("originalSha256",Station.sha256(cached.text)),json.optInt("removedEntries"),json.optBoolean("sanitized")));}}}catch(IOException|JSONException ignored){}
        if(cached!=null&&(SourcePrivacy.publicCredentials(cached.url)||(cached.text.trim().replace("\uFEFF","").startsWith("#EXTM3U")&&SourcePrivacy.publicCredentials(cached.text)))){cached=null;new AtomicFile(file).delete();}
        if(!refresh&&usable(cached,6*60*60*1000L)){note(key+": aktualny zapis lokalny; data źródła "+dateLabel(cached));return cached;}
        List<String> errors=new ArrayList<>();
        boolean certificateRejected=false;
        for(String url:urls){PublicHttp.checkCancelled(cancelled);try{if(SourcePrivacy.publicCredentials(url))throw new IOException("Publiczny adres zawiera dane dostępu.");PublicHttp.Document doc=fetcher.fetch(url,cancelled);if(doc.text.trim().replace("\uFEFF","").startsWith("#EXTM3U")){SourcePrivacy.CleanPlaylist clean=SourcePrivacy.sanitizePublicPlaylist(doc.url,doc.text);synchronized(lock){playlistOrigins.put(doc.url,new PlaylistOrigin(Station.sha256(clean.text),clean.originalSha256,clean.removedEntries,clean.sanitized));}doc=new PublicHttp.Document(doc.url,clean.text,doc.contentType,doc.fetchedAt,doc.modifiedAt);}if(!usable(doc,MAX_AGE))throw new IOException("Data źródła starsza niż7dni lub z przyszłości.");validateFeedBody(key,doc.text);PublicHttp.checkCancelled(cancelled);
                try{JSONObject json=new JSONObject();json.put("schema",1);json.put("url",doc.url);json.put("text",doc.text);json.put("type",doc.contentType);json.put("fetchedAt",doc.fetchedAt);json.put("modifiedAt",doc.modifiedAt);json.put("sha256",Station.sha256(doc.text));synchronized(lock){PlaylistOrigin origin=playlistOrigins.get(doc.url);if(origin!=null&&origin.cleanSha.equals(Station.sha256(doc.text))){json.put("originalSha256",origin.originalSha);json.put("removedEntries",origin.removed);json.put("sanitized",origin.sanitized);}}writeJson(file,json);prune(feeds,64);}catch(JSONException|IOException e){note("Nie zapisano pamięci źródła: "+key);}
                note(key+": pobrano; "+dateLabel(doc)+(url.equals(urls[0])?"":"; serwer zapasowy"));return doc;
            }catch(IOException e){PublicHttp.checkCancelled(cancelled);errors.add(e.getMessage());certificateRejected|=certificateFailure(e);}}
        if(usable(cached,MAX_AGE)){note(key+": TRYB AWARYJNY — zachowano datę wcześniejszych danych; "+dateLabel(cached));return cached;}
        if(certificateRejected)note(CERTIFICATE_DIAGNOSTIC);
        throw new IOException("Brak aktualnego źródła: "+errors);
    }
    private static final String CERTIFICATE_DIAGNOSTIC="TLS: nie udało się potwierdzić certyfikatu katalogu.";
    boolean catalogueCertificateRejected(){return diagnostics().contains(CERTIFICATE_DIAGNOSTIC);}
    static boolean certificateFailure(Throwable error){
        for(Throwable cause=error;cause!=null;cause=cause.getCause()){
            if(cause instanceof java.security.cert.CertificateException || cause instanceof java.security.cert.CertPathValidatorException
                    || cause instanceof javax.net.ssl.SSLPeerUnverifiedException)return true;
        }
        return false;
    }
    static void validateFeedBody(String key,String body)throws IOException{
        try{if(key.equals("channels")){parseDirectory(new PublicHttp.Document("https://iptv-org.github.io/api/channels.json",body,"application/json",System.currentTimeMillis(),0),false);}else if(key.equals("streams")){StreamIndex.read(new PublicHttp.Document("https://iptv-org.github.io/api/streams.json",body,"application/json",System.currentTimeMillis(),0),null);}else if(key.startsWith("radio:")){JSONArray array=new JSONArray(body);if(array.length()>0){JSONObject first=array.optJSONObject(0);if(first==null||!first.has("name")||!first.has("url"))throw new IOException("Nieprawidłowy schemat radia.");}}else if(!body.trim().replace("\uFEFF","").startsWith("#EXTM3U")||M3u.isHls(body))throw new IOException("To nie jest playlista kanałów.");}catch(JSONException e){throw new IOException("Niezgodny schemat danych.",e);}
    }
    private String[] radioMirrors(AtomicBoolean cancelled)throws IOException{
        if(!discoverNetworkServers)return RADIO;PublicHttp.checkCancelled(cancelled);List<String> servers=new ArrayList<>();File cache=new File(folder,"radio-servers.json");
        try{JSONObject saved=readJson(cache,16000);long at=saved.optLong("at");if(at>0&&at<=System.currentTimeMillis()+60000&&System.currentTimeMillis()-at<6*60*60*1000L){JSONArray rows=saved.optJSONArray("servers");if(rows!=null)for(int i=0;i<rows.length();i++){String value=rows.optString(i,"");if(validRadioServer(value)&&!servers.contains(value))servers.add(value);}}}catch(Exception ignored){}
        if(servers.isEmpty()){
            ExecutorService dns=Executors.newSingleThreadExecutor(r->{Thread t=new Thread(r,"radio-server-discovery");t.setDaemon(true);return t;});
            Future<List<String>> pending=dns.submit(()->{List<String> names=new ArrayList<>();for(InetAddress address:InetAddress.getAllByName("all.api.radio-browser.info")){if(!PublicHttp.publicAddress(address))continue;String name="https://"+address.getCanonicalHostName().toLowerCase(Locale.ROOT);if(validRadioServer(name)&&!names.contains(name)&&names.size()<6)names.add(name);}return names;});
            try{servers.addAll(pending.get(2500,TimeUnit.MILLISECONDS));}catch(Exception e){pending.cancel(true);note("Lista serwerów radia chwilowo niedostępna; używam serwera startowego.");}finally{dns.shutdownNow();}
            PublicHttp.checkCancelled(cancelled);if(!servers.isEmpty())try{JSONObject saved=new JSONObject();saved.put("at",System.currentTimeMillis());saved.put("servers",new JSONArray(servers));writeJson(cache,saved);}catch(Exception e){note("Nie zapisano listy serwerów radia.");}
        }
        for(String fallback:RADIO)if(!servers.contains(fallback))servers.add(fallback);Collections.shuffle(servers);note("Radio Browser: "+servers.size()+" serwerów z listy usługi / zapisu startowego.");return servers.toArray(new String[0]);
    }
    static boolean validRadioServer(String value){return value.matches("https://[a-z0-9-]+\\.api\\.radio-browser\\.info")&&PublicHttp.allowedDocument(value);}
    static boolean usable(PublicHttp.Document doc,long maximum){if(doc==null||!PublicHttp.allowedDocument(doc.url)||doc.fetchedAt<=0)return false;long now=System.currentTimeMillis();return doc.fetchedAt<=now+60000&&now-doc.fetchedAt<=maximum&&(doc.modifiedAt==0||doc.modifiedAt<=now+600000&&now-doc.modifiedAt<=MAX_AGE);}
    static String dateLabel(PublicHttp.Document doc){return doc.modifiedAt==0?"serwer nie podaje daty aktualizacji":"Last-Modified "+new java.text.SimpleDateFormat("yyyy-MM-dd HH:mm",Locale.ROOT).format(new java.util.Date(doc.modifiedAt));}
    static long sourceDate(Station station){return station.modifiedAt>0&&station.modifiedAt<station.fetchedAt?station.modifiedAt:station.fetchedAt;}
    static List<Station> playlist(PublicHttp.Document doc,DirectoryIndex index,String country)throws IOException{SourcePrivacy.CleanPlaylist clean=SourcePrivacy.sanitizePublicPlaylist(doc.url,doc.text);doc=new PublicHttp.Document(doc.url,clean.text,doc.contentType,doc.fetchedAt,doc.modifiedAt);if(M3u.isHls(doc.text)||!doc.text.trim().replace("\uFEFF","").startsWith("#EXTM3U"))throw new IOException("Nieprawidłowa playlista kanałów.");List<Station> rows=M3u.parse(doc.text,doc.url);for(Station s:rows){s.country=RegionalPlaylists.country(doc.url,s.group,s.country);s.fetchedAt=doc.fetchedAt;s.modifiedAt=doc.modifiedAt;}index.enrich(rows,country);return rows;}
    static List<Station> parseRadio(PublicHttp.Document doc,String country)throws JSONException{
        JSONArray data=new JSONArray(doc.text);List<Station> stations=new ArrayList<>();for(int i=0;i<data.length();i++){JSONObject row=data.optJSONObject(i);if(row==null)continue;Station s=new Station();s.name=Station.string(row,"name").trim();s.url=Station.string(row,"url_resolved");if(!PublicHttp.allowedStream(s.url))s.url=Station.string(row,"url");if(!PublicHttp.allowedStream(s.url))continue;s.kind="radio";s.id=Station.string(row,"stationuuid");s.country=Station.string(row,"countrycode").toUpperCase(Locale.ROOT);if(!country.isEmpty()&&!s.country.equals(country))continue;s.group=Station.string(row,"tags");s.homepage=Station.string(row,"homepage");s.logo=Station.string(row,"favicon");s.origin=doc.url;s.fetchedAt=doc.fetchedAt;s.modifiedAt=doc.modifiedAt;if(!s.name.isEmpty())stations.add(s);}return deduplicate(stations);
    }
    static List<Station> select(List<Station> stations,Set<String> countries,String query){List<Station> results=new ArrayList<>();Set<String> exact=new HashSet<>();String key=Station.normalizeName(query);if(!key.isEmpty())for(Station station:stations)if((countries.isEmpty()||countries.contains(station.country))&&exactName(key,station))exact.add(station.key());for(Station s:stations)if((countries.isEmpty()||countries.contains(s.country))&&(query.isEmpty()||exact.isEmpty()?M3u.matches(query,s):exact.contains(s.key())))results.add(s);return results;}
    static boolean exactName(String key,Station s){for(String name:M3u.searchNames(s))if(Station.normalizeName(name).equals(key))return true;return false;}
    static List<Station> selectKind(List<Station> list,boolean radio){List<Station> out=new ArrayList<>();for(Station s:list)if(s.kind.equals(radio?"radio":"tv"))out.add(s);return out;}
    static List<Station> deduplicate(List<Station> rows){LinkedHashMap<String,Station> result=new LinkedHashMap<>();for(Station s:rows)if(s!=null&&!s.name.isEmpty()&&PublicHttp.allowedStream(s.url)){String key=s.sourceKey()+"|"+s.country;Station old=result.get(key);if(old==null)result.put(key,s);else mergeRequest(old,s);if(result.size()>=30000)break;}return new ArrayList<>(result.values());}
    private static void mergeRequest(Station old,Station next){
        if(next.freshManifest()&&(!old.freshManifest()||next.manifestCheckedAt>old.manifestCheckedAt)){
            // A successful check belongs to the complete HTTP request, not just its URL.
            old.userAgent=next.userAgent;old.referrer=next.referrer;old.liveManifest=true;old.manifestCheckedAt=next.manifestCheckedAt;
        }else if(!old.freshManifest()){
            boolean changed=false;if(old.userAgent.isEmpty()&&!next.userAgent.isEmpty()){old.userAgent=next.userAgent;changed=true;}if(old.referrer.isEmpty()&&!next.referrer.isEmpty()){old.referrer=next.referrer;changed=true;}
            if(changed){old.liveManifest=false;old.manifestCheckedAt=0;}
            if(next.manifestCheckedAt>old.manifestCheckedAt&&old.userAgent.equals(next.userAgent)&&old.referrer.equals(next.referrer)){old.liveManifest=next.liveManifest;old.manifestCheckedAt=next.manifestCheckedAt;}
        }
    }
    static Set<String> cleanCountries(Set<String> countries){Set<String> result=new LinkedHashSet<>();if(countries!=null)for(String code:countries)if(code!=null&&code.toUpperCase(Locale.ROOT).matches("[A-Z]{2}"))result.add(WorldMarkets.canonical(code.toUpperCase(Locale.ROOT)));return result;}
    static Set<String> jsonCountries(JSONArray array){Set<String> result=new LinkedHashSet<>();if(array!=null)for(int i=0;i<array.length();i++)result.add(array.optString(i,""));return cleanCountries(result);}
    static JSONArray strings(Set<String> values){JSONArray array=new JSONArray();for(String value:values)array.put(value);return array;}
    static String join(JSONArray array){StringBuilder text=new StringBuilder();if(array!=null)for(int i=0;i<array.length();i++){if(text.length()>0)text.append(';');text.append(array.optString(i,""));}return text.toString();}
    static List<Station> parseStations(JSONArray rows){List<Station> result=new ArrayList<>();if(rows!=null)for(int i=0;i<rows.length()&&i<30000;i++){JSONObject row=rows.optJSONObject(i);if(row!=null){Station s=Station.fromJson(row);if(s.fetchedAt<=System.currentTimeMillis()+60000)result.add(s);}}return deduplicate(result);}
    static JSONObject readJson(File file,int limit)throws IOException,JSONException{File backup=new File(file.getPath()+".bak");if(!file.exists()&&!backup.exists()||file.length()>limit||backup.length()>limit)throw new IOException("Brak poprawnego pliku danych.");return new JSONObject(new String(new AtomicFile(file).readFully(),StandardCharsets.UTF_8));}
    static void writeJson(File file,JSONObject value)throws IOException{AtomicFile atomic=new AtomicFile(file);FileOutputStream stream=null;try{stream=atomic.startWrite();stream.write(value.toString().getBytes(StandardCharsets.UTF_8));atomic.finishWrite(stream);}catch(IOException e){if(stream!=null)atomic.failWrite(stream);throw e;}}
    static void prune(File folder,int keep){File[] files=folder.listFiles((dir,name)->name.endsWith(".json"));if(files==null)return;List<File> sorted=new ArrayList<>();Collections.addAll(sorted,files);Collections.sort(sorted,(a,b)->Long.compare(b.lastModified(),a.lastModified()));for(int i=keep;i<sorted.size();i++)sorted.get(i).delete();}
    static final class DirectoryIndex {
        final Map<String,Station> ids=new HashMap<>();final Map<String,List<Station>> names=new HashMap<>();
        DirectoryIndex(List<Station> rows){for(Station station:rows){if(!station.id.isEmpty()&&!ids.containsKey(station.id))ids.put(station.id,station);add(station.name,station);for(String alias:station.aliases)add(alias,station);}}
        void add(String name,Station station){String key=Station.normalizeName(name);if(key.isEmpty())return;List<Station> candidates=names.get(key);if(candidates==null){candidates=new ArrayList<>();names.put(key,candidates);}for(Station old:candidates)if(old.key().equals(station.key()))return;candidates.add(station);}
        void enrich(List<Station> rows,String country){for(Station s:rows){Station meta=ids.get(s.id);if(meta==null){List<Station> candidates=names.get(Station.normalizeName(s.name));if(candidates!=null){Station unique=null;int count=0;for(Station candidate:candidates)if(s.country.isEmpty()||s.country.equals(candidate.country)){unique=candidate;count++;}if(count==1)meta=unique;}}
                if(meta!=null){if(s.id.isEmpty()||ids.containsKey(s.id))s.id=meta.id;if(s.tvgId.isEmpty())s.tvgId=meta.tvgId;s.country=meta.country;s.homepage=meta.homepage;if(s.group.isEmpty())s.group=meta.group;LinkedHashSet<String> allNames=new LinkedHashSet<>();allNames.add(meta.name);allNames.add(s.name);Collections.addAll(allNames,meta.aliases);s.aliases=allNames.toArray(new String[0]);}else if(s.country.isEmpty())s.country=country;}}
    }
}
