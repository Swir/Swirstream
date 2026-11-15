package com.swir.stream;

import android.util.JsonReader;
import android.util.JsonToken;
import java.io.IOException;
import java.io.StringReader;
import java.util.ArrayList;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Collections;

/** Structured source index includes alternatives absent from published M3U files. */
final class StreamIndex {
    static List<Station> read(PublicHttp.Document doc,CatalogRepository.DirectoryIndex directory)throws IOException{
        List<Station> result=new ArrayList<>();int valid=0,records=0;
        try(JsonReader reader=new JsonReader(new StringReader(doc.text))){
            reader.setLenient(false);reader.beginArray();
            while(reader.hasNext()){
                if(++records>60000)throw new IOException("Za duży indeks transmisji.");
                if(reader.peek()!=JsonToken.BEGIN_OBJECT){reader.skipValue();continue;}
                String channel="",feed="",title="",url="",agent="",referrer="";reader.beginObject();
                while(reader.hasNext()){String key=reader.nextName();switch(key){
                    case "channel":channel=CatalogRepository.readString(reader);break;
                    case "feed":feed=CatalogRepository.readString(reader);break;
                    case "title":title=CatalogRepository.readString(reader);break;
                    case "url":url=CatalogRepository.readString(reader);break;
                    case "user_agent":agent=CatalogRepository.readString(reader);break;
                    case "referrer":referrer=CatalogRepository.readString(reader);break;
                    default:reader.skipValue();}}
                reader.endObject();if(!PublicHttp.allowedStream(url))continue;if(!channel.isEmpty())valid++;
                Station meta=directory==null?null:directory.ids.get(channel);if(meta==null)continue;
                Station station=WebCrawler.copy(meta);station.id=station.tvgId=channel;station.feed=feed;station.url=url;
                if(!title.isEmpty()){station.name=title;LinkedHashSet<String> aliases=new LinkedHashSet<>();aliases.add(meta.name);Collections.addAll(aliases,meta.aliases);station.aliases=aliases.toArray(new String[0]);}
                station.origin=doc.url;station.userAgent=M3u.clean(agent);station.referrer=PublicHttp.allowedDocument(referrer)?referrer:"";
                station.fetchedAt=doc.fetchedAt;station.modifiedAt=doc.modifiedAt;result.add(station);
            }
            reader.endArray();if(reader.peek()!=JsonToken.END_DOCUMENT)throw new IOException("Dane po indeksie transmisji.");
        }catch(IllegalStateException e){throw new IOException("Zmieniony schemat indeksu transmisji.",e);}
        if(valid==0)throw new IOException("Indeks nie zawiera poprawnych transmisji.");return result;
    }
}
