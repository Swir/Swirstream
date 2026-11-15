package com.swir.stream;

import android.content.Context;
import androidx.test.ext.junit.runners.AndroidJUnit4;
import androidx.test.platform.app.InstrumentationRegistry;
import org.json.JSONObject;
import org.junit.Assume;
import org.junit.Test;
import org.junit.runner.RunWith;
import static org.junit.Assert.*;
import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.ByteArrayOutputStream;
import java.nio.charset.StandardCharsets;
import java.security.KeyStore;
import java.util.List;
import java.util.UUID;
import java.util.concurrent.atomic.AtomicBoolean;
import javax.crypto.Cipher;
import javax.crypto.SecretKey;
import javax.crypto.spec.GCMParameterSpec;

/** Opt-in public URL only. Does not use or test any operator account. */
@RunWith(AndroidJUnit4.class)
public class ProviderNetworkTest {
    @Test public void actualPublicUrlSavedEncryptedAndEveryDownloadedNameSearchable()throws Exception{
        Assume.assumeTrue("true".equals(InstrumentationRegistry.getArguments().getString("network")));Context context=InstrumentationRegistry.getInstrumentation().getTargetContext();String profile=UUID.randomUUID().toString().replace("-","");ProviderRepository repository=new ProviderRepository(context,profile);String id="";
        try{
            ProviderRepository.SourceInfo source=repository.addM3u("Publiczna lista PL","https://iptv-org.github.io/iptv/countries/pl.m3u","",new AtomicBoolean());id=source.id;ProviderRepository reopened=new ProviderRepository(context,profile);List<Station> channels=reopened.stations();List<ProviderRepository.SourceInfo> reopenedSources=reopened.listSources();
            JSONObject diagnostic=new JSONObject().put("at",System.currentTimeMillis()).put("sdk",android.os.Build.VERSION.SDK_INT).put("downloadedSourceCount",source.count).put("downloadedStatus",source.status).put("reopenedSourceCount",reopenedSources.size()).put("reopenedChannelCount",channels.size()).put("reopenedType",reopenedSources.isEmpty()?"missing":reopenedSources.get(0).type).put("reopenedStatus",reopenedSources.isEmpty()?"missing":reopenedSources.get(0).status).put("vault",diagnoseVault(context,profile,id,reopened));write(context,"provider-public-url-diagnostic.json",diagnostic);
            assertTrue("Downloaded public playlist must have >10 channels; diagnostic="+diagnostic,source.count>10);assertEquals("Reopening must retain every downloaded channel; diagnostic="+diagnostic,source.count,channels.size());assertTrue("Actual public playlist has channels; diagnostic="+diagnostic,channels.size()>10);int matches=0;for(Station station:channels){assertTrue("Every downloaded display name searchable",LocalChannelSearch.prepare(station.name).matches(station));assertTrue(station.url.startsWith("swir://provider/"));assertFalse(station.toJson().toString().contains("https://"));matches++;}
            JSONObject evidence=new JSONObject().put("at",System.currentTimeMillis()).put("publicEndpoint","https://iptv-org.github.io/iptv/countries/pl.m3u").put("channelCount",channels.size()).put("downloadedNamesMatched",matches).put("reopenedSourceCount",reopened.listSources().size()).put("privateVaultOnly",true).put("playbackClaim",false);File directory=context.getExternalFilesDir("evidence");assertNotNull(directory);directory.mkdirs();try(FileOutputStream out=new FileOutputStream(new File(directory,"provider-public-url.json"))){out.write(evidence.toString(2).getBytes(StandardCharsets.UTF_8));}
        }finally{if(!id.isEmpty())repository.remove(id);new File(new File(context.getNoBackupFilesDir(),"providers"),profile).delete();KeyStore store=KeyStore.getInstance("AndroidKeyStore");store.load(null);store.deleteEntry("swir.providers.v1."+profile);}
    }
    private static void write(Context context,String name,JSONObject json)throws Exception{File directory=context.getExternalFilesDir("evidence");assertNotNull(directory);directory.mkdirs();try(FileOutputStream output=new FileOutputStream(new File(directory,name))){output.write(json.toString(2).getBytes(StandardCharsets.UTF_8));}}
    /** Public test input only; record stages/counts/classes, never decrypted JSON or URLs. */
    private static JSONObject diagnoseVault(Context context,String profile,String id,ProviderRepository repository)throws Exception{
        File file=new File(new File(new File(context.getNoBackupFilesDir(),"providers"),profile),id+".vault");JSONObject result=new JSONObject().put("bytes",file.length());String stage="file";byte[] plain=null;
        try{
            byte[] encoded;try(FileInputStream input=new FileInputStream(file);ByteArrayOutputStream bytes=new ByteArrayOutputStream()){byte[] block=new byte[8192];int n;while((n=input.read(block))!=-1)bytes.write(block,0,n);encoded=bytes.toByteArray();}
            stage="key";KeyStore store=KeyStore.getInstance("AndroidKeyStore");store.load(null);SecretKey key=(SecretKey)store.getKey("swir.providers.v1."+profile,null);result.put("keyPresent",key!=null);stage="decrypt";Cipher cipher=Cipher.getInstance("AES/GCM/NoPadding");cipher.init(Cipher.DECRYPT_MODE,key,new GCMParameterSpec(128,encoded,6,12));cipher.updateAAD(("SwirStream providers v1|"+profile+"|"+id).getBytes(StandardCharsets.UTF_8));plain=ProviderVault.finishInChunks(cipher,encoded,18,encoded.length-18);result.put("plaintextBytes",plain.length);
            stage="json";JSONObject source=new JSONObject(new String(plain,StandardCharsets.UTF_8));org.json.JSONArray entries=source.getJSONArray("entries");result.put("entryCount",entries.length());stage="references";java.lang.reflect.Method reference=ProviderRepository.class.getDeclaredMethod("reference",JSONObject.class,JSONObject.class);reference.setAccessible(true);for(int i=0;i<entries.length();i++){result.put("referenceIndex",i);reference.invoke(repository,source,entries.getJSONObject(i));}result.put("stage","complete");
        }catch(Exception error){result.put("stage",stage).put("errorClass",error.getClass().getName());Throwable cause=error.getCause();if(cause!=null)result.put("causeClass",cause.getClass().getName());}finally{if(plain!=null)java.util.Arrays.fill(plain,(byte)0);}return result;
    }
}
