package com.swir.stream;

import android.security.keystore.KeyGenParameterSpec;
import android.security.keystore.KeyProperties;
import android.util.AtomicFile;
import org.json.JSONObject;
import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.security.KeyStore;
import java.util.Arrays;
import javax.crypto.Cipher;
import javax.crypto.KeyGenerator;
import javax.crypto.SecretKey;
import javax.crypto.spec.GCMParameterSpec;

/** Only encrypted bytes reach disk; no temporary plaintext JSON or playlist. */
final class ProviderVault {
    private static final byte[] MAGIC={'S','W','P','V',1};
    static final int MAX_VAULT=32*1024*1024;
    private final File directory;private final String profile,alias;
    ProviderVault(File directory,String profile){this.directory=directory;this.profile=profile;this.alias="swir.providers.v1."+profile;}
    File file(String id)throws IOException{if(id==null||!id.matches("[a-f0-9]{32}"))throw new IOException("Nieprawidłowy identyfikator źródła.");return new File(directory,id+".vault");}
    private SecretKey key()throws Exception{
        KeyStore store=KeyStore.getInstance("AndroidKeyStore");store.load(null);java.security.Key existing=store.getKey(alias,null);if(existing instanceof SecretKey)return (SecretKey)existing;
        KeyGenerator generator=KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES,"AndroidKeyStore");generator.init(new KeyGenParameterSpec.Builder(alias,KeyProperties.PURPOSE_ENCRYPT|KeyProperties.PURPOSE_DECRYPT).setBlockModes(KeyProperties.BLOCK_MODE_GCM).setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE).setKeySize(256).build());return generator.generateKey();
    }
    private byte[] aad(String id){return ("SwirStream providers v1|"+profile+"|"+id).getBytes(StandardCharsets.UTF_8);}
    JSONObject read(String id)throws IOException{
        File target=file(id);try{
            if(target.length()>MAX_VAULT)throw new IOException();byte[] encoded;try(FileInputStream input=new AtomicFile(target).openRead();ByteArrayOutputStream bytes=new ByteArrayOutputStream()){byte[] chunk=new byte[16384];int n;while((n=input.read(chunk))!=-1){if(bytes.size()+n>MAX_VAULT)throw new IOException();bytes.write(chunk,0,n);}encoded=bytes.toByteArray();}
            if(encoded.length<34||!Arrays.equals(MAGIC,Arrays.copyOf(encoded,MAGIC.length))||encoded[5]!=12)throw new IOException();Cipher cipher=Cipher.getInstance("AES/GCM/NoPadding");cipher.init(Cipher.DECRYPT_MODE,key(),new GCMParameterSpec(128,encoded,6,12));cipher.updateAAD(aad(id));byte[] plain=finishInChunks(cipher,encoded,18,encoded.length-18);try{if(plain.length!=encoded.length-34)throw new IOException();return new JSONObject(new String(plain,StandardCharsets.UTF_8));}finally{Arrays.fill(plain,(byte)0);}
        }catch(Exception e){throw new IOException("Nie można odczytać chronionego źródła. Plik i dane pozostały bez zmian.");}
    }
    void write(String id,JSONObject value)throws IOException{
        File target=file(id);AtomicFile atomic=new AtomicFile(target);FileOutputStream output=null;byte[] plain=value.toString().getBytes(StandardCharsets.UTF_8);
        try{
            if(plain.length>MAX_VAULT-64)throw new IOException();if(!directory.isDirectory()&&!directory.mkdirs())throw new IOException();Cipher cipher=Cipher.getInstance("AES/GCM/NoPadding");cipher.init(Cipher.ENCRYPT_MODE,key());byte[] iv=cipher.getIV();if(iv.length!=12)throw new IOException();cipher.updateAAD(aad(id));byte[] encrypted=finishInChunks(cipher,plain,0,plain.length);if(encrypted.length!=plain.length+16)throw new IOException();output=atomic.startWrite();output.write(MAGIC);output.write(iv.length);output.write(iv);output.write(encrypted);atomic.finishWrite(output);
        }catch(Exception e){if(output!=null)atomic.failWrite(output);throw new IOException("Nie zapisano chronionego źródła. Poprzednia lista pozostaje bez zmian.");}finally{Arrays.fill(plain,(byte)0);}
    }
    /**
     * Older Android KeyStoreCryptoOperationChunkedStreamer drops intermediate
     * output when one update spans more than two 64 KiB chunks. Feed 16 KiB at
     * a time. No decrypted bytes leave this method until GCM doFinal verifies
     * the tag; the temporary buffer is wiped on success and authentication error.
     */
    static byte[] finishInChunks(Cipher cipher,byte[] input,int offset,int count)throws java.security.GeneralSecurityException{
        try(SensitiveBytes output=new SensitiveBytes()){
            int end=offset+count;for(int at=offset;at<end;at+=16384){byte[] part=cipher.update(input,at,Math.min(16384,end-at));if(part!=null)try{output.write(part,0,part.length);}finally{Arrays.fill(part,(byte)0);}}
            byte[] last=cipher.doFinal();if(last!=null)try{output.write(last,0,last.length);}finally{Arrays.fill(last,(byte)0);}return output.toByteArray();
        }
    }
    private static final class SensitiveBytes extends ByteArrayOutputStream {@Override public void close(){Arrays.fill(buf,(byte)0);reset();}}
    void remove(String id)throws IOException{new AtomicFile(file(id)).delete();if(file(id).exists())throw new IOException("Nie udało się usunąć źródła.");}
    String[] ids(){File[] files=directory.listFiles((dir,name)->name.matches("[a-f0-9]{32}\\.vault(?:\\.bak)?"));if(files==null)return new String[0];java.util.Set<String> ids=new java.util.TreeSet<>();for(File file:files)ids.add(file.getName().substring(0,32));return ids.toArray(new String[0]);}
}
