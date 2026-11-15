package com.swir.stream;

import java.io.IOException;
import java.net.URI;
import java.util.ArrayList;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Locale;
import java.util.regex.Matcher;
import java.util.regex.Pattern;

/** Public discovery rules only; never interprets a published MAC/password as access. */
final class SourceExplorerRules {
    enum Kind { PortalCandidate, PublicPlaylist, DirectStream, ProviderPage }
    private static final Pattern PRIVATE_TEXT=Pattern.compile("(?i)(?:\\b(?:[0-9a-f]{2}[:-]){5}[0-9a-f]{2}\\b|\\b(?:mac|password|passwd|username|login|hasło|haslo|debug_key|authorization|cookie)[\"']?\\s*[:=]\\s*[\"']?[^\\s<]+)");
    private static final Pattern ATTRIBUTE=Pattern.compile("(?i)\\b(?:href|src|data-src|file|url)\\s*[=:]\\s*([\"'])([^\"'<>]{1,4096})\\1");
    private static final Pattern ABSOLUTE=Pattern.compile("(?i)https?://[^\\s<>\"'\\\\]{1,4096}");
    static boolean privateText(String value){
        if(value==null)return false;String decoded=entities(entities(value));for(int pass=0;pass<=2;pass++){if(PRIVATE_TEXT.matcher(decoded).find()||SourcePrivacy.publicCredentials(decoded))return true;StringBuilder out=new StringBuilder();for(int i=0;i<decoded.length();i++){char c=decoded.charAt(i);if(c=='%'&&i+2<decoded.length()){int h=Character.digit(decoded.charAt(i+1),16),l=Character.digit(decoded.charAt(i+2),16);if(h>=0&&l>=0){out.append((char)(h*16+l));i+=2;continue;}}out.append(c);}decoded=out.toString();}return false;
    }
    static String canonical(String value)throws IOException{
        try{String input=value==null?"":entities(value.trim());if(input.length()>8192||input.matches("(?s).*[\\p{Cntrl}].*"))throw new Exception();URI uri=new URI(input).normalize();String scheme=uri.getScheme(),host=uri.getHost();if(host==null||!("http".equalsIgnoreCase(scheme)||"https".equalsIgnoreCase(scheme))||uri.getRawUserInfo()!=null)throw new Exception();scheme=scheme.toLowerCase(Locale.ROOT);host=host.toLowerCase(Locale.ROOT);if(host.endsWith("."))host=host.substring(0,host.length()-1);int port=uri.getPort();if(port!=-1&&port!=80&&port!=443)throw new Exception();if(host.indexOf(':')>=0&&!host.startsWith("["))host="["+host+"]";String authority=host+(port==-1||scheme.equals("http")&&port==80||scheme.equals("https")&&port==443?"":":"+port);String path=uri.getRawPath();String result=scheme+"://"+authority+(path==null||path.isEmpty()?"/":path)+(uri.getRawQuery()==null?"":"?"+uri.getRawQuery());if(!PublicHttp.allowedDocument(result)||privateText(result))throw new Exception();return result;}
        catch(Exception error){throw new IOException("Eksplorator przyjmuje publiczne adresy HTTP/HTTPS na portach 80/443, bez danych konta.");}
    }
    static boolean allowedPage(String value){try{URI uri=new URI(canonical(value));String path=uri.getPath().toLowerCase(Locale.ROOT);boolean media=mediaLink(value)||playlistLink(value);if(path.matches(".*(?:/(?:admin|adm|administrator|login|signin|account|server)(?:/|$)|/(?:portal|load|player_api|get|xmltv|login)\\.php(?:/|$)).*")||!media&&path.matches(".*/api(?:/|$).*")||path.matches(".*\\.(?:js|css|png|jpe?g|gif|svg|ico|woff2?|ttf|zip|apk|exe)$"))return false;if(uri.getRawQuery()!=null)for(String pair:uri.getRawQuery().split("[&;]",-1)){String key=SourceLinkParser.decode(pair.split("=",2)[0],true).toLowerCase(Locale.ROOT);if(key.matches("action|type|jshttprequest|debug|debug_key|mac|sn|serial|username|password")||!media&&key.matches("token|auth|session"))return false;}return true;}catch(Exception error){return false;}}
    static boolean clientPath(String value){try{String path=new URI(value).getPath().toLowerCase(Locale.ROOT);return path.matches(".*/c(?:/index\\.html?)?/?$");}catch(Exception error){return false;}}
    static boolean explicitPortalPath(String value){try{String path=new URI(value).getPath().toLowerCase(Locale.ROOT);return path.matches(".*/stalker_portal(?:/c(?:/index\\.html?)?)?/?$");}catch(Exception error){return false;}}
    static boolean portalFingerprint(String url,String html){
        if(!allowedPage(url)||!clientPath(url)||html==null)return false;String content=html.replaceAll("(?s)<!--.*?-->","").replaceAll("(?is)<(?:template|textarea|noscript|pre|code)\\b[^>]*>.*?</(?:template|textarea|noscript|pre|code)\\s*>","");String lower=content.toLowerCase(Locale.ROOT);boolean platform=lower.contains("stalker_portal")||lower.contains("ministra")||Pattern.compile("(?i)\\bstb\\b").matcher(content).find();if(!platform)return false;
        Matcher scripts=Pattern.compile("(?is)<script\\b([^>]{0,4096})>(?:.*?)</script\\s*>").matcher(content);while(scripts.find())try{String attributes=scripts.group(1);Matcher type=Pattern.compile("(?i)\\btype\\s*=\\s*([\"'])([^\"']*)\\1").matcher(attributes);if(type.find()&&!type.group(2).matches("(?i)(?:application|text)/(?:java|ecma)script|module|"))continue;Matcher src=Pattern.compile("(?i)\\bsrc\\s*=\\s*([\"'])([^\"']{1,4096})\\1").matcher(attributes);if(!src.find())continue;URI source=new URI(entities(src.group(2)));if(source.getScheme()!=null&&!source.getScheme().matches("(?i)https?"))continue;String path=source.getPath()==null?"":source.getPath().toLowerCase(Locale.ROOT);String basename=path.substring(path.lastIndexOf('/')+1);if(basename.equals("xpcom.common.js")||path.matches("(?:^|.*/)server/api/load_js\\.php"))return true;}catch(Exception ignored){}return false;
    }
    static String extension(String url){try{String path=new URI(url).getPath().toLowerCase(Locale.ROOT);int dot=path.lastIndexOf('.');return dot<0?"":path.substring(dot+1);}catch(Exception error){return "";}}
    static boolean mediaLink(String url){String suffix=extension(url);return suffix.equals("m3u8")||suffix.equals("mpd")||suffix.equals("mp4")||suffix.equals("mp3")||suffix.equals("aac");}
    static boolean playlistLink(String url){String suffix=extension(url);return suffix.equals("m3u")||suffix.equals("m3u8");}
    static boolean protectedManifest(String text){if(text==null)return false;if(Pattern.compile("(?i)<(?:[a-z0-9_-]+:)?ContentProtection\\b").matcher(text).find())return true;Matcher keys=Pattern.compile("(?im)^#EXT-X-(?:SESSION-)?KEY:([^\\r\\n]+)").matcher(text);while(keys.find()){String attrs=keys.group(1);if(Pattern.compile("(?i)(?:^|,)\\s*METHOD\\s*=\\s*SAMPLE-AES").matcher(attrs).find())return true;Matcher format=Pattern.compile("(?i)(?:^|,)\\s*KEYFORMAT\\s*=\\s*(?:[\"']([^\"']+)[\"']|([^,\\s]+))").matcher(attrs);if(format.find()&&!"identity".equalsIgnoreCase(format.group(1)==null?format.group(2):format.group(1)))return true;}return false;}
    static List<String> links(String base,String html){
        LinkedHashSet<String> media=new LinkedHashSet<>(),navigation=new LinkedHashSet<>();if(html==null)return new ArrayList<>();
        // Inspect the whole bounded document: navigation must not hide a playlist in a README/code block.
        Matcher attributes=ATTRIBUTE.matcher(html);while(attributes.find())if(!privateText(html.substring(Math.max(0,attributes.start()-180),Math.min(html.length(),attributes.end()+180))))add(base,attributes.group(2),media,navigation);
        Matcher absolute=ABSOLUTE.matcher(html);while(absolute.find())if(!privateText(html.substring(Math.max(0,absolute.start()-180),Math.min(html.length(),absolute.end()+180))))add(base,absolute.group(),media,navigation);
        List<String> result=new ArrayList<>(media);result.sort((a,b)->Integer.compare(linkPriority(a),linkPriority(b)));for(String link:navigation)if(result.size()<100&&!media.contains(link))result.add(link);return result;
    }
    static boolean preferredLink(String url){return playlistLink(url)||mediaLink(url)||clientPath(url)||explicitPortalPath(url);}
    private static int linkPriority(String url){return playlistLink(url)?0:mediaLink(url)?1:2;}
    private static void add(String base,String relative,LinkedHashSet<String> media,LinkedHashSet<String> navigation){try{String target=canonical(new URI(base).resolve(entities(relative.replace("\\/","/"))).toString());if(allowedPage(target)){LinkedHashSet<String> found=preferredLink(target)?media:navigation;if(found.size()<100)found.add(target);}}catch(Exception ignored){}}
    static String title(String html,String fallback){if(html==null)return fallback;Matcher title=Pattern.compile("(?is)<title[^>]{0,1024}>(.{0,2048}?)</title>").matcher(html);String value=title.find()?entities(title.group(1).replaceAll("<[^>]*>"," ")).trim():fallback;if(value.isEmpty()||privateText(value)||value.contains("://"))return fallback;return value.substring(0,Math.min(160,value.length()));}
    static String entities(String value){String named=value.replace("&amp;","&").replace("&quot;","\"").replace("&apos;","'").replace("&colon;",":").replace("&lt;","<").replace("&gt;",">");Matcher numbers=Pattern.compile("(?i)&#(x[0-9a-f]{1,6}|[0-9]{1,7});?").matcher(named);StringBuffer decoded=new StringBuffer();while(numbers.find()){String entity=numbers.group(1);try{int point=Integer.parseInt(entity.startsWith("x")||entity.startsWith("X")?entity.substring(1):entity,entity.startsWith("x")||entity.startsWith("X")?16:10);if(!Character.isValidCodePoint(point)||point>=0xd800&&point<=0xdfff)continue;numbers.appendReplacement(decoded,Matcher.quoteReplacement(new String(Character.toChars(point))));}catch(Exception ignored){}}numbers.appendTail(decoded);return decoded.toString();}
    private SourceExplorerRules(){}
}
