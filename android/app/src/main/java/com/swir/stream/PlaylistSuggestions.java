package com.swir.stream;

import java.util.ArrayList;
import java.util.Collections;
import java.util.Comparator;
import java.util.List;
import java.util.Set;
import java.util.function.ToDoubleFunction;

/** Query/country matching and existing local preference scores; no new credentials or feeds. */
final class PlaylistSuggestions {
    static final class Entry {
        final CatalogRepository.FoundPlaylist playlist;final int matches,total;final double preference;
        Entry(CatalogRepository.FoundPlaylist playlist,int matches,int total,double preference){this.playlist=playlist;this.matches=matches;this.total=total;this.preference=preference;}
    }
    static Entry match(CatalogRepository.FoundPlaylist playlist,List<Station> stations,String query,Set<String> countries,ToDoubleFunction<Station> score){
        if(playlist==null||(SourcePrivacy.publicCredentials(playlist.source)||SourcePrivacy.publicCredentials(playlist.text)))return null;
        List<Station> matching=CatalogRepository.select(stations,countries,query);if(matching.isEmpty())return null;
        java.util.Map<String,Double> channels=new java.util.HashMap<>();for(Station station:matching){double value=score.applyAsDouble(station);if(!Double.isFinite(value))value=0;channels.put(station.key(),Math.max(channels.getOrDefault(station.key(),-Double.MAX_VALUE),value));}
        double total=0;for(double value:channels.values())total+=value;
        return new Entry(playlist,channels.size(),stations.size(),total/channels.size());
    }
    static List<Entry> rank(List<Entry> entries){List<Entry> ranked=new ArrayList<>(entries);Collections.sort(ranked,Comparator.comparing((Entry e)->e.playlist.stale()).thenComparing(Comparator.comparingDouble((Entry e)->e.preference).reversed()).thenComparing(Comparator.comparingInt((Entry e)->e.matches).reversed()).thenComparing(e->e.playlist.source));return ranked;}
}
