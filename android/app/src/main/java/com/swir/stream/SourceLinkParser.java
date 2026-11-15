package com.swir.stream;

import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.net.URI;
import java.nio.ByteBuffer;
import java.nio.charset.CharacterCodingException;
import java.nio.charset.CodingErrorAction;
import java.nio.charset.StandardCharsets;
import java.util.LinkedHashMap;
import java.util.Locale;
import java.util.Map;

/** Syntax-only private import. Never fetches, persists or includes input in errors. */
final class SourceLinkParser {
    enum Kind { XTREAM, M3U, PORTAL, DIRECT }
    static final class Result {
        final Kind kind; final String url,base,user,password,name;
        Result(Kind kind,String url,String base,String user,String password,String name){this.kind=kind;this.url=url;this.base=base;this.user=user;this.password=password;this.name=name;}
        boolean account(){return kind==Kind.XTREAM&&!user.isEmpty();}
        @Override public String toString(){return "SourceLink["+kind+"]";}
    }
    private static IOException invalid(){return new IOException("Nie można rozpoznać linku. Wklej pełny adres HTTP lub HTTPS od dostawcy, bez dodatkowego tekstu.");}
    private static IOException credentials(){return new IOException("Link musi zawierać pojedynczy, niepusty login i hasło. Sprawdź dane u dostawcy.");}
    static String clean(String input)throws IOException{
        if(input==null||input.length()>16384)throw invalid();String value=input.trim();
        if(value.length()>1&&((value.startsWith("\"")&&value.endsWith("\""))||(value.startsWith("'")&&value.endsWith("'"))))value=value.substring(1,value.length()-1).trim();
        if(value.isEmpty()||controls(value))throw invalid();int q=value.indexOf('?');
        if(q>=0)value=value.substring(0,q+1)+value.substring(q+1).replaceAll("(?i)&amp;(?=[A-Za-z0-9_%.-]+=)","&");
        return value;
    }
    static Result parse(String input,boolean xtreamContext)throws IOException{
        String value=clean(input);URI uri;
        try{uri=new URI(value);if(uri.getHost()==null||!("http".equalsIgnoreCase(uri.getScheme())||"https".equalsIgnoreCase(uri.getScheme()))||uri.getRawUserInfo()!=null||uri.getRawFragment()!=null||uri.getPort()==0||uri.getPort()>65535)throw invalid();}catch(Exception error){throw invalid();}
        String path=uri.getRawPath()==null?"":uri.getRawPath();decode(path,false);String lower=path.toLowerCase(Locale.ROOT),name=uri.getHost();
        String endpoint=lower.replaceFirst("/+$","");endpoint=endpoint.substring(endpoint.lastIndexOf('/')+1);
        boolean api=endpoint.equals("get.php")||endpoint.equals("player_api.php")||endpoint.equals("xmltv.php");
        boolean account=accountParameters(uri.getRawQuery());Map<String,String> parameters=account||xtreamContext||api?query(uri.getRawQuery()):new LinkedHashMap<>();
        String[] pieces=path.split("/",-1);int live=pieces.length-4;boolean liveShape=live>=0&&pieces[live].equalsIgnoreCase("live")&&pieces[pieces.length-1].matches("(?i)[0-9]{1,18}\\.(?:ts|m3u8|mp4)");
        if(liveShape){
            int liveSegments=0;for(String piece:pieces)if(piece.equalsIgnoreCase("live"))liveSegments++;if(liveSegments!=1)throw new IOException("Link zawiera niejednoznaczną ścieżkę transmisji. Użyj linku get.php lub danych konta.");
            if(account||uri.getRawQuery()!=null)throw new IOException("Link transmisji zawiera dodatkowe parametry. Użyj pełnego linku Xtream get.php lub danych konta.");
            String user=credential(decode(pieces[live+1],false)),password=credential(decode(pieces[live+2],false));String prefix=path.substring(0,path.lastIndexOf("/"+pieces[live]+"/"));
            return new Result(Kind.XTREAM,value,base(uri,prefix),user,password,name);
        }
        if(account){
            if(!api&&!xtreamContext)throw new IOException("Link zawiera dane konta, ale nie wskazuje formatu. Wybierz Xtream lub świadomie dodaj go jako własną playlistę URL.");
            if(!api&&!path.endsWith("/")&&endpoint.contains("."))throw new IOException("Link wskazuje plik, a nie serwer Xtream. Użyj własnej playlisty URL albo linku get.php / player_api.php.");
            String user=credential(parameters.get("username")),password=credential(parameters.get("password"));
            for(Map.Entry<String,String> parameter:parameters.entrySet()){
                String key=parameter.getKey(),entry=parameter.getValue().toLowerCase(Locale.ROOT);boolean known=key.equals("username")||key.equals("password")||key.equals("type")&&(entry.equals("m3u")||entry.equals("m3u_plus"))||key.equals("output")&&(entry.equals("ts")||entry.equals("m3u8")||entry.equals("hls"))||key.equals("action")&&endpoint.equals("player_api.php")&&(entry.equals("get_live_streams")||entry.equals("get_live_categories"));
                if(!known)throw new IOException("Link wymaga dodatkowych parametrów dostawcy. Nie usunięto ich. Użyj własnej playlisty URL lub podaj dane Xtream ręcznie.");
            }
            String prefix=api?path.replaceFirst("(?i)/(?:player_api|get|xmltv)\\.php/*$",""):path;
            return new Result(Kind.XTREAM,value,base(uri,prefix),user,password,name);
        }
        if(api&&uri.getRawQuery()!=null)throw credentials();
        if(xtreamContext){if(uri.getRawQuery()!=null)throw new IOException("Adres serwera zawiera nieobsługiwane parametry. Podaj pełny link konta albo sam serwer.");if(!api&&!path.endsWith("/")&&endpoint.contains("."))throw new IOException("Link wskazuje plik, a nie serwer Xtream. Użyj własnej playlisty URL albo linku get.php / player_api.php.");return new Result(Kind.XTREAM,value,base(uri,path.replaceFirst("(?i)/(?:player_api|get|xmltv)\\.php/*$","")),"","",name);}
        Kind kind=uri.getRawQuery()==null&&lower.matches(".*/stalker_portal(?:/c(?:/index\\.html?)?)?/?$")?Kind.PORTAL:lower.matches(".*\\.(?:mpd|mp4|mp3|aac)$")?Kind.DIRECT:Kind.M3U;
        return new Result(kind,value,"","","",name);
    }
    static Result xtream(String input,String user,String password)throws IOException{
        Result parsed=parse(input,true);String login=user==null?"":user,secret=password==null?"":password;
        if(parsed.account()){
            if(!login.isEmpty()&&!login.equals(parsed.user)||!secret.isEmpty()&&!secret.equals(parsed.password))throw new IOException("Dane wpisane osobno różnią się od linku. Wyczyść login i hasło albo użyj samego adresu serwera; niczego nie nadpisano.");
            return parsed;
        }
        return new Result(Kind.XTREAM,parsed.url,parsed.base,credential(login),credential(secret),parsed.name);
    }
    private static String base(URI uri,String path){return uri.getScheme().toLowerCase(Locale.ROOT)+"://"+uri.getRawAuthority().toLowerCase(Locale.ROOT)+path.replaceFirst("/+$","");}
    private static Map<String,String> query(String raw)throws IOException{
        Map<String,String> out=new LinkedHashMap<>();if(raw==null||raw.isEmpty())return out;if(raw.indexOf(';')>=0)throw new IOException("Link zawiera niejednoznaczny separator parametrów. Poproś dostawcę o pełny link Xtream.");
        for(String pair:raw.split("&",-1)){int equals=pair.indexOf('=');if(equals<1)throw invalid();String key=decode(pair.substring(0,equals),true).toLowerCase(Locale.ROOT),value=decode(pair.substring(equals+1),true);if(out.containsKey(key))throw new IOException("Link powtarza parametr. Użyj pojedynczego zestawu danych od dostawcy.");out.put(key,value);}return out;
    }
    private static boolean accountParameters(String raw)throws IOException{if(raw==null)return false;for(String pair:raw.split("[&;]",-1)){int equal=pair.indexOf('=');String key=pair.substring(0,equal<0?pair.length():equal);try{key=decode(key,true);}catch(IOException malformed){if(importCandidate(raw))throw invalid();continue;}if(key.equalsIgnoreCase("username")||key.equalsIgnoreCase("password"))return true;}return false;}
    private static String credential(String value)throws IOException{if(value==null||value.trim().isEmpty()||value.length()>2048||controls(value))throw credentials();return value;}
    private static boolean controls(String value){for(int i=0;i<value.length();i++)if(Character.isISOControl(value.charAt(i)))return true;return false;}
    static String decode(String value,boolean form)throws IOException{
        ByteArrayOutputStream bytes=new ByteArrayOutputStream();
        for(int i=0;i<value.length();){char c=value.charAt(i);if(c=='%'){if(i+2>=value.length())throw invalid();int high=Character.digit(value.charAt(i+1),16),low=Character.digit(value.charAt(i+2),16);if(high<0||low<0)throw invalid();bytes.write(high*16+low);i+=3;}else{int point=value.codePointAt(i);if(Character.isSurrogate(c)&&Character.charCount(point)==1)throw invalid();byte[] encoded=new String(Character.toChars(point)).getBytes(StandardCharsets.UTF_8);if(form&&c=='+')bytes.write(' ');else bytes.write(encoded,0,encoded.length);i+=Character.charCount(point);}}
        try{String decoded=StandardCharsets.UTF_8.newDecoder().onMalformedInput(CodingErrorAction.REPORT).onUnmappableCharacter(CodingErrorAction.REPORT).decode(ByteBuffer.wrap(bytes.toByteArray())).toString();if(controls(decoded))throw invalid();return decoded;}catch(CharacterCodingException error){throw invalid();}
    }
    /** Also catches malformed/encoded account input before search, logs or saved state. */
    static boolean importCandidate(String input){
        if(input==null)return false;String value=input.trim();if(value.matches("(?is)^[\"']?https?:.*"))return true;
        for(int pass=0;pass<=2;pass++){if(value.matches("(?is).*(?:[?&;\\s]|^)(?:username|password|passwd|access_token|token|authorization|api_key|mac)\\s*=.*")||value.matches("(?is).*https?.*/(?:live|movie|series)/[^/]+/[^/]+/.*"))return true;StringBuilder decoded=new StringBuilder();for(int i=0;i<value.length();i++){char c=value.charAt(i);if(c=='%'&&i+2<value.length()){int h=Character.digit(value.charAt(i+1),16),l=Character.digit(value.charAt(i+2),16);if(h>=0&&l>=0){decoded.append((char)(h*16+l));i+=2;continue;}}decoded.append(c);}value=decoded.toString();}return false;
    }
    private SourceLinkParser(){}
}
