package com.swir.stream;

import java.util.*;

final class RegionalPlaylists {
    static final String KODINERDS="https://raw.githubusercontent.com/jnk22/kodinerds-iptv/master/iptv/clean/clean_tv.m3u";
    static final String TDT="https://www.tdtchannels.com/lists/tv.m3u8";
    private static final Set<String> SPANISH_REGIONS=new HashSet<>(Arrays.asList("Andalucía","Cataluña","C. Valenciana","Canarias","País Vasco","Galicia","Castilla-La Mancha","C. de Madrid","Castilla y León","R. de Murcia","Cantabria","C. Foral de Navarra","Extremadura","La Rioja","Illes Balears","Melilla","P. de Asturias","Aragón","Ceuta","Islas Baleares"));
    static List<String[]> seeds(Set<String> countries) {
        String[] es={"tdtchannels",TDT}, de={"kodinerds",KODINERDS};
        return countries.isEmpty() || countries.contains("ES") ? Arrays.asList(es,de) : Arrays.asList(de,es);
    }
    static String country(String url,String group,String original){
        if(KODINERDS.equals(url) && group.startsWith("IPTV-") && group.length()==7){
            String code=WorldMarkets.canonical(group.substring(5));
            if(!WorldMarkets.name(code).equals(code))return code;
        }
        if(TDT.equals(url)){
            if(SPANISH_REGIONS.contains(group))return "ES";
            if("TV".equals(original))return "";
        }
        return original;
    }
}
