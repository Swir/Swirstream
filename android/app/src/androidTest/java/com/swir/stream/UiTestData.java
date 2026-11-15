package com.swir.stream;

import android.content.Context;
import org.json.JSONArray;
import org.json.JSONObject;
import java.io.*;
import java.nio.charset.StandardCharsets;
import java.util.Collections;
import java.util.HashSet;

/** Deterministic catalogue fixture; it never asserts that the example URLs play. */
final class UiTestData {
    private static byte[] savedCatalog;
    private static boolean hadCatalog;
    private static java.util.Set<String> savedCountries;
    private static String savedQuality;
    private static boolean seeded;
    static void seed(Context context)throws Exception{
        seeded=true;
        android.content.SharedPreferences preferences=context.getSharedPreferences("interface",Context.MODE_PRIVATE);savedCountries=new HashSet<>(preferences.getStringSet("countries",new HashSet<>(Collections.singletonList("PL"))));savedQuality=preferences.getString("quality","Stabilność");File old=new File(context.getFilesDir(),"catalog/catalog.json");hadCatalog=old.exists();savedCatalog=null;if(hadCatalog){try(InputStream in=new FileInputStream(old);ByteArrayOutputStream bytes=new ByteArrayOutputStream()){byte[] b=new byte[16384];int n;while((n=in.read(b))!=-1)bytes.write(b,0,n);savedCatalog=bytes.toByteArray();}}
        context.getSharedPreferences("interface",Context.MODE_PRIVATE).edit().putStringSet("countries",new HashSet<>(Collections.singletonList("PL"))).putString("quality","Stabilność").commit();
        JSONArray rows=new JSONArray();for(int i=0;i<4;i++){Station s=new Station();s.id="fixture-"+i;s.name=i==0?"Kanał testowy sport":i==1?"TVP 1":i==2?"TVP 10":"Kanał testowy radio";s.aliases=i==0?new String[]{"AliasSport"}:new String[0];s.country="PL";s.kind=i==3?"radio":"tv";s.url="https://example.org/fixture-"+i+".m3u8";s.group=i==0?"sports":"news";s.fetchedAt=System.currentTimeMillis();rows.put(s.toJson());}
        JSONObject document=new JSONObject().put("schema",1).put("dataAt",System.currentTimeMillis()).put("savedAt",System.currentTimeMillis()).put("mixed",true).put("radio",false).put("countries",new JSONArray().put("PL")).put("stations",rows);
        File folder=new File(context.getFilesDir(),"catalog");folder.mkdirs();try(OutputStream out=new FileOutputStream(new File(folder,"catalog.json"))){out.write(document.toString().getBytes(StandardCharsets.UTF_8));}
    }
    static void restore(Context context)throws Exception{if(!seeded)return;seeded=false;File file=new File(context.getFilesDir(),"catalog/catalog.json");if(hadCatalog&&savedCatalog!=null){try(OutputStream out=new FileOutputStream(file)){out.write(savedCatalog);}}else if(file.exists()&&!file.delete())throw new IOException("Cannot remove test catalogue");if(savedCountries!=null)context.getSharedPreferences("interface",Context.MODE_PRIVATE).edit().putStringSet("countries",savedCountries).putString("quality",savedQuality).commit();}
}
