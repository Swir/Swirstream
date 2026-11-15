package com.swir.stream;

import android.content.Context;
import org.json.JSONArray;
import org.json.JSONException;
import org.json.JSONObject;
import java.io.File;
import java.net.URI;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.util.ArrayList;
import java.util.Collections;
import java.util.Comparator;
import java.util.HashMap;
import java.util.IdentityHashMap;
import java.util.Iterator;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.regex.Pattern;

/** Local bounded preference and source-outcome learning. No assistant or network I/O. */
public final class PersonalAi {
    private static final long DAY=86_400_000L;
    private final File file;
    private final String profileId;
    private TasteNet model;
    private boolean enabled=true;
    private int preferenceCount,sourceCount;
    private long sourceUpdated;
    private double[] preferenceWeights=new double[512],sourceWeights=new double[512];
    private final Map<String,Observation> preferenceSignals=new HashMap<>(),sourceSignals=new HashMap<>();
    private final LinkedHashMap<String,String> categories=new LinkedHashMap<>();
    private String notice="";
    private static final class Observation {final long at;final boolean positive;Observation(long at,boolean positive){this.at=at;this.positive=positive;}}

    public PersonalAi(Context context,String profileId) {
        this.profileId=profileId;
        file=new File(ProfileStore.directory(context,profileId),"personal-ai.json");
        try{model=TasteNet.load(context);}catch(Exception e){notice="Nie wczytano modelu TasteNet; dostępne są tylko jawne kategorie źródeł.";}
        load();
    }
    public synchronized String notice(){return notice;}
    public synchronized boolean enabled(){return enabled;}
    public synchronized int preferenceCount(){return preferenceCount;}
    public synchronized int sourceCount(){return sourceCount;}
    public synchronized void setEnabled(boolean value){enabled=value;save();}
    public synchronized void reset(){preferenceWeights=new double[512];sourceWeights=new double[512];preferenceCount=sourceCount=0;sourceUpdated=0;preferenceSignals.clear();sourceSignals.clear();save();}
    private static String safe(String text){return text==null?"":text;}
    public synchronized String category(Station station) {
        if(station==null)return "unknown";String text=safe(station.name)+" "+safe(station.group);if(text.length()>4096)text=text.substring(0,4096);
        String cached=categories.get(text);if(cached!=null)return cached;
        String group=TasteNet.normalize(station.group),result="unknown";
        for(String topic:TasteNet.TOPICS)if(Pattern.compile("(?:^|[,;|/ ])"+topic+"(?:$|[,;|/ ])").matcher(group).find()){result=topic;break;}
        if("unknown".equals(result)&&model!=null){TasteNet.Prediction prediction=model.predict(text);if(prediction.accepted)result=prediction.label;}
        if(categories.size()>=2048)categories.clear();categories.put(text,result);return result;
    }
    private static String identity(Station station){return safe(station.key());}
    private static String domain(Station station){if(station.isProvider())return "provider:"+station.providerProfileId+":"+station.providerId;String address=PublicHttp.allowedDocument(station.homepage)?station.homepage:PublicHttp.allowedDocument(station.origin)?station.origin:station.url;try{String host=new URI(address).getHost();if(host==null)return "";host=host.toLowerCase(Locale.ROOT);return host.startsWith("www.")?host.substring(4):host;}catch(Exception ignored){return "";}}
    private double[] features(Station station,boolean availability) {
        double[] vector=new double[512];String topic=category(station);
        if(availability){add(vector,"site:"+domain(station),1);add(vector,"channel:"+identity(station),.8);add(vector,"country:"+safe(station.country),.3);add(vector,"topic:"+topic,.35);add(vector,"kind:"+safe(station.kind),.15);}
        else {add(vector,"topic:"+topic,1);add(vector,"country:"+safe(station.country),.5);add(vector,"kind:"+safe(station.kind),.25);add(vector,"channel:"+identity(station),.7);for(String word:TasteNet.words(station.name))if(word.length()>2&&!word.matches("^[0-9]+[pi]?$"))add(vector,"name:"+word,.2);}
        TasteNet.normalizeVector(vector);return vector;
    }
    private static void add(double[] vector,String feature,double amount){vector[TasteNet.bucket(feature,vector.length)]+=amount;}
    private static double clamp(double v,double min,double max){return Math.max(min,Math.min(max,v));}
    private static double probability(double[] features,double[] weights,double decay){double dot=0;for(int i=0;i<features.length;i++)dot+=features[i]*weights[i]*decay;return 1/(1+Math.exp(-clamp(dot,-20,20)));}
    private double sourceDecay(long now){return sourceUpdated<=0?0:Math.exp(-Math.max(0,(now-sourceUpdated)/(double)DAY)/30);}
    public synchronized double score(Station station) {
        return scoreAt(station,System.currentTimeMillis());
    }
    private double scoreAt(Station station,long when) {
        if(!enabled||station==null)return 0;double preference=preferenceCount==0?0:(probability(features(station,false),preferenceWeights,1)-.5)*24;
        double source=sourceCount==0?0:(probability(features(station,true),sourceWeights,sourceDecay(when))-.5)*24*Math.min(1,sourceCount/6d);
        return clamp(preference+source,-24,24);
    }
    /** Called for a real positive watch/favorite or explicit negative preference. */
    public synchronized void preference(Station station,boolean positive) {
        if(!enabled||!learnable(station))return;long now=System.currentTimeMillis();String id=digest(identity(station));Observation previous=preferenceSignals.get(id);
        if(previous!=null&&previous.positive==positive&&now-previous.at<DAY)return;
        double[] vector=features(station,false);double error=(positive?1:0)-probability(vector,preferenceWeights,1);
        for(int i=0;i<512;i++)preferenceWeights[i]=clamp(preferenceWeights[i]*.9995+.8*error*vector[i],-8,8);
        preferenceCount=Math.min(1_000_000,preferenceCount+1);preferenceSignals.put(id,new Observation(now,positive));prune(preferenceSignals,now);save();
    }
    /** Only actual player/decoder callbacks may call this; HTTP/M3U success is not proof. */
    public synchronized void playback(Station station,boolean success,long startupMs) {
        if(!enabled||!learnable(station)||startupMs<=0||startupMs>3_600_000)return;long now=System.currentTimeMillis();String id=digest(identity(station)+"|"+safe(station.sourceKey()));Observation previous=sourceSignals.get(id);
        if(previous!=null&&previous.positive==success&&now-previous.at<30*60_000L)return;
        double[] vector=features(station,true);double decay=sourceCount==0?1:sourceDecay(now);double error=(success?1:0)-probability(vector,sourceWeights,decay);
        for(int i=0;i<512;i++)sourceWeights[i]=clamp(sourceWeights[i]*decay*.9995+.55*error*vector[i],-6,6);
        sourceCount=Math.min(1_000_000,sourceCount+1);sourceUpdated=now;sourceSignals.put(id,new Observation(now,success));prune(sourceSignals,now);save();
    }
    private boolean learnable(Station station){return station!=null&&!"test".equalsIgnoreCase(station.kind)&&!identity(station).isEmpty()&&(station.isProvider()?station.validProviderReference()&&profileId.equals(station.providerProfileId):PublicHttp.allowedStream(station.url));}
    public synchronized void reorder(List<Station> stations) {
        long now=System.currentTimeMillis();final IdentityHashMap<Station,Double> scores=new IdentityHashMap<>();for(Station station:stations)scores.put(station,scoreAt(station,now));
        Collections.sort(stations,(a,b)->{if(a==b)return 0;if(a==null)return 1;if(b==null)return -1;int compared=Boolean.compare(b.freshManifest(),a.freshManifest());if(compared!=0)return compared;compared=Double.compare(scores.get(b),scores.get(a));if(compared!=0)return compared;compared=TasteNet.normalize(a.name).compareTo(TasteNet.normalize(b.name));if(compared!=0)return compared;compared=identity(a).compareTo(identity(b));return compared!=0?compared:safe(a.url).compareTo(safe(b.url));});
    }
    private static String digest(String text){try{byte[] bytes=MessageDigest.getInstance("SHA-256").digest(text.getBytes(StandardCharsets.UTF_8));StringBuilder hex=new StringBuilder(64);for(byte b:bytes){int n=b&255;hex.append(Character.forDigit(n>>>4,16)).append(Character.forDigit(n&15,16));}return hex.toString();}catch(Exception e){throw new IllegalStateException(e);}}
    private static void prune(Map<String,Observation> values,long now){Iterator<Map.Entry<String,Observation>> iterator=values.entrySet().iterator();while(iterator.hasNext())if(now-iterator.next().getValue().at>30*DAY)iterator.remove();if(values.size()>4000){List<Map.Entry<String,Observation>> oldest=new ArrayList<>(values.entrySet());Collections.sort(oldest,Comparator.comparingLong(e->e.getValue().at));for(int i=0;i<oldest.size()-4000;i++)values.remove(oldest.get(i).getKey());}}
    private void load() {
        if(!ProfileStore.hasSnapshot(file))return;
        try {
            JSONObject json=ProfileStore.readJson(file,2_000_000);if(json.getInt("schema")!=1)throw new JSONException("AI schema");
            double[] preference=readWeights(json.getJSONArray("preferenceWeights"),8),source=readWeights(json.getJSONArray("sourceWeights"),6);
            int pc=json.getInt("preferenceCount"),sc=json.getInt("sourceCount");long updated=json.getLong("sourceUpdated");if(pc<0||pc>1_000_000||sc<0||sc>1_000_000||updated<0||updated>System.currentTimeMillis()+60_000)throw new JSONException("AI counters");
            Map<String,Observation> ps=readSignals(json.getJSONObject("preferenceSignals")),ss=readSignals(json.getJSONObject("sourceSignals"));
            enabled=json.getBoolean("enabled");preferenceWeights=preference;sourceWeights=source;preferenceCount=pc;sourceCount=sc;sourceUpdated=updated;preferenceSignals.putAll(ps);sourceSignals.putAll(ss);
        }catch(Exception e){ProfileStore.quarantine(file);notice="Uszkodzoną naukę AI zachowano w pliku .damaged. Ten profil zaczyna bez wyuczonych ocen; biblioteka pozostaje.";}
    }
    private static double[] readWeights(JSONArray input,double max) throws JSONException {if(input.length()!=512)throw new JSONException("Weights shape");double[] output=new double[512];for(int i=0;i<512;i++){double v=input.getDouble(i);if(Double.isNaN(v)||Double.isInfinite(v)||Math.abs(v)>max)throw new JSONException("Weights range");output[i]=v;}return output;}
    private static Map<String,Observation> readSignals(JSONObject input) throws JSONException {if(input.length()>4000)throw new JSONException("Signals limit");Map<String,Observation> output=new HashMap<>();Iterator<String> keys=input.keys();long now=System.currentTimeMillis();while(keys.hasNext()){String key=keys.next();if(!key.matches("[a-f0-9]{64}"))throw new JSONException("Signal identity");JSONObject item=input.getJSONObject(key);long at=item.getLong("at");if(at<=0||at>now+60_000)throw new JSONException("Signal date");if(now-at<=30*DAY)output.put(key,new Observation(at,item.getBoolean("positive")));}return output;}
    private static JSONArray weights(double[] source) throws JSONException {JSONArray array=new JSONArray();for(double value:source)array.put(value);return array;}
    private static JSONObject signals(Map<String,Observation> source) throws JSONException {JSONObject object=new JSONObject();for(Map.Entry<String,Observation> entry:source.entrySet()){JSONObject item=new JSONObject();item.put("at",entry.getValue().at);item.put("positive",entry.getValue().positive);object.put(entry.getKey(),item);}return object;}
    private void save(){try{JSONObject json=new JSONObject();json.put("schema",1);json.put("enabled",enabled);json.put("preferenceCount",preferenceCount);json.put("sourceCount",sourceCount);json.put("sourceUpdated",sourceUpdated);json.put("preferenceWeights",weights(preferenceWeights));json.put("sourceWeights",weights(sourceWeights));json.put("preferenceSignals",signals(preferenceSignals));json.put("sourceSignals",signals(sourceSignals));ProfileStore.writeJson(file,json);}catch(Exception e){notice="Nauka działa w tej sesji, lecz nie udało się jej zapisać. Sprawdź wolne miejsce.";}}
}
