package com.swir.stream;

import java.io.*;
import java.net.*;
import java.nio.ByteBuffer;
import java.nio.charset.*;
import java.util.*;
import java.util.regex.*;

/** Transient user input only. Never persist or stringify entries containing MACs. */
@androidx.annotation.OptIn(markerClass=androidx.media3.common.util.UnstableApi.class)
final class PortalTxt {
    static final int MAX_BYTES=1_048_576,MAX_ENTRIES=200,MAX_ERRORS=50;
    static final class Entry {
        final String url,mac;final int line;
        Entry(String url,String mac,int line){this.url=url;this.mac=mac;this.line=line;}
        boolean account(){return !mac.isEmpty();}
        String label(int index){return "Portal "+(index+1)+" • wiersz "+line+(account()?" • własny MAC":" • sam adres");}
    }
    static final class Import {
        final List<Entry> entries;final List<Integer> invalidLines;final int invalidCount,duplicates;
        Import(List<Entry> entries,List<Integer> invalidLines,int invalidCount,int duplicates){this.entries=Collections.unmodifiableList(new ArrayList<>(entries));this.invalidLines=Collections.unmodifiableList(new ArrayList<>(invalidLines));this.invalidCount=invalidCount;this.duplicates=duplicates;}
    }
    private static final Pattern MAC=Pattern.compile("^((?:[0-9a-fA-F]{2}:){5}[0-9a-fA-F]{2})(?:\\s+(?:-\\s+.*|#.*))?$");
    static Import read(InputStream input)throws IOException {
        if(input==null)throw new IOException("Nie można odczytać pliku TXT.");
        ByteArrayOutputStream out=new ByteArrayOutputStream();byte[] buffer=new byte[8192];int count;
        while((count=input.read(buffer))!=-1){if(out.size()+count>MAX_BYTES)throw new IOException("Plik TXT przekracza 1 MiB. Podziel go na mniejsze pliki.");out.write(buffer,0,count);}
        return parse(out.toByteArray());
    }
    static Import parse(byte[] bytes)throws IOException {
        if(bytes.length>MAX_BYTES)throw new IOException("Plik TXT przekracza 1 MiB. Podziel go na mniejsze pliki.");
        if(bytes.length>=4&&((bytes[0]==-1&&bytes[1]==-2&&bytes[2]==0&&bytes[3]==0)||(bytes[0]==0&&bytes[1]==0&&bytes[2]==-2&&bytes[3]==-1)))throw new IOException("Nieobsługiwane kodowanie TXT.");
        Charset encoding=StandardCharsets.UTF_8;int offset=0;
        if(bytes.length>=3&&(bytes[0]&255)==239&&(bytes[1]&255)==187&&(bytes[2]&255)==191)offset=3;
        else if(bytes.length>=2&&(bytes[0]&255)==255&&(bytes[1]&255)==254){encoding=StandardCharsets.UTF_16LE;offset=2;}
        else if(bytes.length>=2&&(bytes[0]&255)==254&&(bytes[1]&255)==255){encoding=StandardCharsets.UTF_16BE;offset=2;}
        String text;try{text=encoding.newDecoder().onMalformedInput(CodingErrorAction.REPORT).onUnmappableCharacter(CodingErrorAction.REPORT).decode(ByteBuffer.wrap(bytes,offset,bytes.length-offset)).toString();}
        catch(CharacterCodingException bad){throw new IOException("Nieprawidłowe kodowanie TXT. Użyj UTF-8 lub UTF-16 z BOM.");}
        if(text.indexOf('\0')>=0)throw new IOException("Nieprawidłowe kodowanie TXT.");
        LinkedHashMap<String,Entry> unique=new LinkedHashMap<>();List<Integer> errors=new ArrayList<>();int invalid=0,duplicates=0,lineNo=0,pendingLine=0;String pending=null;boolean paired=false;
        try(BufferedReader reader=new BufferedReader(new StringReader(text))){String raw;while((raw=reader.readLine())!=null){lineNo++;String line=raw.trim();if(line.isEmpty()||line.startsWith("#")||line.startsWith(";"))continue;
            try {
                if(line.length()>10000)throw new IOException();
                Matcher match=MAC.matcher(line);
                if(match.matches()){
                    if(pending==null)throw new IOException();String mac=ProviderAdapters.mac(match.group(1));paired=true;duplicates+=add(unique,new Entry(pending,mac,lineNo));continue;
                }
                if(pending!=null&&!paired)duplicates+=add(unique,new Entry(pending,"",pendingLine));pending=null;paired=false;
                String[] parts=line.split("\\|",-1);
                if(parts.length==1){pending=address(line);pendingLine=lineNo;}
                else if(parts.length==2){Matcher own=MAC.matcher(parts[1].trim());if(!own.matches())throw new IOException();String url=address(parts[0].trim()),mac=ProviderAdapters.mac(own.group(1));duplicates+=add(unique,new Entry(url,mac,lineNo));pending=url;pendingLine=lineNo;paired=true;}
                else throw new IOException();
            }catch(LimitException limit){throw limit;}catch(Exception bad){if(pending!=null&&!paired)duplicates+=add(unique,new Entry(pending,"",pendingLine));pending=null;paired=false;invalid++;if(errors.size()<MAX_ERRORS)errors.add(lineNo);}
        }}
        if(pending!=null&&!paired)duplicates+=add(unique,new Entry(pending,"",pendingLine));
        return new Import(new ArrayList<>(unique.values()),errors,invalid,duplicates);
    }
    private static final class LimitException extends IOException {LimitException(){super("TXT zawiera więcej niż 200 unikalnych pozycji. Podziel plik; nic nie zostało uruchomione.");}}
    private static int add(Map<String,Entry> unique,Entry entry)throws IOException{String key=dedup(entry.url)+"|"+entry.mac;if(unique.containsKey(key))return 1;if(unique.size()>=MAX_ENTRIES)throw new LimitException();unique.put(key,entry);return 0;}
    static String address(String raw)throws IOException {
        if(raw==null||raw.length()>8192||raw.matches("(?s).*[\\p{Cntrl}].*"))throw new IOException("Nieprawidłowy adres portalu.");
        URI uri=ProviderHttp.checkedUri(raw);if(uri.getRawQuery()!=null||uri.getRawUserInfo()!=null||uri.getRawFragment()!=null)throw new IOException("Nieprawidłowy adres portalu.");
        String decoded=raw;for(int i=0;i<3;i++){Matcher encoded=Pattern.compile("%([0-9a-fA-F]{2})").matcher(decoded);StringBuffer next=new StringBuffer();while(encoded.find())encoded.appendReplacement(next,Matcher.quoteReplacement(String.valueOf((char)Integer.parseInt(encoded.group(1),16))));encoded.appendTail(next);decoded=next.toString();if(decoded.matches("(?s).*[\\p{Cntrl}].*"))throw new IOException("Nieprawidłowy adres portalu.");}
        String host=uri.getHost().toLowerCase(Locale.ROOT);if(host.endsWith("."))host=host.substring(0,host.length()-1);
        String literal=host.replace("[","").replace("]","");
        if(literal.contains(":")||literal.matches("[0-9.]+")){try{if(!literal.contains(":")&&!literal.matches("(?:[0-9]{1,3}\\.){3}[0-9]{1,3}"))throw new IOException();if(!ProviderDataSource.publicAddress(InetAddress.getByName(literal)))throw new IOException();}catch(Exception bad){throw new IOException("Nieprawidłowy adres portalu.");}}
        if(host.contains(":")&&!host.startsWith("["))host="["+host+"]";
        String scheme=uri.getScheme().toLowerCase(Locale.ROOT);int port=uri.getPort();String path=uri.getRawPath();
        return scheme+"://"+host+(port<0||scheme.equals("http")&&port==80||scheme.equals("https")&&port==443?"":":"+port)+(path==null||path.isEmpty()?"/":path);
    }
    private static String dedup(String url){return url.replaceFirst("/+$","");}
    private PortalTxt(){}
}
