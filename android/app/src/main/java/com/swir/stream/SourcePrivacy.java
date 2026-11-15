package com.swir.stream;

import java.util.regex.Pattern;
import java.io.IOException;

/** Guides credential-bearing manual imports into the encrypted source manager. */
final class SourcePrivacy {
    private static final Pattern CREDENTIAL=Pattern.compile("(?i)(?:[?&;](?:username|password|passwd|token|access_token|auth|api_key|key|session|mac)=|https?://[^/\\s]+@|https?://[^\\s]+/(?:live|movie|series)/[^/\\s]+/[^/\\s]+/|(?:^|[|&\\n])(?:Authorization|Cookie)=)");
    static boolean requiresVault(String input){return input!=null&&CREDENTIAL.matcher(input).find();}
    private static final Pattern PUBLIC_CREDENTIAL=Pattern.compile("(?im)(?:[?&;](?:password|passwd|authorization|mac)\\s*=\\s*[^&\\s#\"'<>]+|https?://[^/\\s\"'<>]+@|/(?:live|movie|series)/[^/\\s?\"'<>]+/[^/\\s?\"'<>]+/\\d+(?:\\.(?:m3u8?|ts|mp4|mkv|avi))?(?=[?&#|\\s\"'<>]|$)|(?:^#EXTVLCOPT:\\s*http-(?:cookie|authorization)\\s*=|[|&](?:Authorization|Cookie)\\s*=|[\"'](?:Authorization|Cookie)[\"']\\s*:|^#(?:Authorization|Cookie)\\s*:|^#KODIPROP:[^\\r\\n]*license_key\\s*=|^#KODIPROP:[^\\r\\n]*(?:stream_headers|manifest_headers)\\s*=[^\\r\\n]*(?:Authorization|Cookie)\\s*=)\\s*[^\\r\\n]+)");
    /** Discovery never adopts credentials merely because someone published them. */
    static boolean publicCredentials(String input){
        if(input==null||input.isEmpty())return false;String value=M3u.entities(input);
        for(int pass=0;pass<2&&value.indexOf('%')>=0;pass++){
            StringBuilder decoded=new StringBuilder(value.length());
            for(int i=0;i<value.length();i++){char c=value.charAt(i);if(c=='%'&&i+2<value.length()){int high=Character.digit(value.charAt(i+1),16),low=Character.digit(value.charAt(i+2),16);if(high>=0&&low>=0){decoded.append((char)(high*16+low));i+=2;continue;}}decoded.append(c);}value=decoded.toString();
        }
        return PUBLIC_CREDENTIAL.matcher(value).find();
    }
    static final class CleanPlaylist {
        final String text,originalSha256;final int removedEntries;final boolean sanitized;
        CleanPlaylist(String text,String hash,int removed,boolean sanitized){this.text=text;this.originalSha256=hash;this.removedEntries=removed;this.sanitized=sanitized;}
    }
    static CleanPlaylist sanitizePublicPlaylist(String url,String body)throws IOException{
        if(publicCredentials(url))throw new IOException("Publiczny adres playlisty zawiera dane konta.");
        String hash=Station.sha256(body);if(!publicCredentials(body))return new CleanPlaylist(body,hash,0,false);
        java.util.List<String> output=new java.util.ArrayList<>(),pending=new java.util.ArrayList<>();int removed=0;
        for(String raw:body.replace("\r\n","\n").split("\n",-1)){
            String line=raw.trim().replace("\uFEFF","");
            if(line.toUpperCase(java.util.Locale.ROOT).startsWith("#EXTM3U")){output.add(publicCredentials(line)?"#EXTM3U":raw);continue;}
            if(line.isEmpty()||line.startsWith("#")){pending.add(raw);continue;}
            pending.add(raw);boolean unsafe=false;for(String part:pending)if(publicCredentials(part)){unsafe=true;break;}
            if(unsafe)removed++;else output.addAll(pending);pending.clear();
        }
        boolean unsafe=false;for(String part:pending)if(publicCredentials(part)){unsafe=true;break;}if(!unsafe)output.addAll(pending);
        return new CleanPlaylist(String.join("\n",output),hash,removed,true);
    }
    static void requirePublicPlaylist(String url,String body)throws IOException{
        if(publicCredentials(url)||publicCredentials(body))throw new IOException("Pominięto publiczną playlistę zawierającą dane dostępu. Własne konta dodaj w Moich źródłach.");
    }
    private SourcePrivacy(){}
}
