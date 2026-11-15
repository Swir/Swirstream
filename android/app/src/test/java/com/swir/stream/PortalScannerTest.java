package com.swir.stream;

import org.junit.Test;
import java.io.*;
import java.util.*;
import java.util.concurrent.atomic.*;
import static org.junit.Assert.*;

public final class PortalScannerTest {
    private List<PortalTxt.Entry> entries(){return Arrays.asList(new PortalTxt.Entry("https://portal.example.org/c/","02:00:00:00:00:01",1),new PortalTxt.Entry("https://portal.example.org/c/","02:00:00:00:00:02",2),new PortalTxt.Entry("https://portal.example.org/c/","",3));}
    @Test public void completedRowsSurviveCancellationAndPendingRowsStayUnchecked(){AtomicBoolean cancel=new AtomicBoolean();AtomicInteger calls=new AtomicInteger();List<PortalScanner.Result> rows=new PortalScanner((entry,stop,end)->{calls.incrementAndGet();return new PortalScanner.Result(PortalScanner.Status.CATALOG_AVAILABLE,3,false);},1000,10000).scan(entries(),cancel,(index,row)->{if(row.status==PortalScanner.Status.CATALOG_AVAILABLE)cancel.set(true);});assertEquals(1,calls.get());assertEquals(PortalScanner.Status.CATALOG_AVAILABLE,rows.get(0).status);assertEquals(PortalScanner.Status.NOT_CHECKED,rows.get(1).status);assertFalse(rows.get(0).label().contains("odtwarzanie potwierdzone"));}
    @Test public void budgetExpiresAndStopsLaterAccounts(){AtomicInteger calls=new AtomicInteger();long started=System.nanoTime();List<PortalScanner.Result> rows=new PortalScanner((entry,stop,end)->{calls.incrementAndGet();while(!stop.get())Thread.sleep(2);throw new IOException("url?password=hidden 02:00:00:00:00:01");},35,35).scan(entries(),new AtomicBoolean(),(i,r)->{});assertEquals(1,calls.get());assertEquals(PortalScanner.Status.TIMEOUT,rows.get(0).status);assertEquals(PortalScanner.Status.NOT_CHECKED,rows.get(1).status);assertTrue((System.nanoTime()-started)/1_000_000<1000);assertFalse(rows.get(0).label().contains("hidden"));}
    @Test public void redactedCsvKeepsOnlyStatusAndCounts(){List<PortalScanner.Result> results=Arrays.asList(new PortalScanner.Result(PortalScanner.Status.CATALOG_AVAILABLE,12,true),PortalScanner.Result.status(PortalScanner.Status.ACCESS_DENIED));String csv=PortalScanner.csv(entries(),results);assertFalse(csv.contains("example.org"));assertFalse(csv.contains("02:00"));assertTrue(csv.contains("CATALOG_AVAILABLE,12,true,not_checked"));assertTrue(csv.contains("NOT_CHECKED,0,false,not_checked"));}
    @Test public void classifiedDenialDoesNotLeakExceptions(){List<PortalScanner.Result> results=new PortalScanner((e,c,d)->{throw new ProviderHttp.StatusException(403);},1000,1000).scan(Collections.singletonList(entries().get(0)),new AtomicBoolean(),(i,r)->{});assertEquals(PortalScanner.Status.ACCESS_DENIED,results.get(0).status);assertEquals("Odmowa dostępu",results.get(0).label());}
}
