package com.swir.stream;

import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.text.Normalizer;
import java.util.Locale;
import java.util.regex.Pattern;

/** A channel/source candidate. Presence in a catalogue never proves playback. */
public final class Station {
    private static final Pattern MARKS=Pattern.compile("\\p{M}+"),LABELS=Pattern.compile("(?:\\(|\\[)\\s*(?:uhd|fhd|hd|sd|4k|\\d{3,4}[pi]|geo-blocked|not 24/7|offline)\\s*(?:\\)|\\])"),QUALITY=Pattern.compile("\\b(?:uhd|fhd|hd|sd|4k|\\d{3,4}[pi])\\b"),OTHER=Pattern.compile("[^\\p{L}\\p{N}+]"),VARIANT_QUALITY=Pattern.compile("(?:uhd|fhd|hd|sd|4k|\\d{3,4}[pi])$");
    public String id="",name="",url="",country="",kind="tv",group="",logo="",tvgId="",userAgent="",referrer="",origin="";
    public String homepage="",feed="";
    public String providerId="",providerChannelId="",providerProfileId="";
    /** Resolved playback data lives only in memory, never in profile/catalog JSON. */
    public java.util.Map<String,String> requestHeaders=new java.util.HashMap<>(),drmHeaders=new java.util.HashMap<>();
    public String drmScheme="",drmLicenseUrl="";
    public String[] aliases=new String[0];
    public long fetchedAt,modifiedAt,manifestCheckedAt;
    public boolean liveManifest;
    public boolean freshManifest(){long age=System.currentTimeMillis()-manifestCheckedAt;return liveManifest&&age>=0&&age<30*60*1000L;}
    public String availability(){return freshManifest()?"Manifest live dostępny • obraz do sprawdzenia":manifestCheckedAt>0?"Działanie niepotwierdzone • spróbuj odtworzyć":"Jeszcze niesprawdzone";}
    public Station() {}
    public boolean isProvider(){return !providerId.isEmpty();}
    public boolean validProviderReference(){return isProvider()&&providerId.matches("[0-9a-f]{32}")&&providerChannelId.matches("[0-9a-f]{64}")&&providerProfileId.matches("default|[0-9a-f]{32}")&&url.equals(providerReference());}
    public String providerReference(){return "swir://provider/"+providerProfileId+"/"+providerId+"/"+providerChannelId;}
    public String key(){if(isProvider())return providerReference();String identity=!id.isEmpty()?id:!tvgId.isEmpty()?tvgId:country+":"+normalizeName(name);String variant=programVariant(feed);return kind+":"+identity+(variant.isEmpty()?"":"@"+variant);}
    public String sourceKey(){return sha256(isProvider()?providerReference():url.trim());}
    public static String normalizeName(String value){
        if(value==null||value.isEmpty())return "";String text=MARKS.matcher(Normalizer.normalize(value,Normalizer.Form.NFKD)).replaceAll("").replace('ł','l').replace('Ł','L').toLowerCase(Locale.ROOT);
        return OTHER.matcher(QUALITY.matcher(LABELS.matcher(text).replaceAll(" ")).replaceAll(" ")).replaceAll("");
    }
    public static String programVariant(String value){return VARIANT_QUALITY.matcher(normalizeName(value)).replaceFirst("");}
    static String sha256(String text){try{byte[] bytes=MessageDigest.getInstance("SHA-256").digest(text.getBytes(StandardCharsets.UTF_8));char[] hex="0123456789abcdef".toCharArray(),result=new char[bytes.length*2];for(int i=0;i<bytes.length;i++){int value=bytes[i]&255;result[i*2]=hex[value>>>4];result[i*2+1]=hex[value&15];}return new String(result);}catch(Exception e){throw new IllegalStateException(e);}}
    public JSONObject toJson(){
        JSONObject value=new JSONObject();try{
            value.put("id",id);value.put("name",name);value.put("url",url);value.put("country",country);value.put("kind",kind);value.put("group",group);value.put("logo",logo);value.put("tvgId",tvgId);value.put("userAgent",userAgent);value.put("referrer",referrer);value.put("origin",origin);value.put("homepage",homepage);value.put("feed",feed);value.put("fetchedAt",fetchedAt);value.put("modifiedAt",modifiedAt);
            value.put("manifestCheckedAt",manifestCheckedAt);value.put("liveManifest",liveManifest);JSONArray names=new JSONArray();for(String alias:aliases)if(alias!=null)names.put(alias);value.put("aliases",names);
            if(isProvider()){
                value.put("providerId",providerId);value.put("providerChannelId",providerChannelId);value.put("providerProfileId",providerProfileId);
                value.put("url",providerReference());value.put("logo","");value.put("homepage","");value.put("userAgent","");value.put("referrer","");
            }return value;
        }catch(JSONException e){throw new IllegalStateException(e);}
    }
    public static Station fromJson(JSONObject value){
        Station s=new Station();s.id=string(value,"id");s.name=string(value,"name");s.url=string(value,"url");s.country=WorldMarkets.canonical(string(value,"country").toUpperCase(Locale.ROOT));s.kind="radio".equals(string(value,"kind"))?"radio":"tv";
        s.group=string(value,"group");s.logo=string(value,"logo");s.tvgId=string(value,"tvgId");s.userAgent=string(value,"userAgent");s.referrer=string(value,"referrer");s.origin=string(value,"origin");s.homepage=string(value,"homepage");s.feed=string(value,"feed");s.fetchedAt=Math.max(0,value.optLong("fetchedAt",0));s.modifiedAt=Math.max(0,value.optLong("modifiedAt",0));
        s.providerId=string(value,"providerId");s.providerChannelId=string(value,"providerChannelId");s.providerProfileId=string(value,"providerProfileId");
        if(s.isProvider()){
            if(!s.providerId.matches("[0-9a-f]{32}")||!s.providerChannelId.matches("[0-9a-f]{64}")||!s.providerProfileId.matches("default|[0-9a-f]{32}"))throw new IllegalArgumentException("Nieprawidłowa referencja źródła");
            s.url=s.providerReference();s.logo=s.homepage=s.userAgent=s.referrer="";
        }
        s.manifestCheckedAt=Math.max(0,value.optLong("manifestCheckedAt",0));s.liveManifest=value.optBoolean("liveManifest",false);JSONArray aliases=value.optJSONArray("aliases");if(aliases!=null){s.aliases=new String[Math.min(30,aliases.length())];for(int i=0;i<s.aliases.length;i++)s.aliases[i]=aliases.optString(i,"");}return s;
    }
    static String string(JSONObject value,String key){Object raw=value.opt(key);return raw instanceof String?(String)raw:"";}
}
