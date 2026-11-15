package com.swir.stream;

import android.content.Context;
import org.json.JSONArray;
import org.json.JSONObject;
import java.io.File;
import java.io.IOException;
import java.net.InetAddress;
import java.nio.charset.StandardCharsets;
import java.text.SimpleDateFormat;
import java.util.ArrayList;
import java.util.Collections;
import java.util.Date;
import java.util.HashMap;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.UUID;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.atomic.AtomicBoolean;

/** Private, profile-bound sources. Only opaque references leave this repository. */
public final class ProviderRepository {
    public static final class SourceInfo {
        public final String id,name,type,status; public final int count; public final long updatedAt;
        SourceInfo(String id,String name,String type,int count,long updatedAt,String status){this.id=id;this.name=name;this.type=type;this.count=count;this.updatedAt=updatedAt;this.status=status;}
    }
    private static final Map<String,Object> LOCKS=new ConcurrentHashMap<>();
    private final ProviderVault vault;private final String profile;private final Object lock;
    private final ProviderHttp.Fetcher fetcher;private final ProviderHttp.Resolver resolver;
    private volatile Map<String,EpgRepository.Feed> schedules=Collections.emptyMap();
    public ProviderRepository(Context context,String profileId){this(new File(context.getNoBackupFilesDir(),"providers"),profileId,ProviderHttp.network(context),InetAddress::getAllByName);}
    ProviderRepository(File root,String profileId,ProviderHttp.Fetcher fetcher,ProviderHttp.Resolver resolver){
        if(profileId==null||!profileId.matches("default|[a-f0-9]{32}"))throw new IllegalArgumentException("Nieprawidłowy profil.");
        profile=profileId;File dir=new File(root,profile);vault=new ProviderVault(dir,profile);lock=LOCKS.computeIfAbsent(dir.getAbsolutePath(),k->new Object());this.fetcher=fetcher;this.resolver=resolver;
    }
    public String profileId(){return profile;}
    public List<SourceInfo> listSources(){
        List<SourceInfo> out=new ArrayList<>();synchronized(lock){for(String id:vault.ids())try{out.add(info(read(id)));}catch(Exception e){out.add(new SourceInfo(id,"Źródło niedostępne","unknown",0,0,"Nie można odczytać chronionego zapisu. Plik zachowano."));}}return out;
    }
    public List<Station> stations(String profileId){return profile.equals(profileId)?stations():Collections.emptyList();}
    /** Call on a worker. EPG is loaded here so now()/next() never decrypt on the UI thread. */
    public List<Station> stations(){
        List<Station> out=new ArrayList<>();Map<String,EpgRepository.Feed> epg=new HashMap<>();
        synchronized(lock){for(String id:vault.ids())try{
            JSONObject source=read(id);JSONArray entries=source.getJSONArray("entries");for(int i=0;i<entries.length();i++)out.add(reference(source,entries.getJSONObject(i)));
            EpgRepository.Feed feed=readFeed(source.optJSONObject("epg"));if(feed!=null)epg.put(id,feed);
        }catch(Exception ignored){/* A corrupt source cannot erase the remaining library. */}}
        schedules=Collections.unmodifiableMap(epg);return out;
    }
    public SourceInfo addM3u(String name,String url,String epgUrl,AtomicBoolean cancel)throws IOException{return addM3u(name,url,epgUrl,false,cancel);}
    SourceInfo addPublicM3u(String name,String url,String epgUrl,AtomicBoolean cancel)throws IOException{return addM3u(name,url,epgUrl,true,cancel);}
    private SourceInfo addM3u(String name,String url,String epgUrl,boolean publicOnly,AtomicBoolean cancel)throws IOException{
        try{String address=ProviderHttp.checkedUri(url).toString();JSONObject duplicate=null;
            synchronized(lock){for(String id:vault.ids()){JSONObject existing;try{existing=read(id);}catch(IOException unreadable){continue;}if("m3u".equals(existing.optString("type"))&&address.equals(existing.getJSONObject("config").optString("url"))){duplicate=existing;break;}}}
            if(duplicate!=null){String id=duplicate.getString("id"),revision=duplicate.optString("revision");if(publicOnly&&!duplicate.getJSONObject("config").optBoolean("publicDiscovery"))throw new IOException("Ten adres jest już zapisany jako własna playlista. Otwórz ją w Playlisty URL; jej prywatne dane pozostały bez zmian.");if(epgUrl!=null&&!epgUrl.trim().isEmpty())duplicate.getJSONObject("config").put("epgUrl",optionalUrl(epgUrl));JSONObject refreshed=download(duplicate,cancel);PublicHttp.checkCancelled(cancel);synchronized(lock){if(!revision.equals(read(id).optString("revision")))throw new IOException("Źródło zmieniło się podczas pobierania. Odśwież je ponownie.");PublicHttp.checkCancelled(cancel);vault.write(id,refreshed);}return info(refreshed);}
            return add(name,"m3u",new JSONObject().put("url",address).put("epgUrl",optionalUrl(epgUrl)).put("publicDiscovery",publicOnly),cancel);
        }catch(IOException e){throw e;}catch(Exception e){throw failure();}
    }
    public SourceInfo addXtream(String name,String base,String user,String password,AtomicBoolean cancel)throws IOException{
        try{SourceLinkParser.Result parsed=SourceLinkParser.xtream(base,user,password);JSONObject config=new JSONObject().put("base",parsed.base).put("user",parsed.user).put("password",parsed.password),existing=null;
            synchronized(lock){for(String id:vault.ids()){JSONObject saved;try{saved=read(id);}catch(IOException unreadable){continue;}JSONObject old=saved.optJSONObject("config");if("xtream".equals(saved.optString("type"))&&old!=null&&ProviderAdapters.xtreamAccountIdentity(parsed.base).equals(ProviderAdapters.xtreamAccountIdentity(old.optString("base")))&&parsed.user.equals(old.optString("user"))){if(existing!=null)throw new IOException("Zapisano kilka kopii tego konta. Wybierz właściwe źródło w Xtream i użyj Edytuj dane źródła.");existing=saved;}}}
            if(existing==null)return add(name,"xtream",config,cancel);String id=existing.getString("id"),revision=existing.optString("revision");existing.put("config",config);JSONObject refreshed=download(existing,cancel);PublicHttp.checkCancelled(cancel);
            synchronized(lock){if(!revision.equals(read(id).optString("revision")))throw new IOException("Źródło zmieniło się podczas pobierania. Otwórz edycję ponownie.");PublicHttp.checkCancelled(cancel);vault.write(id,refreshed);}return info(refreshed);
        }catch(IOException e){throw e;}catch(Exception e){throw failure();}
    }
    public SourceInfo addDirect(String name,String url,String epg,AtomicBoolean cancel)throws IOException{
        try{String address=ProviderHttp.checkedUri(url).toString();JSONObject existing=null;synchronized(lock){for(String id:vault.ids()){JSONObject old=read(id);if("direct".equals(old.optString("type"))&&address.equals(old.getJSONObject("config").optString("url"))){if(existing!=null)throw new IOException("Wybierz zapisane źródło do edycji.");existing=old;}}}if(existing!=null)return update(existing.getString("id"),existing.getString("name"),"direct",address,"","","",epg,"","",Collections.emptyMap(),cancel);return add(name,"direct",new JSONObject().put("url",address).put("epgUrl",optionalUrl(epg)),cancel);}catch(IOException error){throw error;}catch(Exception error){throw failure();}
    }
    public SourceInfo addPortal(String name,String portal,String registeredMac,AtomicBoolean cancel)throws IOException{
        try{return add(name,"portal",new JSONObject().put("url",PortalDiscovery.input(portal)).put("mac",ProviderAdapters.mac(registeredMac)),cancel);}catch(IOException e){throw e;}catch(Exception e){throw failure();}
    }
    public SourceInfo addDrm(String name,String streamUrl,String scheme,String licenseUrl,Map<String,String> licenseHeaders,AtomicBoolean cancel)throws IOException{
        if(!"widevine".equalsIgnoreCase(scheme)&&!"clearkey".equalsIgnoreCase(scheme))throw new IOException("Wybierz Widevine lub ClearKey.");
        try{return add(name,"drm",new JSONObject().put("url",ProviderHttp.checkedUri(streamUrl).toString()).put("scheme",scheme.toLowerCase(Locale.ROOT)).put("license",ProviderHttp.checkedUri(licenseUrl).toString()).put("headers",new JSONObject(ProviderHttp.cleanHeaders(licenseHeaders))),cancel);}catch(IOException e){throw e;}catch(Exception e){throw failure();}
    }
    public SourceInfo addDrm(String name,String streamUrl,String scheme,String licenseUrl,AtomicBoolean cancel)throws IOException{return addDrm(name,streamUrl,scheme,licenseUrl,Collections.emptyMap(),cancel);}
    /** Transient form data: never place in Bundle, preferences, logs or an export. */
    JSONObject editConfiguration(String id)throws IOException{synchronized(lock){JSONObject source=read(id);try{return new JSONObject().put("id",id).put("name",source.getString("name")).put("type",source.getString("type")).put("config",new JSONObject(source.getJSONObject("config").toString()));}catch(Exception error){throw failure();}}}
    SourceInfo update(String id,String name,String type,String url,String user,String password,String mac,String epg,String scheme,String license,Map<String,String> headers,AtomicBoolean cancel)throws IOException{
        if(name==null||name.trim().isEmpty()||name.length()>120||name.contains("://"))throw new IOException("Podaj krótką nazwę źródła, bez adresu i danych logowania.");
        try{JSONObject config=new JSONObject();
            if("m3u".equals(type)||"direct".equals(type))config.put("url",ProviderHttp.checkedUri(url).toString()).put("epgUrl",optionalUrl(epg));
            else if("xtream".equals(type)){SourceLinkParser.Result parsed=SourceLinkParser.xtream(url,user,password);config.put("base",parsed.base).put("user",parsed.user).put("password",parsed.password);}
            else if("portal".equals(type))config.put("url",PortalDiscovery.input(url)).put("mac",ProviderAdapters.mac(mac));
            else if("drm".equals(type)){if(!"widevine".equals(scheme)&&!"clearkey".equals(scheme))throw new IOException("Wybierz Widevine lub ClearKey.");config.put("url",ProviderHttp.checkedUri(url).toString()).put("scheme",scheme).put("license",ProviderHttp.checkedUri(license).toString()).put("headers",new JSONObject(ProviderHttp.cleanHeaders(headers)));}
            else throw new IOException("Nieznany rodzaj źródła.");
            JSONObject old;synchronized(lock){old=read(id);}if(!type.equals(old.getString("type")))throw new IOException("Rodzaj zapisanego źródła nie może się zmienić.");String revision=old.optString("revision");if("m3u".equals(type)||"direct".equals(type))config.put("publicDiscovery",old.getJSONObject("config").optBoolean("publicDiscovery"));boolean renameOnly=sameConfig(old.getJSONObject("config"),config);old.put("name",label(name,"Moje źródło")).put("config",config);JSONObject refreshed;if(renameOnly){refreshed=old;refreshed.put("revision",UUID.randomUUID().toString());}else refreshed=download(old,cancel);PublicHttp.checkCancelled(cancel);
            synchronized(lock){if(!revision.equals(read(id).optString("revision")))throw new IOException("Źródło zmieniło się podczas pobierania. Otwórz edycję ponownie.");PublicHttp.checkCancelled(cancel);vault.write(id,refreshed);}return info(refreshed);
        }catch(IOException error){throw error;}catch(Exception error){throw failure();}
    }
    private static boolean sameConfig(JSONObject left,JSONObject right){if(left.length()!=right.length())return false;java.util.Iterator<String> keys=left.keys();while(keys.hasNext()){String key=keys.next();Object a=left.opt(key),b=right.opt(key);if(a instanceof JSONObject&&b instanceof JSONObject){if(!sameConfig((JSONObject)a,(JSONObject)b))return false;}else if(!java.util.Objects.equals(a,b))return false;}return true;}
    private SourceInfo add(String name,String type,JSONObject config,AtomicBoolean cancel)throws IOException{
        if(name==null||name.trim().isEmpty()||name.length()>120||name.contains("://"))throw new IOException("Podaj krótką nazwę źródła, bez adresu i danych logowania.");
        try{
            JSONObject source=new JSONObject().put("schema",1).put("id",UUID.randomUUID().toString().replace("-","")).put("name",label(name,"Moje źródło")).put("type",type).put("config",config).put("revision",UUID.randomUUID().toString());
            JSONObject refreshed=download(source,cancel);PublicHttp.checkCancelled(cancel);
            synchronized(lock){if(vault.ids().length>=20)throw new IOException("Można zapisać do 20 źródeł w profilu.");PublicHttp.checkCancelled(cancel);vault.write(refreshed.getString("id"),refreshed);}return info(refreshed);
        }catch(IOException e){throw e;}catch(Exception e){throw failure();}
    }
    public SourceInfo refresh(String id,AtomicBoolean cancel)throws IOException{
        JSONObject old;synchronized(lock){old=read(id);}String revision=old.optString("revision");JSONObject refreshed=download(old,cancel);PublicHttp.checkCancelled(cancel);
        synchronized(lock){JSONObject current=read(id);if(!revision.equals(current.optString("revision")))throw new IOException("Źródło zmieniło się podczas pobierania. Odśwież je ponownie.");PublicHttp.checkCancelled(cancel);vault.write(id,refreshed);}return info(refreshed);
    }
    public void remove(String id)throws IOException{synchronized(lock){vault.remove(id);Map<String,EpgRepository.Feed> copy=new HashMap<>(schedules);copy.remove(id);schedules=Collections.unmodifiableMap(copy);}}
    public Station resolve(Station reference,AtomicBoolean cancel)throws IOException{
        validateReference(reference);JSONObject source,entry;synchronized(lock){source=read(reference.providerId);entry=find(source,reference.providerChannelId);}PublicHttp.checkCancelled(cancel);
        try{
            Station station=Station.fromJson(entry.getJSONObject("station"));
            if("portal".equals(source.getString("type"))){ProviderAdapters.PortalSession session=ProviderAdapters.portalSession(source.getJSONObject("config"),fetcher,cancel);String original=PortalDiscovery.originalCommand(entry);if(!original.isEmpty())try{ProviderHttp.validateAddress(ProviderHttp.checkedUri(original),resolver);}catch(IOException unavailable){PublicHttp.checkCancelled(cancel);original="";}station.url=original.isEmpty()?ProviderAdapters.portalLink(session,entry.getString("command"),fetcher,cancel):original;station.userAgent=PortalDiscovery.USER_AGENT;if(ProviderHttp.sameOrigin(session.endpoint,station.url))station.requestHeaders.putAll(session.headers);}
            ProviderHttp.validateAddress(ProviderHttp.checkedUri(station.url),resolver);
            if("drm".equals(source.getString("type"))){JSONObject config=source.getJSONObject("config");station.drmScheme=config.getString("scheme");station.drmLicenseUrl=config.getString("license");ProviderHttp.validateAddress(ProviderHttp.checkedUri(station.drmLicenseUrl),resolver);station.drmHeaders.putAll(headers(config.optJSONObject("headers")));}
            if(entry.optJSONObject("headers")!=null)station.requestHeaders.putAll(headers(entry.getJSONObject("headers")));
            Station safe=reference(source,entry);
            station.id=safe.id;station.name=safe.name;station.tvgId=safe.tvgId;station.feed=safe.feed;station.origin=safe.origin;station.group=safe.group;station.country=safe.country;station.logo=station.homepage="";station.aliases=safe.aliases;
            station.providerId=reference.providerId;station.providerChannelId=reference.providerChannelId;station.providerProfileId=profile;PublicHttp.checkCancelled(cancel);synchronized(lock){if(!source.optString("revision").equals(read(reference.providerId).optString("revision")))throw new IOException("Źródło zmieniło się podczas przygotowania transmisji. Wybierz kanał ponownie.");}return station;
        }catch(IOException e){throw e;}catch(Exception e){throw new IOException("Nie udało się przygotować transmisji dostawcy.");}
    }
    private void validateReference(Station station)throws IOException{
        if(station==null||!profile.equals(station.providerProfileId)||!station.providerId.matches("[a-f0-9]{32}")||!station.providerChannelId.matches("[a-f0-9]{64}")||!station.url.equals("swir://provider/"+profile+"/"+station.providerId+"/"+station.providerChannelId))throw new IOException("Kanał nie należy do bieżącego profilu.");
    }
    private JSONObject download(JSONObject previous,AtomicBoolean cancel)throws IOException{
        PublicHttp.checkCancelled(cancel);
        try{
            JSONObject source=new JSONObject(previous.toString());String type=source.getString("type");JSONObject config=source.getJSONObject("config");ProviderAdapters.Result result;
            if("m3u".equals(type))result=ProviderAdapters.m3u(config,fetcher,cancel);
            else if("xtream".equals(type))result=ProviderAdapters.xtream(config,fetcher,cancel);
            else if("portal".equals(type))result=ProviderAdapters.portal(config,fetcher,cancel);
            else if("direct".equals(type)){
                ProviderHttp.validateAddress(ProviderHttp.checkedUri(config.getString("url")),resolver);result=new ProviderAdapters.Result();Station s=new Station();s.name=source.getString("name");s.url=config.getString("url");s.fetchedAt=System.currentTimeMillis();if(SourceExplorerRules.extension(s.url).matches("mp3|aac"))s.kind="radio";result.entries.put(ProviderAdapters.entry("single",s,"",Collections.emptyMap()));result.downloaded=System.currentTimeMillis();result.epgUrl=config.optString("epgUrl");
            }else if("drm".equals(type)){
                ProviderHttp.validateAddress(ProviderHttp.checkedUri(config.getString("url")),resolver);ProviderHttp.validateAddress(ProviderHttp.checkedUri(config.getString("license")),resolver);
                result=new ProviderAdapters.Result();Station s=new Station();s.name=source.getString("name");s.url=config.getString("url");s.fetchedAt=System.currentTimeMillis();result.entries.put(ProviderAdapters.entry("single",s,"",Collections.emptyMap()));result.downloaded=System.currentTimeMillis();
            }else throw new IOException("Nieznany rodzaj źródła.");
            if(result.entries.length()==0)throw new IOException("Źródło nie zwróciło kanałów. Poprzednia lista pozostaje zapisana.");
            if("m3u".equals(type))reconcilePlaylistIds(previous.optJSONArray("entries"),result.entries);
            source.put("entries",result.entries).put("updatedAt",result.downloaded).put("body",result.body).put("revision",UUID.randomUUID().toString()).put("status",type.equals("drm")?"Wymaga uprawnień i zgodnego DRM urządzenia.":type.equals("direct")?"Zapisano adres; odtwarzanie niepotwierdzone.":"Lista pobrana; odtwarzanie zależy od dostawcy.");
            if("portal".equals(type))source.put("portalMode",result.portalMode).put("portalEndpoint",result.portalEndpoint).put("status",PortalDiscovery.DIRECT.equals(result.portalMode)?PortalDiscovery.DIRECT_NOTICE:PortalDiscovery.FULL_NOTICE);
            if("portal".equals(type)){source.put("portalTruncated",result.portalTruncated).put("portalRowsRead",result.portalRowsRead).put("portalRawBytes",result.portalRawBytes);if(result.portalTruncated)source.put("status",source.optString("status")+" Lista ograniczona do pierwszych "+result.entries.length()+" unikalnych kanałów (limit liczby lub rozmiaru). Pozostałe pozycje nie zostały zapisane.");}
            if(!result.epgUrl.isEmpty())try{
                ProviderHttp.Document document=fetcher.fetch(result.epgUrl,Collections.emptyMap(),cancel);EpgRepository.Feed feed=EpgRepository.parse(document.bytes,"","provider:"+source.getString("id"),document.downloadedAt,document.modifiedAt,cancel);source.put("epg",writeFeed(feed));source.put("status","Lista i aktualna ramówka pobrane.");
            }catch(Exception e){PublicHttp.checkCancelled(cancel);source.put("status",source.has("epg")?"Lista pobrana. EPG: zachowano poprzedni zapis.":"Lista pobrana. EPG chwilowo niedostępne.");}
            if(result.omitted>0)source.put("status",source.optString("status")+" Pominięto "+result.omitted+" wpisów z danymi kont.");
            PublicHttp.checkCancelled(cancel);return source;
        }catch(IOException e){throw e;}catch(Exception e){throw failure();}
    }
    /** Preserve old favorites only where old/new identity is unambiguous; never by row order. */
    private static void reconcilePlaylistIds(JSONArray previous,JSONArray current)throws Exception{
        if(previous==null)return;Map<String,List<JSONObject>> oldExact=new HashMap<>(),newExact=new HashMap<>(),oldPaths=new HashMap<>(),newPaths=new HashMap<>(),oldNames=new HashMap<>(),newNames=new HashMap<>();
        for(int pass=0;pass<2;pass++){JSONArray rows=pass==0?previous:current;Map<String,List<JSONObject>> exact=pass==0?oldExact:newExact,paths=pass==0?oldPaths:newPaths,names=pass==0?oldNames:newNames;for(int i=0;i<rows.length();i++){JSONObject entry=rows.getJSONObject(i);Station station=Station.fromJson(entry.getJSONObject("station"));exact.computeIfAbsent(ProviderAdapters.playlistExactIdentity(station),k->new ArrayList<>()).add(entry);paths.computeIfAbsent(ProviderAdapters.playlistIdentity(station),k->new ArrayList<>()).add(entry);names.computeIfAbsent(ProviderAdapters.playlistMetadata(station),k->new ArrayList<>()).add(entry);}}
        java.util.Set<String> used=new java.util.HashSet<>();java.util.Set<JSONObject> matched=java.util.Collections.newSetFromMap(new java.util.IdentityHashMap<>());
        for(int pass=0;pass<3;pass++){Map<String,List<JSONObject>> older=pass==0?oldExact:pass==1?oldPaths:oldNames,newer=pass==0?newExact:pass==1?newPaths:newNames;for(Map.Entry<String,List<JSONObject>> group:newer.entrySet()){List<JSONObject> old=older.get(group.getKey()),fresh=group.getValue();if(old==null||old.size()!=1||fresh.size()!=1)continue;JSONObject entry=fresh.get(0);String id=old.get(0).getString("id");if(!matched.contains(entry)&&used.add(id)){entry.put("id",id);matched.add(entry);}}}
        java.util.Set<String> previousIds=new java.util.HashSet<>();for(int i=0;i<previous.length();i++)previousIds.add(previous.getJSONObject(i).getString("id"));
        for(int i=0;i<current.length();i++){JSONObject entry=current.getJSONObject(i);if(matched.contains(entry))continue;String id=entry.getString("id");while(previousIds.contains(id)||used.contains(id))id=Station.sha256("m3u-new|"+id+"|"+UUID.randomUUID());entry.put("id",id);used.add(id);}
    }
    private JSONObject read(String id)throws IOException{
        JSONObject source=vault.read(id);if(source.optInt("schema")!=1||!id.equals(source.optString("id"))||source.optJSONArray("entries")==null||source.optJSONArray("entries").length()>30000)throw new IOException("Nieprawidłowy chroniony zapis źródła.");return source;
    }
    private static JSONObject find(JSONObject source,String id)throws IOException{JSONArray array=source.optJSONArray("entries");for(int i=0;i<array.length();i++){JSONObject entry=array.optJSONObject(i);if(entry!=null&&id.equals(entry.optString("id")))return entry;}throw new IOException("Kanał nie występuje już w zapisanej liście dostawcy.");}
    private static SourceInfo info(JSONObject source){return new SourceInfo(source.optString("id"),source.optString("name"),source.optString("type"),source.optJSONArray("entries")==null?0:source.optJSONArray("entries").length(),source.optLong("updatedAt"),source.optString("status"));}
    private Station reference(JSONObject source,JSONObject entry)throws Exception{
        Station real=Station.fromJson(entry.getJSONObject("station"));Station s=new Station();s.providerId=source.getString("id");s.providerChannelId=entry.getString("id");s.providerProfileId=profile;s.url="swir://provider/"+profile+"/"+s.providerId+"/"+s.providerChannelId;s.id=s.providerChannelId;
        s.name=label(real.name,"Kanał");s.group=label(real.group,"");s.country=real.country.matches("[A-Z]{2}")?real.country:"";s.kind=real.kind;s.tvgId=label(real.tvgId,"");s.feed=label(real.feed,"");s.origin=source.getString("name");s.fetchedAt=real.fetchedAt;s.modifiedAt=real.modifiedAt;return s;
    }
    static String label(String value,String fallback){if(value==null)return fallback;value=value.replaceAll("[\\p{Cntrl}]"," ").trim();if(value.isEmpty()||value.contains("://")||value.matches("(?i).*(?:username|password|token|access_token)\\s*=.*"))return fallback;return value.substring(0,Math.min(value.length(),200));}
    private static String optionalUrl(String value)throws IOException{return value==null||value.trim().isEmpty()?"":ProviderHttp.checkedUri(value).toString();}
    private static String credential(String value)throws IOException{if(value==null||value.isEmpty()||value.length()>2048||value.contains("\r")||value.contains("\n"))throw new IOException("Wpisz dane dostępu przekazane przez dostawcę.");return value;}
    static Map<String,String> headers(JSONObject json)throws IOException{Map<String,String> headers=new LinkedHashMap<>();if(json!=null){java.util.Iterator<String> keys=json.keys();while(keys.hasNext()){String key=keys.next();headers.put(key,json.optString(key));}}return ProviderHttp.cleanHeaders(headers);}
    private static IOException failure(){return new IOException("Nie udało się odczytać danych dostawcy. Poprzednia lista pozostaje zapisana.");}
    public String now(Station station){return slot(station,false);}
    public String next(Station station){return slot(station,true);}
    private String slot(Station station,boolean next){
        String empty=next?"Następnie: brak danych EPG":"Teraz: brak danych EPG";if(station==null||!profile.equals(station.providerProfileId))return empty;
        EpgRepository.Feed feed=schedules.get(station.providerId);long now=System.currentTimeMillis();if(!EpgRepository.valid(feed,now,48))return empty;EpgRepository.Channel channel=EpgRepository.match(feed,station);if(channel==null)return empty;
        EpgRepository.Programme current=null;int overlapping=0;for(EpgRepository.Programme p:channel.programmes)if(p.start<=now&&p.stop>now){current=p;overlapping++;}if(overlapping!=1)current=null;
        EpgRepository.Programme chosen=current;if(next){chosen=null;long after=current==null?now:current.stop;for(EpgRepository.Programme p:channel.programmes)if(p.start>=after&&p.start>now){chosen=p;break;}}
        if(chosen==null)return empty;SimpleDateFormat time=new SimpleDateFormat("HH:mm",Locale.getDefault());return next?"Następnie "+time.format(new Date(chosen.start))+"  "+chosen.title:"Teraz "+time.format(new Date(chosen.start))+"–"+time.format(new Date(chosen.stop))+"  "+chosen.title;
    }
    private static JSONObject writeFeed(EpgRepository.Feed feed)throws Exception{
        JSONArray channels=new JSONArray();for(EpgRepository.Channel c:feed.channels.values()){JSONArray programmes=new JSONArray();for(EpgRepository.Programme p:c.programmes)programmes.put(new JSONArray().put(p.start).put(p.stop).put(p.title));channels.put(new JSONObject().put("id",c.id).put("names",new JSONArray(c.names)).put("programmes",programmes));}
        return new JSONObject().put("downloaded",feed.downloaded).put("modified",feed.modified).put("channels",channels);
    }
    private static EpgRepository.Feed readFeed(JSONObject json){
        if(json==null)return null;try{EpgRepository.Feed feed=new EpgRepository.Feed();feed.country="";feed.source="private";feed.downloaded=json.getLong("downloaded");feed.modified=json.getLong("modified");JSONArray channels=json.getJSONArray("channels");if(channels.length()>5000)return null;int total=0;
            for(int i=0;i<channels.length();i++){JSONObject item=channels.getJSONObject(i);EpgRepository.Channel channel=new EpgRepository.Channel(item.getString("id"));JSONArray names=item.getJSONArray("names");for(int j=0;j<Math.min(names.length(),20);j++)channel.names.add(names.getString(j));JSONArray programmes=item.getJSONArray("programmes");if(programmes.length()>48||(total+=programmes.length())>25000)return null;for(int j=0;j<programmes.length();j++){JSONArray p=programmes.getJSONArray(j);channel.programmes.add(new EpgRepository.Programme(p.getLong(0),p.getLong(1),p.getString(2)));}feed.channels.put(channel.id,channel);}return EpgRepository.valid(feed,System.currentTimeMillis(),48)?feed:null;
        }catch(Exception e){return null;}
    }
}
