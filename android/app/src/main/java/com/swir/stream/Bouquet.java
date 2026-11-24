package com.swir.stream;

import java.net.URI;
import java.util.regex.*;
import java.io.IOException;

final class Bouquet {
    private static final Pattern SERVICE=Pattern.compile("(?m)^#SERVICE ");
    private static final Pattern ENCODED=Pattern.compile("^(https?%3a(?://|%2f%2f)[^:\\s]+)(?::(.*))?$",Pattern.CASE_INSENSITIVE);
    static boolean isBouquet(String text){return SERVICE.matcher(text.replace("\uFEFF","")).find();}
    static String toM3u(String text) throws IOException {
        StringBuilder out=new StringBuilder("#EXTM3U\n");String address="",name="";
        // A final empty service also flushes the last channel.
        for(String raw:(text+"\n#SERVICE ").split("\n")){
            String line=raw.replace("\uFEFF","").trim();
            if(line.equals("#SERVICE") || line.startsWith("#SERVICE ")){
                if(!address.isEmpty())out.append("#EXTINF:-1,").append(name.isEmpty()?URI.create(address).getHost():name).append('\n').append(address).append('\n');
                address="";name="";
                if(line.length()<10)continue;
                String[] fields=line.substring(9).split(":",11);if(fields.length!=11)continue;
                Matcher match=ENCODED.matcher(fields[10]);boolean encoded=match.matches();
                String value=fields[10];
                if(encoded){
                    String field=match.group(1);
                    try{value=field.toLowerCase(java.util.Locale.ROOT).contains("%3a%2f%2f")
                        ? java.net.URLDecoder.decode(field.replace("+","%2B"),"UTF-8") : field.replaceAll("(?i)%3a",":");}
                    catch(IllegalArgumentException e){continue;}
                }
                try{URI uri=new URI(value);if(uri.getHost()!=null && uri.getUserInfo()==null && ("https".equalsIgnoreCase(uri.getScheme()) || "http".equalsIgnoreCase(uri.getScheme()))){address=uri.toASCIIString();name=encoded && match.group(2)!=null?match.group(2).trim():"";}}catch(java.net.URISyntaxException ignored){}
            }else if(line.startsWith("#DESCRIPTION ") && !address.isEmpty())name=line.substring(13).trim();
        }
        if(out.indexOf("#EXTINF")<0)throw new IOException("Dreambox / Enigma2: no IPTV URLs / brak adresów IPTV. DVB wymaga tunera.");
        return out.toString();
    }
}
