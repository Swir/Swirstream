package com.swir.stream;

import org.json.JSONArray;
import org.json.JSONObject;
import org.json.JSONTokener;
import java.io.IOException;
import java.net.URI;
import java.net.URLDecoder;
import java.net.URLEncoder;
import java.util.Collections;
import java.util.HashMap;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.concurrent.atomic.AtomicBoolean;

/** User-configured endpoints only: never discovers accounts, MACs or DRM keys. */
final class ProviderAdapters {
    static final class Result {final JSONArray entries=new JSONArray();String epgUrl="",body="",portalMode="",portalEndpoint="";long downloaded,portalRowsRead,portalRawBytes;int omitted;boolean portalTruncated;}
    static String encode(String value){try{return URLEncoder.encode(value,"UTF-8").replace("+","%20");}catch(Exception e){throw new IllegalStateException();}}
    static JSONObject entry(String stableId,Station s,String command,Map<String,String> headers)throws Exception{return new JSONObject().put("id",Station.sha256(stableId)).put("station",s.toJson()).put("command",command).put("headers",new JSONObject(headers));}
    static Result m3u(JSONObject config,ProviderHttp.Fetcher fetcher,AtomicBoolean cancel)throws Exception{
        boolean publicOnly=config.optBoolean("publicDiscovery");if(publicOnly&&(SourcePrivacy.publicCredentials(config.getString("url"))||SourcePrivacy.publicCredentials(config.optString("epgUrl"))))throw new IOException("Publiczna playlista nie może korzystać z danych kont. Własną listę dodaj ręcznie w Playlisty URL.");
        ProviderHttp.Document doc=fetcher.fetch(config.getString("url"),Collections.emptyMap(),cancel);String text=doc.text();int omitted=0;if(publicOnly){SourcePrivacy.CleanPlaylist clean=SourcePrivacy.sanitizePublicPlaylist(doc.url,text);text=clean.text;omitted=clean.removedEntries;}String beginning=text.trim().replace("\uFEFF","");if(beginning.startsWith("<")||!beginning.startsWith("#EXTM3U")&&!beginning.contains("#EXTINF:"))throw new IOException("Adres nie zwrócił playlisty M3U.");
        if(publicOnly&&M3u.isHls(text))throw new IOException("Adres zwrócił manifest pojedynczej transmisji zamiast listy kanałów. Poprzednia lista pozostała zapisana.");
        Result result=new Result();result.omitted=omitted;result.body=text;result.downloaded=doc.downloadedAt;
        if(M3u.isHls(text)){Station s=new Station();s.name="Transmisja HLS";s.url=doc.url;s.fetchedAt=doc.downloadedAt;s.modifiedAt=doc.modifiedAt;result.entries.put(entry("single",s,"",Collections.emptyMap()));return result;}
        StringBuilder normalized=new StringBuilder();Map<String,Map<String,String>> allHeaders=new HashMap<>();Map<String,String> current=new LinkedHashMap<>();
        for(String raw:text.split("\\r?\\n")){
            PublicHttp.checkCancelled(cancel);String line=raw.trim();
            if(line.regionMatches(true,0,"#EXTINF:",0,8))current=new LinkedHashMap<>();
            if(line.regionMatches(true,0,"#EXTVLCOPT:http-user-agent=",0,25))current.put("User-Agent",line.substring(line.indexOf('=')+1));
            if(line.regionMatches(true,0,"#EXTVLCOPT:http-referrer=",0,23))current.put("Referer",line.substring(line.indexOf('=')+1));
            if(!line.isEmpty()&&!line.startsWith("#")){
                String[] parts=line.split("\\|",2);try{
                    String url=ProviderHttp.checkedUri(new URI(doc.url).resolve(M3u.entities(parts[0].trim())).toString()).toString();
                    if(parts.length>1)for(String header:parts[1].split("&")){String[] pair=header.split("=",2);if(pair.length==2&&(pair[0].equalsIgnoreCase("User-Agent")||pair[0].equalsIgnoreCase("Referer")||pair[0].equalsIgnoreCase("Authorization")||pair[0].equalsIgnoreCase("Cookie")))current.put(pair[0],URLDecoder.decode(pair[1],"UTF-8"));}
                    allHeaders.put(url,ProviderHttp.cleanHeaders(current));normalized.append(url).append('\n');
                }catch(Exception ignored){normalized.append("# niewłaściwy adres\n");}current=new LinkedHashMap<>();
            }else normalized.append(raw).append('\n');
        }
        List<Station> stations=M3u.parse(normalized.toString(),doc.url);Map<String,Integer> occurrences=new HashMap<>();int number=0;
        for(Station station:stations){PublicHttp.checkCancelled(cancel);if(++number>30000)break;station.name=ProviderRepository.label(station.name,"Kanał "+number);station.origin="";station.homepage=station.logo="";station.fetchedAt=doc.downloadedAt;station.modifiedAt=doc.modifiedAt;
            String identity="m3u-v2|"+playlistExactIdentity(station);int occurrence=occurrences.containsKey(identity)?occurrences.get(identity)+1:0;occurrences.put(identity,occurrence);Map<String,String> headers=allHeaders.get(station.url);if(headers==null)headers=Collections.emptyMap();result.entries.put(entry(identity+"|"+occurrence,station,"",headers));}
        result.epgUrl=config.optString("epgUrl");if(result.epgUrl.isEmpty()){
            String header=beginning.split("\\r?\\n",2)[0];Map<String,String> attrs=M3u.attributes(header);outer:for(String key:new String[]{"x-tvg-url","url-tvg","tvg-url"})for(String value:M3u.attr(attrs,key).split(","))try{if(!value.trim().isEmpty()){result.epgUrl=ProviderHttp.checkedUri(new URI(doc.url).resolve(value.trim()).toString()).toString();break outer;}}catch(Exception ignored){}
        }return result;
    }
    static String playlistMetadata(Station station){return station.tvgId+"|"+station.feed+"|"+station.name+"|"+station.country;}
    static String playlistExactIdentity(Station station){return playlistMetadata(station)+"|"+station.url;}
    static String playlistIdentity(Station station){String address="";try{URI uri=new URI(station.url);address=uri.getScheme().toLowerCase(Locale.ROOT)+"://"+uri.getRawAuthority().toLowerCase(Locale.ROOT)+(uri.getRawPath()==null?"":uri.getRawPath());}catch(Exception ignored){}return playlistMetadata(station)+"|"+address;}
    static String xtreamBase(String value)throws IOException{
        URI uri=ProviderHttp.checkedUri(value);if(uri.getRawQuery()!=null)throw new IOException("Podaj adres serwera Xtream bez loginu i hasła w adresie.");String path=uri.getRawPath()==null?"":uri.getRawPath();path=path.replaceFirst("(?i)/(?:player_api|get|xmltv)\\.php/?$","").replaceFirst("/+$","");return uri.getScheme().toLowerCase(Locale.ROOT)+"://"+uri.getRawAuthority().toLowerCase(Locale.ROOT)+path;
    }
    static String xtreamAccountIdentity(String value)throws IOException{URI uri=ProviderHttp.checkedUri(xtreamBase(value));int port=uri.getPort()>0?uri.getPort():"https".equalsIgnoreCase(uri.getScheme())?443:80;return uri.getScheme().toLowerCase(Locale.ROOT)+"://"+uri.getHost().toLowerCase(Locale.ROOT)+":"+port+uri.getRawPath();}
    static Result xtream(JSONObject config,ProviderHttp.Fetcher fetcher,AtomicBoolean cancel)throws Exception{
        String base=config.getString("base"),user=config.getString("user"),password=config.getString("password"),api=base+"/player_api.php?username="+encode(user)+"&password="+encode(password);JSONObject auth=jsonObject(fetcher.fetch(api,Collections.emptyMap(),cancel).text());JSONObject info=auth.optJSONObject("user_info");
        if(info==null||info.optInt("auth",0)!=1||!"active".equalsIgnoreCase(info.optString("status","active")))throw new IOException("Dostawca odrzucił dane Xtream albo konto jest nieaktywne.");
        long expires=info.optLong("exp_date",0);if(expires>0&&expires<System.currentTimeMillis()/1000)throw new IOException("Dostawca zgłasza wygaśnięcie konta.");
        String extension="m3u8";JSONArray formats=info.optJSONArray("allowed_output_formats");if(formats!=null&&formats.length()>0){boolean hls=false,ts=false;for(int i=0;i<formats.length();i++){hls|="m3u8".equalsIgnoreCase(formats.optString(i));ts|="ts".equalsIgnoreCase(formats.optString(i));}if(!hls&&ts)extension="ts";}
        Map<String,String> groups=new HashMap<>();try{JSONArray categories=jsonArray(fetcher.fetch(api+"&action=get_live_categories",Collections.emptyMap(),cancel).text());for(int i=0;i<Math.min(categories.length(),10000);i++){JSONObject c=categories.optJSONObject(i);if(c!=null)groups.put(c.optString("category_id"),ProviderRepository.label(c.optString("category_name"),""));}}catch(Exception e){PublicHttp.checkCancelled(cancel);}
        ProviderHttp.Document document=fetcher.fetch(api+"&action=get_live_streams",Collections.emptyMap(),cancel);JSONArray streams=jsonArray(document.text());if(streams.length()>30000)throw new IOException("Lista Xtream przekracza limit 30 000 kanałów.");Result result=new Result();result.downloaded=document.downloadedAt;java.util.Set<String> seen=new java.util.HashSet<>();
        for(int i=0;i<streams.length();i++){PublicHttp.checkCancelled(cancel);JSONObject channel=streams.optJSONObject(i);if(channel==null)continue;String id=channel.optString("stream_id");if(!id.matches("[0-9]{1,18}")||!seen.add(id))continue;Station station=new Station();station.name=ProviderRepository.label(channel.optString("name"),"Kanał "+id);station.tvgId=ProviderRepository.label(channel.optString("epg_channel_id"),"");station.group=groups.getOrDefault(channel.optString("category_id"),"");station.url=base+"/live/"+encode(user)+"/"+encode(password)+"/"+id+"."+extension;station.fetchedAt=document.downloadedAt;station.modifiedAt=document.modifiedAt;result.entries.put(entry(id,station,"",Collections.emptyMap()));}
        result.epgUrl=base+"/xmltv.php?username="+encode(user)+"&password="+encode(password);return result;
    }
    static String portalEndpoint(String input)throws IOException{
        URI uri=ProviderHttp.checkedUri(input);if(uri.getRawQuery()!=null)throw new IOException("Podaj sam adres portalu, bez parametrów.");String path=uri.getRawPath()==null?"":uri.getRawPath();path=path.replaceFirst("/+$","");path=path.replaceFirst("(?i)/c(?:/index\\.html?)?$","");if(!path.endsWith("/server/load.php")&&!path.endsWith("/portal.php"))path+="/server/load.php";return uri.getScheme()+"://"+uri.getRawAuthority()+path;
    }
    static String mac(String input)throws IOException{String mac=input==null?"":input.trim().toUpperCase(Locale.ROOT);if(!mac.matches("(?:[0-9A-F]{2}:){5}[0-9A-F]{2}")||mac.equals("00:00:00:00:00:00")||mac.equals("FF:FF:FF:FF:FF:FF"))throw new IOException("Wpisz adres MAC zarejestrowany u swojego dostawcy.");return mac;}
    static final class PortalSession{final String endpoint,mode;final Map<String,String> headers;PortalSession(String endpoint,Map<String,String> headers,String mode){this.endpoint=endpoint;this.headers=Collections.unmodifiableMap(new LinkedHashMap<>(headers));this.mode=mode;}}
    static PortalSession portalSession(JSONObject config,ProviderHttp.Fetcher fetcher,AtomicBoolean cancel)throws Exception{
        Map<String,String> headers=new LinkedHashMap<>();headers.put("Cookie","mac="+mac(config.getString("mac"))+"; stb_lang=pl; timezone=Europe%2FWarsaw");headers.put("User-Agent",PortalDiscovery.USER_AGENT);
        for(String endpoint:PortalDiscovery.candidates(config.getString("url"))){
            PublicHttp.checkCancelled(cancel);PortalDiscovery.Kind kind;
            try{ProviderHttp.Document probe=fetcher.fetch(endpoint+"?type=itv&action=get_genres&JsHttpRequest=1-xml",headers,cancel);PublicHttp.checkCancelled(cancel);if(!ProviderHttp.sameOrigin(endpoint,probe.url))throw new IOException("Portal przekierował prywatne połączenie do innej domeny.");kind=PortalDiscovery.classify(probe.text());}
            catch(ProviderHttp.StatusException status){if(status.code==401||status.code==403)kind=PortalDiscovery.Kind.AUTH_REQUIRED;else if(status.code==404||status.code==405)continue;else throw status;}
            if(kind==PortalDiscovery.Kind.DIRECT)return new PortalSession(endpoint,headers,PortalDiscovery.DIRECT);
            if(kind==PortalDiscovery.Kind.AUTH_REQUIRED)return portalFullSession(endpoint,headers,fetcher,cancel);
            if(kind==PortalDiscovery.Kind.DENIED)throw new IOException("Portal odmówił dostępu dla podanego MAC. Sprawdź uprawnienia u dostawcy.");
        }
        throw new IOException("Nie znaleziono zgodnego interfejsu portalu. Sprawdź adres u dostawcy; odtwarzanie niepotwierdzone.");
    }
    private static PortalSession portalFullSession(String endpoint,Map<String,String> initial,ProviderHttp.Fetcher fetcher,AtomicBoolean cancel)throws Exception{
        Map<String,String> headers=new LinkedHashMap<>(initial);PublicHttp.checkCancelled(cancel);
        JSONObject handshake=portalObject(fetcher.fetch(endpoint+"?type=stb&action=handshake&JsHttpRequest=1-xml",headers,cancel).text()).optJSONObject("js");String token=handshake==null?"":handshake.optString("token");if(token.isEmpty()||token.length()>4096||token.contains("\r")||token.contains("\n"))throw new IOException("Portal nie przyznał sesji dla podanego MAC.");headers.put("Authorization","Bearer "+token);
        PublicHttp.checkCancelled(cancel);
        JSONObject profile=portalObject(fetcher.fetch(endpoint+"?type=stb&action=get_profile&JsHttpRequest=1-xml",headers,cancel).text()).optJSONObject("js");
        if(!portalProfileActive(profile))throw new IOException("Portal nie potwierdził aktywnego urządzenia. Może wymagać dodatkowej rejestracji u dostawcy.");
        PublicHttp.checkCancelled(cancel);return new PortalSession(endpoint,headers,PortalDiscovery.FULL);
    }
    /** Portal state must be explicit, never the default of a failed numeric coercion. */
    static boolean portalProfileActive(JSONObject profile){return profile!=null&&positiveProfileId(profile.opt("id"))&&clearProfileFlag(profile.opt("status"))&&clearProfileFlag(profile.opt("blocked"));}
    private static boolean positiveProfileId(Object value){
        if(value instanceof Integer||value instanceof Long)return ((Number)value).longValue()>0;
        if(!(value instanceof String))return false;String text=profileTrim((String)value);if(!text.matches("[0-9]+"))return false;try{return Long.parseLong(text)>0;}catch(NumberFormatException invalid){return false;}
    }
    private static boolean clearProfileFlag(Object value){
        if(value==null||value==JSONObject.NULL)return true;if(value instanceof Boolean)return !((Boolean)value);
        if(value instanceof Integer||value instanceof Long)return ((Number)value).longValue()==0;
        if(!(value instanceof String))return false;String text=profileTrim((String)value);return text.isEmpty()||text.equals("0")||text.matches("[fF][aA][lL][sS][eE]");
    }
    private static String profileTrim(String text){int start=0,end=text.length();while(start<end&&" \t\r\n".indexOf(text.charAt(start))>=0)start++;while(end>start&&" \t\r\n".indexOf(text.charAt(end-1))>=0)end--;return text.substring(start,end);}
    static Result portal(JSONObject config,ProviderHttp.Fetcher fetcher,AtomicBoolean cancel)throws Exception{
        PortalSession session=portalSession(config,fetcher,cancel);Map<String,String> groups=new HashMap<>();
        try{Object genres=portalObject(fetcher.fetch(session.endpoint+"?type=itv&action=get_genres&JsHttpRequest=1-xml",session.headers,cancel).text()).opt("js");if(genres instanceof JSONArray){JSONArray array=(JSONArray)genres;for(int i=0;i<Math.min(array.length(),10000);i++){JSONObject g=array.optJSONObject(i);if(g!=null)groups.put(g.optString("id"),ProviderRepository.label(g.optString("title"),""));}}}catch(Exception e){PublicHttp.checkCancelled(cancel);}
        PortalChannelReader.Catalog document=fetcher.fetchPortalChannels(session.endpoint+"?type=itv&action=get_all_channels&JsHttpRequest=1-xml",session.headers,cancel);
        if(!ProviderHttp.sameOrigin(session.endpoint,document.url))throw new IOException("Portal przekierował prywatne połączenie do innej domeny.");
        if(document.channels.isEmpty())throw new IOException("Portal nie zwrócił listy kanałów dla zarejestrowanego MAC.");Result result=new Result();result.downloaded=document.downloadedAt;
        for(int i=0;i<document.channels.size();i++){PublicHttp.checkCancelled(cancel);PortalChannelReader.Channel c=document.channels.get(i);Station station=new Station();station.name=ProviderRepository.label(c.name,"Kanał "+(i+1));station.group=groups.getOrDefault(c.groupId,"");station.tvgId=ProviderRepository.label(c.xmltvId,"");station.fetchedAt=document.downloadedAt;station.modifiedAt=document.modifiedAt;result.entries.put(entry(c.id,station,c.command,Collections.emptyMap()).put("use_http_tmp_link",c.useHttpTmpLink==null?JSONObject.NULL:c.useHttpTmpLink).put("use_load_balancing",c.useLoadBalancing==null?JSONObject.NULL:c.useLoadBalancing));document.channels.set(i,null);}
        result.portalMode=session.mode;result.portalEndpoint=session.endpoint;result.portalTruncated=document.truncated;result.portalRowsRead=document.rowsRead;result.portalRawBytes=document.rawBytes;return result;
    }
    static String portalLink(PortalSession session,String command,ProviderHttp.Fetcher fetcher,AtomicBoolean cancel)throws Exception{
        JSONObject js=portalObject(fetcher.fetch(session.endpoint+"?type=itv&action=create_link&cmd="+encode(command)+"&JsHttpRequest=1-xml",session.headers,cancel).text()).optJSONObject("js");String result=js==null?"":js.optString("cmd").trim();result=result.replaceFirst("^(?:ffmpeg|ffrt)\\s+","");return ProviderHttp.checkedUri(result).toString();
    }
    private static JSONObject portalObject(String text)throws IOException{PortalJson.validate(text);return jsonObject(text);}
    private static JSONObject jsonObject(String text)throws IOException{try{Object value=new JSONTokener(text).nextValue();if(value instanceof JSONObject)return (JSONObject)value;}catch(Exception ignored){}throw new IOException("Dostawca zwrócił nieprawidłową odpowiedź API.");}
    private static JSONArray jsonArray(String text)throws IOException{try{Object value=new JSONTokener(text).nextValue();if(value instanceof JSONArray)return (JSONArray)value;}catch(Exception ignored){}throw new IOException("Dostawca zwrócił nieprawidłową listę kanałów.");}
}
