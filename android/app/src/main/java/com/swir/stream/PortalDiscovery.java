package com.swir.stream;

import org.json.JSONArray;
import org.json.JSONObject;
import java.io.IOException;
import java.net.URI;
import java.util.ArrayList;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Locale;

/** Bounded endpoint classification for a portal explicitly supplied by its user. */
final class PortalDiscovery {
    static final String USER_AGENT="Mozilla/5.0 (QtEmbedded; U; Linux; C) AppleWebKit/533.3 (KHTML, like Gecko) MAG250";
    static final String DIRECT="direct_mac", FULL="full_profile";
    static final String DIRECT_NOTICE="Katalog MAC pobrany bez pełnej sesji. Aktywność konta i odtwarzanie niepotwierdzone.";
    static final String FULL_NOTICE="Profil portalu zaakceptowany; katalog pobrany. Odtwarzanie niepotwierdzone.";
    enum Kind { DIRECT, AUTH_REQUIRED, DENIED, UNKNOWN }
    static Boolean channelFlag(Object value){
        if(value instanceof Boolean)return (Boolean)value;
        if(value instanceof Integer||value instanceof Long){long number=((Number)value).longValue();return number==0?Boolean.FALSE:number==1?Boolean.TRUE:null;}
        if(!(value instanceof String))return null;String text=(String)value;
        if(text.equals("0")||text.equals("false"))return Boolean.FALSE;
        if(text.equals("1")||text.equals("true"))return Boolean.TRUE;return null;
    }
    static String originalCommand(JSONObject entry){
        if(!Boolean.FALSE.equals(entry.opt("use_http_tmp_link"))||!Boolean.FALSE.equals(entry.opt("use_load_balancing")))return "";
        String command=entry.optString("command").trim().replaceFirst("^(?:ffmpeg|ffrt)\\s+","");
        try{return ProviderHttp.checkedUri(command).toString();}catch(IOException ignored){return "";}
    }

    static String input(String value)throws IOException{
        URI uri=ProviderHttp.checkedUri(value);
        if(uri.getRawQuery()!=null)throw new IOException("Podaj sam adres portalu, bez parametrów.");
        return uri.getScheme()+"://"+uri.getRawAuthority()+(uri.getRawPath()==null?"":uri.getRawPath());
    }
    static List<String> candidates(String value)throws IOException{
        URI uri=URI.create(input(value));String origin=uri.getScheme()+"://"+uri.getRawAuthority();
        String path=uri.getRawPath()==null?"":uri.getRawPath();path=path.replaceFirst("/+$","");
        LinkedHashSet<String> paths=new LinkedHashSet<>();boolean php=path.toLowerCase(Locale.ROOT).endsWith(".php");
        if(php)paths.add(path);
        String base=php?path.replaceFirst("(?i)/server/load\\.php$","").replaceFirst("(?i)/[^/]+\\.php$",""):path.replaceFirst("(?i)/c(?:/index\\.html?)?$","");
        paths.add(base+"/portal.php");paths.add(base+"/server/load.php");
        if(!base.toLowerCase(Locale.ROOT).matches(".*/stalker_portal(?:/.*)?$"))paths.add(base+"/stalker_portal/server/load.php");
        List<String> result=new ArrayList<>();for(String candidate:paths){String address=origin+candidate;if(!ProviderHttp.sameOrigin(value,address))throw new IOException("Nieprawidłowy adres portalu.");result.add(address);if(result.size()==3)break;}return result;
    }
    static Kind classify(String body){
        String text=body==null?"":body.trim();if(text.startsWith("\uFEFF"))text=text.substring(1).trim();
        if(!text.startsWith("{")&&!text.startsWith("[")&&deniedMessage(text))return Kind.DENIED;
        if(authMessage(text))return Kind.AUTH_REQUIRED;
        try{
            PortalJson.validate(text);JSONObject root=new JSONObject(text);Object js=root.opt("js");
            if(denial(root)||js instanceof JSONObject&&denial((JSONObject)js)||js instanceof String&&deniedMessage((String)js))return Kind.DENIED;
            if(authEnvelope(root)||js instanceof String&&authMessage((String)js))return Kind.AUTH_REQUIRED;
            if(js instanceof JSONObject&&authEnvelope((JSONObject)js))return Kind.AUTH_REQUIRED;
            if(hasError(root)||js instanceof JSONObject&&hasError((JSONObject)js))return Kind.UNKNOWN;
            JSONArray array=js instanceof JSONArray?(JSONArray)js:js instanceof JSONObject?((JSONObject)js).optJSONArray("data"):null;
            if(array==null||array.length()==0||array.length()>10000)return Kind.UNKNOWN;
            for(int i=0;i<array.length();i++){JSONObject row=array.optJSONObject(i);if(row!=null&&denial(row))return Kind.DENIED;}
            for(int i=0;i<array.length();i++){
                JSONObject genre=array.optJSONObject(i);if(genre==null)return Kind.UNKNOWN;if(denial(genre))return Kind.DENIED;if(authEnvelope(genre))return Kind.AUTH_REQUIRED;if(hasError(genre))return Kind.UNKNOWN;Object id=genre.opt("id"),title=genre.opt("title");
                if(!((id instanceof String||id instanceof Number)&&!String.valueOf(id).trim().isEmpty()&&String.valueOf(id).length()<=200&&title instanceof String&&!((String)title).trim().isEmpty()&&((String)title).length()<=200))return Kind.UNKNOWN;
            }return Kind.DIRECT;
        }catch(Exception ignored){/* A malformed or unrelated document proves no portal mode. */}
        return Kind.UNKNOWN;
    }
    static boolean denial(JSONObject value){
        Object auth=value.opt("auth");if(Boolean.FALSE.equals(auth)||Integer.valueOf(0).equals(auth)||Long.valueOf(0).equals(auth)||"0".equals(auth)||String.valueOf(auth).matches("[fF][aA][lL][sS][eE]"))return true;
        for(String key:new String[]{"error","errors","msg","message","block_msg"})if(messageMatch(value.opt(key),true))return true;
        for(String key:new String[]{"status","blocked"}){Object flag=value.opt(key);if(flag==null||flag==JSONObject.NULL||Boolean.FALSE.equals(flag)||Integer.valueOf(0).equals(flag)||Long.valueOf(0).equals(flag)||"0".equals(flag)||String.valueOf(flag).matches("[fF][aA][lL][sS][eE]")||"".equals(flag))continue;return true;}return false;
    }
    static boolean hasError(JSONObject value){for(String key:new String[]{"error","errors"}){Object error=value.opt(key);if(error!=null&&error!=JSONObject.NULL&&!Boolean.FALSE.equals(error)&&!"".equals(error)&&!Integer.valueOf(0).equals(error)&&!(error instanceof JSONArray&&((JSONArray)error).length()==0))return true;}return false;}
    static boolean authEnvelope(JSONObject value){for(String key:new String[]{"error","errors","msg","message","block_msg"})if(messageMatch(value.opt(key),false))return true;return false;}
    private static boolean messageMatch(Object value,boolean denied){if(value instanceof String)return denied?deniedMessage((String)value):authMessage((String)value);if(value instanceof JSONArray){JSONArray values=(JSONArray)value;for(int i=0;i<Math.min(values.length(),100);i++){Object item=values.opt(i);if(item instanceof String&&(denied?deniedMessage((String)item):authMessage((String)item)))return true;}}return false;}
    private static boolean deniedMessage(String value){return value!=null&&value.matches("(?is).*(?:\\bblocked\\b|\\bdisabled\\b|\\bbanned\\b|\\bexpired\\b|\\binactive\\b).*?");}
    private static boolean authMessage(String value){return value!=null&&value.trim().matches("(?i)(?:authorization failed(?:[.!]?(?:\\s+[0-9]+)?)?|unauthorized(?: request)?[.!]?|not authorized[.!]?|(?:authentication|authorization) required[.!]?|access denied[.!]?|invalid token[.!]?)");}
    private PortalDiscovery(){}
}
