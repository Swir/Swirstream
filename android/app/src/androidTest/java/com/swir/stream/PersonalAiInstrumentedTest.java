package com.swir.stream;

import android.content.Context;
import android.content.ContextWrapper;
import androidx.test.ext.junit.runners.AndroidJUnit4;
import androidx.test.platform.app.InstrumentationRegistry;
import org.json.JSONArray;
import org.json.JSONObject;
import org.junit.Before;
import org.junit.Test;
import org.junit.runner.RunWith;
import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.FileOutputStream;
import java.io.InputStream;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.Collections;
import java.util.HashSet;
import java.util.List;
import java.util.Locale;
import java.util.UUID;
import static org.junit.Assert.*;

@RunWith(AndroidJUnit4.class)
public class PersonalAiInstrumentedTest {
    private Context context;
    private File root;
    @Before public void isolatedStorage(){Context target=InstrumentationRegistry.getInstrumentation().getTargetContext();root=new File(target.getCacheDir(),"ai-regression-"+UUID.randomUUID());assertTrue(root.mkdirs());context=new ContextWrapper(target){@Override public File getFilesDir(){return root;}};}
    private static Station station(String id,String country,String group,String host){Station station=new Station();station.id=id;station.tvgId=id;station.name="Station "+id;station.country=country;station.kind="tv";station.group=group;station.url="https://"+host+"/"+id+".m3u8";station.homepage="https://"+host+"/";return station;}
    private static byte[] read(InputStream input)throws Exception{try(InputStream in=input;ByteArrayOutputStream bytes=new ByteArrayOutputStream()){byte[] buffer=new byte[8192];int n;while((n=in.read(buffer))!=-1)bytes.write(buffer,0,n);return bytes.toByteArray();}}
    private static String hex(byte[] bytes){StringBuilder text=new StringBuilder();for(byte b:bytes)text.append(String.format(Locale.ROOT,"%02X",b&255));return text.toString();}
    private void write(File file,String text)throws Exception{try(FileOutputStream stream=new FileOutputStream(file)){stream.write(text.getBytes(StandardCharsets.UTF_8));}}

    @Test public void exactWindowsWeightsAndNumericInferenceParity()throws Exception {
        JSONObject expected=new JSONObject(new String(read(InstrumentationRegistry.getInstrumentation().getContext().getAssets().open("tastenet-parity.json")),StandardCharsets.UTF_8));
        byte[] actual=read(context.getAssets().open("model/tastenet.json"));assertEquals(expected.getString("modelSha256"),hex(MessageDigest.getInstance("SHA-256").digest(actual)));
        TasteNet net=TasteNet.load(context);JSONArray rows=expected.getJSONArray("rows");assertTrue(rows.length()>=48);
        for(int i=0;i<rows.length();i++){
            JSONObject row=rows.getJSONObject(i),prediction=row.getJSONObject("prediction");String text=row.getString("text");
            assertEquals("Invariant normalization: "+text,row.getString("normalized"),TasteNet.normalize(text));
            double[] vector=new double[1024];JSONArray features=row.getJSONArray("features");for(int j=0;j<features.length();j++){JSONObject feature=features.getJSONObject(j);vector[feature.getInt("i")]=feature.getDouble("v");}
            assertArrayEquals("Hashed features: "+text,vector,net.features(text),1e-12);
            TasteNet.Prediction output=net.predict(text);assertEquals(text,prediction.getString("Intent"),output.label);assertEquals(text,prediction.getDouble("Confidence"),output.confidence,1e-10);assertEquals(text,prediction.getDouble("Margin"),output.margin,1e-10);assertEquals(text,prediction.getBoolean("Accepted"),output.accepted);
        }
    }
    @Test public void explicitCategoriesAndUnknownWordsDoNotInventConfidence(){PersonalAi ai=new PersonalAi(context,"default");Station s=station("a","PL","sports","sport.example");s.name="World news";assertEquals("sports",ai.category(s));s.group="";s.name="qzxvvv999";assertEquals("unknown",ai.category(s));assertEquals(0,ai.score(s),0);ai.preference(s,true);assertEquals("unknown",ai.category(s));}

    @Test public void preferenceDeduplicationOppositeFeedbackAndBounds(){PersonalAi ai=new PersonalAi(context,"default");Station good=station("sport","PL","sports","sport.example"),other=station("music","DE","music","music.example");ai.preference(good,true);double first=ai.score(good);for(int i=0;i<25;i++)ai.preference(good,true);assertEquals(1,ai.preferenceCount());assertEquals(first,ai.score(good),1e-12);assertTrue(ai.score(good)>ai.score(other));ai.preference(good,false);assertEquals(2,ai.preferenceCount());assertTrue(ai.score(good)<first);for(int i=0;i<120;i++)ai.preference(station("s"+i,"PL","sports","sport.example"),true);assertTrue(ai.score(good)<=24&&ai.score(good)>=-24);assertEquals(ai.preferenceCount(),new PersonalAi(context,"default").preferenceCount());}

    @Test public void playbackOutcomesLearnAndPersistWithoutRawUrls()throws Exception {
        PersonalAi ai=new PersonalAi(context,"default");Station good=station("checkgood","PL","news","good.example"),bad=station("checkbad","PL","news","bad.example");
        for(int i=0;i<10;i++){ai.playback(station("good"+i,"PL","news","good.example"),true,400);ai.playback(station("bad"+i,"PL","news","bad.example"),false,8000);}
        assertEquals(20,ai.sourceCount());assertTrue(ai.score(good)>ai.score(bad));PersonalAi restarted=new PersonalAi(context,"default");assertEquals(20,restarted.sourceCount());assertEquals(ai.score(good),restarted.score(good),1e-6);
        String saved=new String(read(new java.io.FileInputStream(new File(ProfileStore.directory(context,"default"),"personal-ai.json"))),StandardCharsets.UTF_8);assertFalse(saved.contains("https://"));assertFalse(saved.contains("good.example"));
        ai.playback(good,true,300);ai.playback(good,true,350);assertEquals(21,ai.sourceCount());ai.playback(good,false,8000);assertEquals(22,ai.sourceCount());
    }

    @Test public void nonPlaybackAndLocalBrowserSourcesCannotTrain(){PersonalAi ai=new PersonalAi(context,"default");Station s=station("a","PL","news","good.example");ai.playback(s,true,0);ai.playback(s,false,-1);ai.playback(s,true,3_600_001);s.url="http://127.0.0.1/live.m3u8";ai.playback(s,true,300);s.url="https://youtube.com/watch?v=abc";ai.playback(s,true,300);s.url="https://good.example/player.html";ai.playback(s,true,300);s.url="https://good.example/live.m3u8";s.kind="test";ai.playback(s,true,300);assertEquals(0,ai.sourceCount());}

    @Test public void offAndResetArePersistentAndIsolatedFromLibrary(){ProfileStore profiles=new ProfileStore(context);Station s=station("a","PL","news","good.example");profiles.setFavorite(s,true);profiles.watched(s);PersonalAi a=new PersonalAi(context,profiles.currentId());a.preference(s,true);a.playback(s,true,300);profiles.add("Drugi");String secondId=profiles.currentId();PersonalAi b=new PersonalAi(context,secondId);b.preference(s,false);assertTrue(a.score(s)>b.score(s));a.setEnabled(false);a.preference(s,false);a.playback(s,false,8000);assertEquals(1,a.preferenceCount());assertEquals(1,a.sourceCount());assertEquals(0,a.score(s),0);assertFalse(new PersonalAi(context,"default").enabled());a.reset();assertFalse(a.enabled());assertEquals(0,a.preferenceCount());assertEquals(0,a.sourceCount());assertEquals(1,new PersonalAi(context,secondId).preferenceCount());profiles.select("Mój profil");assertTrue(profiles.favorite(s));assertEquals(1,profiles.history().size());}

    @Test public void deterministicRankingPreservesEveryCandidateAndDuplicate(){PersonalAi ai=new PersonalAi(context,"default");Station a=station("a","PL","sports","one.example"),b=station("b","DE","music","two.example"),c=station("c","PL","news","three.example");ai.preference(b,true);List<Station> list=new ArrayList<>(Arrays.asList(c,a,b,a));ai.reorder(list);assertEquals(4,list.size());assertSame(b,list.get(0));assertEquals(2,Collections.frequency(list,a));List<Station> original=new ArrayList<>(list);ai.reorder(list);assertEquals(original,list);}

    @Test public void profilesFavoritesHistoryAndMetadataRecovery()throws Exception {
        ProfileStore profiles=new ProfileStore(context);Station a=station("a","PL","news","one.example"),b=station("b","DE","music","two.example");profiles.setFavorite(a,true);profiles.watched(a);profiles.watched(b);profiles.watched(a);assertEquals(2,profiles.history().size());assertEquals(a.key(),profiles.history().get(0).key());List<Station> exposed=profiles.favorites();exposed.get(0).name="External mutation";assertNotEquals("External mutation",profiles.favorites().get(0).name);
        profiles.add("Podróż");String secondId=profiles.currentId();assertTrue(secondId.matches("[a-f0-9]{32}"));assertTrue(profiles.favorites().isEmpty());profiles.setFavorite(b,true);ProfileStore reopened=new ProfileStore(context);assertEquals("Podróż",reopened.current());assertTrue(reopened.favorite(b));reopened.select("Mój profil");assertTrue(reopened.favorite(a));assertFalse(reopened.favorite(b));
        write(new File(new File(root,"profiles"),"index.json"),"{broken");ProfileStore recovered=new ProfileStore(context);assertTrue(recovered.profiles().contains("Podróż"));recovered.select("Podróż");assertTrue(recovered.favorite(b));assertFalse(recovered.notice().isEmpty());
        try{recovered.add("podróż");fail("Duplicate case-insensitive profile");}catch(IllegalArgumentException expected){}
        try{new PersonalAi(context,"../../outside");fail("Traversal profile");}catch(IllegalArgumentException expected){}
        assertFalse(new File(root.getParentFile(),"outside").exists());
    }

    @Test public void malformedModelIsQuarantinedWithoutTouchingOtherProfile()throws Exception {
        ProfileStore profiles=new ProfileStore(context);profiles.add("Other");String other=profiles.currentId();Station s=station("a","PL","news","good.example");new PersonalAi(context,other).preference(s,true);
        File file=new File(ProfileStore.directory(context,"default"),"personal-ai.json");write(file,"{broken");PersonalAi recovered=new PersonalAi(context,"default");assertEquals(0,recovered.preferenceCount());assertFalse(recovered.notice().isEmpty());assertEquals(1,new PersonalAi(context,other).preferenceCount());assertTrue(file.getParentFile().listFiles((dir,name)->name.contains(".damaged-")).length>0);
        recovered.preference(s,true);JSONObject json=ProfileStore.readJson(file,2_000_000);json.put("preferenceWeights",new JSONArray().put(999));ProfileStore.writeJson(file,json);assertEquals(0,new PersonalAi(context,"default").preferenceCount());
    }

    @Test public void staleSourceEvidenceDecaysWithoutPretendingItWasVerified()throws Exception {
        PersonalAi ai=new PersonalAi(context,"default");Station s=station("a","PL","news","good.example");for(int i=0;i<8;i++)ai.playback(station("source"+i,"PL","news","good.example"),true,300);double fresh=ai.score(s);assertTrue(fresh>0);
        File file=new File(ProfileStore.directory(context,"default"),"personal-ai.json");JSONObject json=ProfileStore.readJson(file,2_000_000);json.put("sourceUpdated",System.currentTimeMillis()-365L*86_400_000L);ProfileStore.writeJson(file,json);assertTrue(Math.abs(new PersonalAi(context,"default").score(s))<fresh*.001);
    }
    @Test public void atomicBackupOnlyStateRecoversAfterInterruptedWrite(){
        ProfileStore profiles=new ProfileStore(context);Station s=station("a","PL","news","good.example");profiles.setFavorite(s,true);PersonalAi ai=new PersonalAi(context,"default");ai.preference(s,true);
        File model=new File(ProfileStore.directory(context,"default"),"personal-ai.json"),library=new File(ProfileStore.directory(context,"default"),"library.json"),index=new File(new File(root,"profiles"),"index.json");
        for(File file:Arrays.asList(model,library,index))assertTrue(file.renameTo(new File(file.getPath()+".bak")));
        assertEquals(1,new PersonalAi(context,"default").preferenceCount());assertTrue(new ProfileStore(context).favorite(s));assertTrue(model.exists());assertTrue(library.exists());assertTrue(index.exists());
    }
}
