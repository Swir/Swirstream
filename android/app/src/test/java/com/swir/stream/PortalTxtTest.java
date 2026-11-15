package com.swir.stream;

import org.junit.Test;
import org.json.*;
import java.io.*;
import java.nio.charset.StandardCharsets;
import java.util.*;
import static org.junit.Assert.*;

public final class PortalTxtTest {
    @Test public void sharedWindowsAndroidContract()throws Exception{
        byte[] source;try(InputStream in=getClass().getResourceAsStream("/portal-txt-parity.json")){assertNotNull(in);source=in.readAllBytes();}
        JSONArray cases=new JSONObject(new String(source,StandardCharsets.UTF_8)).getJSONArray("cases");assertEquals(28,cases.length());
        for(int i=0;i<cases.length();i++){JSONObject fixture=cases.getJSONObject(i),expected=fixture.getJSONObject("expected");String name=fixture.getString("name");PortalTxt.Import actual;
            try{actual=PortalTxt.parse(Base64.getDecoder().decode(fixture.getString("inputBase64")));}catch(IOException error){assertTrue(name+" unexpectedly rejected",expected.getBoolean("fatal"));continue;}
            assertFalse(name+" must reject entire import",expected.getBoolean("fatal"));assertEquals(name,expected.getInt("invalidLines"),actual.invalidCount);assertEquals(name,expected.getInt("duplicates"),actual.duplicates);JSONArray rows=expected.getJSONArray("entries");assertEquals(name,rows.length(),actual.entries.size());
            for(int j=0;j<rows.length();j++){assertEquals(name,rows.getJSONObject(j).getString("url"),actual.entries.get(j).url);assertEquals(name,rows.getJSONObject(j).getString("mac"),actual.entries.get(j).mac);}
        }
    }
    @Test public void mixedInlineBlockAndEncodedPercentPreserveAccountIdentity()throws Exception{PortalTxt.Import result=PortalTxt.parse(("https://portal.example.org/my%25portal/c/|02:00:00:00:00:01\n02:00:00:00:00:02 # expiry\n").getBytes(StandardCharsets.UTF_8));assertEquals(2,result.entries.size());assertEquals(result.entries.get(0).url,result.entries.get(1).url);assertEquals(0,result.invalidCount);}
    @Test public void oversizedRawReadRejectsBeforeMoreThanOneBlockBeyondLimit()throws Exception{final int[] read={0};InputStream input=new InputStream(){public int read(){read[0]++;return 'x';}public int read(byte[] bytes){Arrays.fill(bytes,(byte)'x');read[0]+=bytes.length;return bytes.length;}};try{PortalTxt.read(input);fail();}catch(IOException expected){assertTrue(read[0]<=PortalTxt.MAX_BYTES+8192);}}
    @Test public void errorDetailsAreBoundedAndLabelsNeverExposeSecrets()throws Exception{StringBuilder text=new StringBuilder();for(int i=0;i<1000;i++)text.append("https://portal.example.org/c/?password=hidden\n");text.append("https://portal.example.org/private-path/c/|02:00:00:00:00:01\n");PortalTxt.Import result=PortalTxt.parse(text.toString().getBytes(StandardCharsets.UTF_8));assertEquals(1000,result.invalidCount);assertEquals(50,result.invalidLines.size());assertEquals(1,result.entries.size());String label=result.entries.get(0).label(0);assertFalse(label.contains("02:00"));assertFalse(label.contains("portal.example"));assertFalse(label.contains("private-path"));}
    @Test public void twoHundredUniquePlusDuplicateAllowedButNextUniqueRejectsEntireFile()throws Exception{StringBuilder text=new StringBuilder();for(int i=0;i<200;i++)text.append("https://portal.example.org/c/").append(i).append('\n');text.append("https://portal.example.org/c/0\n");PortalTxt.Import result=PortalTxt.parse(text.toString().getBytes(StandardCharsets.UTF_8));assertEquals(200,result.entries.size());assertEquals(1,result.duplicates);text.append("https://portal.example.org/c/201\n");try{PortalTxt.parse(text.toString().getBytes(StandardCharsets.UTF_8));fail();}catch(IOException expected){assertTrue(expected.getMessage().contains("200"));}}
    @Test public void literalPrivateIpRejectedOffline()throws Exception{for(String host:new String[]{"127.0.0.1","192.168.1.2","10.1.2.3","[::1]","[fc00::1]","2130706433"}){PortalTxt.Import result=PortalTxt.parse(("http://"+host+"/c/\n").getBytes(StandardCharsets.UTF_8));assertTrue(host,result.entries.isEmpty());assertEquals(1,result.invalidCount);}}
    @Test public void rejectsUtf32AndEmbeddedNulAsWholeFile()throws Exception{for(byte[] bad:new byte[][]{{-1,-2,0,0,65,0,0,0},"https://portal.example.org/c/\0".getBytes(StandardCharsets.UTF_8)})try{PortalTxt.parse(bad);fail();}catch(IOException expected){assertFalse(expected.getMessage().contains("example.org"));}}
}
