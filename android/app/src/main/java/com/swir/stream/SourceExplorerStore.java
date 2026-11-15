package com.swir.stream;

import android.content.Context;
import android.util.AtomicFile;
import org.json.JSONArray;
import org.json.JSONObject;
import java.io.File;
import java.io.FileOutputStream;
import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.concurrent.atomic.AtomicBoolean;

/** Per-profile public candidates, separate from playable library and encrypted accounts. */
final class SourceExplorerStore {
    private final File file;final String profile;private volatile boolean unreadable;
    SourceExplorerStore(Context context,String profile){this(new File(context.getFilesDir(),"profiles"),profile);}
    SourceExplorerStore(File root,String profile){if(profile==null||!profile.matches("default|[a-f0-9]{32}"))throw new IllegalArgumentException("Nieprawidłowy profil.");this.profile=profile;file=new File(new File(root,profile),"source-explorer.json");}
    synchronized List<SourceCandidate> load(){try{List<SourceCandidate> rows=read();unreadable=false;return rows;}catch(IOException error){unreadable=true;return new ArrayList<>();}}
    boolean unreadable(){return unreadable;}
    private List<SourceCandidate> read()throws IOException{List<SourceCandidate> rows=new ArrayList<>();if(!file.exists()&&!new File(file+".bak").exists())return rows;try{JSONObject saved=CatalogRepository.readJson(file,2_000_000);if(saved.optInt("schema")!=1||!profile.equals(saved.optString("profile")))throw new IOException();JSONArray entries=saved.getJSONArray("candidates");if(entries.length()>100)throw new IOException();for(int i=0;i<entries.length();i++)rows.add(SourceCandidate.read(entries.getJSONObject(i)));return rows;}catch(Exception error){throw new IOException("Nie można odczytać zapisu eksploratora. Zachowano plik; przed ponownym zapisem możesz świadomie wyczyścić zapisane wyniki.");}}
    synchronized List<SourceCandidate> merge(List<SourceCandidate> incoming,AtomicBoolean cancel)throws IOException{
        PublicHttp.checkCancelled(cancel);Map<String,SourceCandidate> all=new LinkedHashMap<>();for(SourceCandidate candidate:read())all.put(candidate.id,candidate);for(SourceCandidate candidate:incoming){SourceCandidate old=all.get(candidate.id);if(old==null||candidate.checkedAt>old.checkedAt||candidate.checkedAt==old.checkedAt&&candidate.discoveredAt>=old.discoveredAt)all.put(candidate.id,old==null?candidate:new SourceCandidate(candidate.url,candidate.title,candidate.evidenceUrl,candidate.kind,candidate.status,Math.min(old.discoveredAt,candidate.discoveredAt),candidate.checkedAt,candidate.modifiedAt,candidate.channelCount,candidate.protectedMedia));}
        List<SourceCandidate> result=new ArrayList<>(all.values());result.sort((a,b)->Long.compare(Math.max(b.checkedAt,b.discoveredAt),Math.max(a.checkedAt,a.discoveredAt)));if(result.size()>100)result=new ArrayList<>(result.subList(0,100));write(result,cancel);return result;
    }
    synchronized void remove(String id)throws IOException{List<SourceCandidate> rows=read();rows.removeIf(row->row.id.equals(id));write(rows,new AtomicBoolean());}
    synchronized void clear()throws IOException{write(new ArrayList<>(),new AtomicBoolean());unreadable=false;}
    private void write(List<SourceCandidate> rows,AtomicBoolean cancel)throws IOException{
        AtomicFile target=new AtomicFile(file);FileOutputStream output=null;try{JSONArray entries=new JSONArray();for(SourceCandidate candidate:rows)entries.put(candidate.json());byte[] bytes=new JSONObject().put("schema",1).put("profile",profile).put("candidates",entries).toString().getBytes(StandardCharsets.UTF_8);PublicHttp.checkCancelled(cancel);File parent=file.getParentFile();if(parent!=null&&!parent.isDirectory()&&!parent.mkdirs())throw new IOException();output=target.startWrite();output.write(bytes);PublicHttp.checkCancelled(cancel);target.finishWrite(output);output=null;}catch(Exception error){if(output!=null)target.failWrite(output);throw new IOException(cancel.get()?"Anulowano. Poprzednie wyniki pozostały zapisane.":"Nie udało się zapisać wyników eksploratora. Poprzedni zapis zachowano.");}
    }
}
