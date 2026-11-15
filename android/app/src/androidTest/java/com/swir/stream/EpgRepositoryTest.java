package com.swir.stream;

import androidx.test.ext.junit.runners.AndroidJUnit4;
import org.junit.Test;
import org.junit.runner.RunWith;
import static org.junit.Assert.*;
import java.nio.charset.StandardCharsets;
import java.util.concurrent.atomic.AtomicBoolean;
import java.io.*;
import java.util.zip.GZIPOutputStream;
import java.text.SimpleDateFormat;
import java.util.*;

@RunWith(AndroidJUnit4.class)
public class EpgRepositoryTest {
    static String at(long value) { SimpleDateFormat f = new SimpleDateFormat("yyyyMMddHHmmss Z",Locale.ROOT);f.setTimeZone(TimeZone.getTimeZone("UTC"));return f.format(new Date(value)); }
    static String xml(long now) { return "<?xml version='1.0'?><tv><channel id='Demo.pl'><display-name>Demo HD</display-name></channel><programme channel='Demo.pl' start='"+at(now-60_000)+"' stop='"+at(now+60_000)+"'><title>Aktualna audycja</title></programme><programme channel='Demo.pl' start='"+at(now+60_000)+"' stop='"+at(now+3_600_000)+"'><title>Następna audycja</title></programme></tv>"; }
    @Test public void xmltvTimezoneAndInvalidDates() {
        assertEquals(EpgRepository.parseTime("20261107120000 +0100"),EpgRepository.parseTime("20261107110000 Z"));
        assertEquals(EpgRepository.parseTime("20261107110000"),EpgRepository.parseTime("20261107110000 +0000"));
        assertEquals(0,EpgRepository.parseTime("20260230120000 +0000"));assertEquals(0,EpgRepository.parseTime("nonsense"));
    }
    @Test public void compressedSchedulesAndCountryNames() throws Exception {
        long now=System.currentTimeMillis();ByteArrayOutputStream bytes=new ByteArrayOutputStream();
        try(GZIPOutputStream gzip=new GZIPOutputStream(bytes)){gzip.write(xml(now).getBytes(StandardCharsets.UTF_8));}
        EpgRepository.Feed feed=EpgRepository.parse(bytes.toByteArray(),"PL","https://example.com/test.xml.gz",now,now,new AtomicBoolean());
        assertTrue(EpgRepository.valid(feed,now,6));assertEquals(2,feed.channels.get("Demo.pl").programmes.size());
        Station station=new Station();station.country="PL";station.name="PL | Demo FHD";assertEquals("Demo.pl",EpgRepository.match(feed,station).id);
        station.tvgId="Demo.pl";station.name="Inna nazwa";assertEquals("Demo.pl",EpgRepository.match(feed,station).id);
        station.name="Demo +1";assertNull(EpgRepository.match(feed,station));
        station.name="Demo";station.tvgId="Demo.pl@regional";assertNull(EpgRepository.match(feed,station));
    }
    @Test public void ambiguousNamesAndStaleFeedsAreNotMatched() throws Exception {
        long now=System.currentTimeMillis();EpgRepository.Feed f=EpgRepository.parse(xml(now).getBytes(StandardCharsets.UTF_8),"PL","https://example.com/epg",now,0,new AtomicBoolean());
        EpgRepository.Channel extra=new EpgRepository.Channel("other");extra.names.add("Demo");f.channels.put("other",extra);
        Station s=new Station();s.name="Demo";s.country="PL";assertNull(EpgRepository.match(f,s));
        f.downloaded=now-49*EpgRepository.HOUR;assertFalse(EpgRepository.valid(f,now,48));
        try { EpgRepository.parse(xml(now).getBytes(StandardCharsets.UTF_8),"PL","https://example.com",now,now-49*EpgRepository.HOUR,new AtomicBoolean());fail("stale publisher accepted"); } catch(IOException expected){}
        try { EpgRepository.parse(xml(now-100*EpgRepository.HOUR).getBytes(StandardCharsets.UTF_8),"PL","https://example.com",now,0,new AtomicBoolean());fail("old programmes accepted"); } catch(IOException expected){}
    }
    @Test public void cancelledOversizeAndExternalEntitiesRejected() throws Exception {
        long now=System.currentTimeMillis();
        try{EpgRepository.parse(xml(now).getBytes(StandardCharsets.UTF_8),"PL","https://example.com",now,0,new AtomicBoolean(true));fail("cancel ignored");}catch(InterruptedIOException expected){}
        String xxe="<!DOCTYPE tv [<!ENTITY xxe SYSTEM 'file:///etc/passwd'>]>"+xml(now).replace("<?xml version='1.0'?>", "").replace("Aktualna audycja","&xxe;");
        try{EpgRepository.parse(xxe.getBytes(StandardCharsets.UTF_8),"PL","https://example.com",now,0,new AtomicBoolean());fail("external entity accepted");}catch(Exception expected){}
        String internal="<!DOCTYPE tv [<!ENTITY test 'internal'>]>"+xml(now).replace("<?xml version='1.0'?>", "").replace("Aktualna audycja","&test;");
        try{EpgRepository.parse(internal.getBytes(StandardCharsets.UTF_8),"PL","https://example.com",now,0,new AtomicBoolean());fail("doctype accepted");}catch(Exception expected){}
        try(InputStream in=new EpgRepository.LimitedInputStream(new ByteArrayInputStream(new byte[5]),4)){while(in.read()!=-1){}fail("limit ignored");}catch(IOException expected){}
        try{EpgRepository.readBytes(new ByteArrayInputStream(new byte[5]),10,new AtomicBoolean(),0);fail("deadline ignored");}catch(IOException expected){}
    }
    @Test public void publicDocumentUrlsOnly() throws Exception {
        assertEquals("example.com",EpgRepository.checkedUrl("https://example.com/epg.xml",false).getHost());
        for(String url:new String[]{"file:///tmp/a.xml","http://localhost/a","http://127.0.0.1/a","http://192.168.1.1/a","http://user:pass@example.com/a","http://example.com:8080/a"}) {
            try{EpgRepository.checkedUrl(url,true);fail("Accepted "+url);}catch(IOException expected){}
        }
    }
    @Test public void standardEscapesInIgnoredDescriptionAreAccepted() throws Exception {
        long now=System.currentTimeMillis();String doc=xml(now).replace("</title>","</title><desc>Rock &amp; roll &lt;live&gt; &#321; &#x142;</desc>");
        EpgRepository.Feed f=EpgRepository.parse(doc.getBytes(StandardCharsets.UTF_8),"PL","https://example.com",now,0,new AtomicBoolean());
        assertTrue(EpgRepository.valid(f,now,6));assertEquals("Aktualna audycja",f.channels.get("Demo.pl").programmes.get(0).title);
        assertFalse(EpgRepository.safeEntity("custom"));assertFalse(EpgRepository.safeEntity("xxe"));
    }
}
