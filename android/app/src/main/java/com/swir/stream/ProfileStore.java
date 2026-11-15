package com.swir.stream;

import android.content.Context;
import android.util.AtomicFile;
import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;
import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Locale;
import java.util.UUID;

/** Private, local profiles; these are not authenticated user accounts. */
public final class ProfileStore {
    private final Context context;
    private final File root,index;
    private final LinkedHashMap<String,String> names=new LinkedHashMap<>();
    private final LinkedHashMap<String,Station> favoriteStations=new LinkedHashMap<>();
    private final List<Visit> visits=new ArrayList<>();
    private String active="default",notice="";
    private static final int MAX_PROFILES=20,MAX_FAVORITES=1000,MAX_HISTORY=200;
    private static final class Visit {final Station station;final long at;Visit(Station s,long at){station=s;this.at=at;}}

    public ProfileStore(Context context) {
        this.context=context;root=new File(context.getFilesDir(),"profiles");index=new File(root,"index.json");root.mkdirs();
        boolean loaded=false;
        try{if(hasSnapshot(index)){loadIndex(readJson(index,64_000));loaded=true;}}
        catch(Exception e){quarantine(index);notice="Odtworzono listę profili. Zachowano pliki bibliotek.";}
        if(!loaded){recoverProfiles();saveIndex();}
        for(String id:names.keySet())saveMetadata(id,names.get(id));
        loadLibrary();
    }
    private void loadIndex(JSONObject value) throws JSONException {
        if(value.getInt("schema")!=1)throw new JSONException("Index schema");
        JSONArray list=value.getJSONArray("profiles");if(list.length()<1||list.length()>MAX_PROFILES)throw new JSONException("Profile count");
        LinkedHashMap<String,String> parsed=new LinkedHashMap<>();
        for(int i=0;i<list.length();i++){JSONObject p=list.getJSONObject(i);String id=p.getString("id"),name=validatedName(p.getString("name"));if(!validId(id)||parsed.containsKey(id)||hasName(parsed,name))throw new JSONException("Profile identity");parsed.put(id,name);}
        String selected=value.getString("active");if(!parsed.containsKey(selected))throw new JSONException("Active profile");
        names.clear();names.putAll(parsed);active=selected;
    }
    private void recoverProfiles() {
        names.clear();names.put("default","Mój profil");active="default";
        File[] directories=root.listFiles();if(directories==null)return;
        java.util.Arrays.sort(directories,(a,b)->a.getName().compareTo(b.getName()));
        for(File dir:directories){if(names.size()>=MAX_PROFILES)break;if(!dir.isDirectory()||!validId(dir.getName()))continue;
            try{JSONObject p=readJson(new File(dir,"profile.json"),8000);String name=validatedName(p.getString("name"));if(!dir.getName().equals(p.getString("id")))continue;
                if("default".equals(dir.getName()))names.put("default",name);else if(!hasName(names,name))names.put(dir.getName(),name);
            }catch(Exception ignored){}
        }
    }
    private static boolean hasName(LinkedHashMap<String,String> map,String name){for(String n:map.values())if(n.equalsIgnoreCase(name))return true;return false;}
    private static String validatedName(String value){String name=value==null?"":value.trim();if(name.length()<1||name.length()>40||java.util.regex.Pattern.compile("\\p{Cntrl}").matcher(name).find())throw new IllegalArgumentException("Nazwa profilu: 1–40 znaków.");return name;}
    static boolean validId(String id){return id!=null&&id.matches("(?:default|[a-f0-9]{32})");}
    static File directory(Context context,String id){if(!validId(id))throw new IllegalArgumentException("Nieprawidłowy identyfikator profilu.");File dir=new File(new File(context.getFilesDir(),"profiles"),id);dir.mkdirs();return dir;}
    public synchronized List<String> profiles(){return new ArrayList<>(names.values());}
    public synchronized String current(){return names.get(active);}
    public synchronized String currentId(){return active;}
    public synchronized String notice(){return notice;}
    public synchronized void select(String name){for(String id:names.keySet())if(names.get(id).equals(name)){active=id;saveIndex();loadLibrary();return;}throw new IllegalArgumentException("Nie znaleziono profilu.");}
    public synchronized void add(String value){String name=validatedName(value);if(names.size()>=MAX_PROFILES)throw new IllegalArgumentException("Maksymalnie 20 profili.");if(hasName(names,name))throw new IllegalArgumentException("Taki profil już istnieje.");String id=UUID.randomUUID().toString().replace("-","").toLowerCase(Locale.ROOT);saveMetadata(id,name);names.put(id,name);active=id;saveIndex();loadLibrary();}
    private void saveMetadata(String id,String name){try{JSONObject p=new JSONObject();p.put("id",id);p.put("name",name);writeJson(new File(directory(context,id),"profile.json"),p);}catch(Exception e){notice="Nie zapisano metadanych profilu. Sprawdź wolne miejsce.";}}
    private void saveIndex(){try{JSONObject p=new JSONObject();p.put("schema",1);p.put("active",active);JSONArray list=new JSONArray();for(String id:names.keySet()){JSONObject item=new JSONObject();item.put("id",id);item.put("name",names.get(id));list.put(item);}p.put("profiles",list);writeJson(index,p);}catch(Exception e){notice="Nie zapisano listy profili. Sprawdź wolne miejsce.";}}
    private File libraryFile(){return new File(directory(context,active),"library.json");}
    private void loadLibrary() {
        favoriteStations.clear();visits.clear();File path=libraryFile();if(!hasSnapshot(path))return;
        try {
            JSONObject json=readJson(path,4_000_000);if(json.getInt("schema")!=1)throw new JSONException("Library schema");
            JSONArray fav=json.getJSONArray("favorites"),history=json.getJSONArray("history");if(fav.length()>MAX_FAVORITES||history.length()>MAX_HISTORY)throw new JSONException("Library limit");
            for(int i=0;i<fav.length();i++){Station station=parseStation(fav.optJSONObject(i));if(station!=null)favoriteStations.put(station.key(),station);}
            java.util.HashSet<String> seen=new java.util.HashSet<>();
            for(int i=0;i<history.length();i++){JSONObject item=history.optJSONObject(i);if(item==null)continue;Station station=parseStation(item.optJSONObject("station"));long at=item.optLong("at",0);if(station!=null&&at>0&&at<=System.currentTimeMillis()+60_000&&seen.add(station.key()))visits.add(new Visit(station,at));}
        }catch(Exception e){favoriteStations.clear();visits.clear();quarantine(path);notice="Uszkodzony zapis biblioteki zachowano w pliku .damaged. Inne profile są bez zmian.";}
    }
    static Station copy(Station station){if(station==null)return null;try{return parseStation(station.toJson());}catch(Exception e){return null;}}
    private static Station parseStation(JSONObject json){if(json==null)return null;try{Station s=Station.fromJson(json);return s!=null&&s.name!=null&&!s.name.trim().isEmpty()&&s.name.length()<=500&&s.url!=null&&!s.url.isEmpty()&&s.url.length()<=8192&&s.key()!=null&&!s.key().isEmpty()?s:null;}catch(Exception e){return null;}}
    public synchronized boolean favorite(Station station){return station!=null&&favoriteStations.containsKey(station.key());}
    public synchronized void setFavorite(Station station,boolean value){Station copy=copy(station);if(copy==null)return;if(value){if(!favoriteStations.containsKey(copy.key())&&favoriteStations.size()>=MAX_FAVORITES)throw new IllegalStateException("Maksymalnie 1000 ulubionych.");favoriteStations.put(copy.key(),copy);}else favoriteStations.remove(copy.key());saveLibrary();}
    public synchronized void watched(Station station){Station copy=copy(station);if(copy==null)return;for(int i=visits.size()-1;i>=0;i--)if(visits.get(i).station.key().equals(copy.key()))visits.remove(i);visits.add(0,new Visit(copy,System.currentTimeMillis()));while(visits.size()>MAX_HISTORY)visits.remove(visits.size()-1);saveLibrary();}
    public synchronized List<Station> favorites(){List<Station> list=new ArrayList<>();for(Station s:favoriteStations.values()){Station c=copy(s);if(c!=null)list.add(c);}return list;}
    public synchronized List<Station> history(){List<Station> list=new ArrayList<>();for(Visit visit:visits){Station c=copy(visit.station);if(c!=null)list.add(c);}return list;}
    private void saveLibrary(){try{JSONObject json=new JSONObject();json.put("schema",1);JSONArray fav=new JSONArray(),history=new JSONArray();for(Station s:favoriteStations.values())fav.put(s.toJson());for(Visit v:visits){JSONObject visit=new JSONObject();visit.put("station",v.station.toJson());visit.put("at",v.at);history.put(visit);}json.put("favorites",fav);json.put("history",history);if(json.toString().getBytes(StandardCharsets.UTF_8).length>4_000_000)throw new IOException("Library size limit");writeJson(libraryFile(),json);}catch(Exception e){notice="Zmiany biblioteki działają w tej sesji, lecz nie udało się ich zapisać.";}}

    // API 23 AtomicFile can leave only .bak after a crash before recreating base.
    static boolean hasSnapshot(File file){return file.exists()||new File(file.getPath()+".bak").exists();}

    static JSONObject readJson(File file,int maxBytes) throws IOException,JSONException {
        if(file.length()>maxBytes)throw new IOException("Zapis przekracza limit.");
        try(FileInputStream input=new AtomicFile(file).openRead();ByteArrayOutputStream bytes=new ByteArrayOutputStream()){
            byte[] block=new byte[8192];int n;while((n=input.read(block))!=-1){if(bytes.size()+n>maxBytes)throw new IOException("Zapis przekracza limit.");bytes.write(block,0,n);}return new JSONObject(new String(bytes.toByteArray(),StandardCharsets.UTF_8));
        }
    }
    static void writeJson(File file,JSONObject value) throws IOException {
        File parent=file.getParentFile();if(parent!=null&&!parent.isDirectory()&&!parent.mkdirs())throw new IOException("Nie utworzono folderu.");
        AtomicFile atomic=new AtomicFile(file);FileOutputStream output=null;
        try{output=atomic.startWrite();output.write(value.toString().getBytes(StandardCharsets.UTF_8));atomic.finishWrite(output);}
        catch(IOException|RuntimeException e){if(output!=null)atomic.failWrite(output);throw e;}
    }
    static void quarantine(File file){if(file.exists())file.renameTo(new File(file.getParentFile(),file.getName()+".damaged-"+UUID.randomUUID().toString()));}
}
