package com.swir.stream;

import org.json.JSONObject;
import org.junit.Test;
import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.List;
import java.util.concurrent.atomic.AtomicBoolean;
import java.util.concurrent.atomic.AtomicInteger;
import static org.junit.Assert.*;

/** Offline protocol fixtures only: no account, DNS lookup or player connection. */
public final class PortalDiscoveryTest {
    private static final String GENRES="{\"js\":[{\"id\":\"1\",\"title\":\"TV\"}]}";
    private static final String CHANNELS="{\"js\":{\"data\":[{\"id\":\"7\",\"name\":\"Fixture TV\",\"tv_genre_id\":\"1\",\"cmd\":\"ffmpeg http://localhost/ch/7\"}]}}";
    private static ProviderHttp.Document doc(String url,String text){return new ProviderHttp.Document(url,text.getBytes(StandardCharsets.UTF_8),12345,0);}
    private static JSONObject config(String url)throws Exception{return new JSONObject().put("url",url).put("mac","02:00:00:00:00:01");}
    private interface Throwing {void run()throws Exception;}
    private static void rejects(Throwing action)throws Exception{try{action.run();fail("Expected rejection");}catch(IOException expected){assertFalse(expected.getMessage().contains("02:00:00:00:00:01"));assertFalse(expected.getMessage().contains("fixture.example"));}}

    @Test public void candidatesPreserveOriginPortAndEncodedPrefix()throws Exception{
        assertEquals(Arrays.asList("https://fixture.example:8443/panel%20one/%2Fkeep/portal.php","https://fixture.example:8443/panel%20one/%2Fkeep/server/load.php","https://fixture.example:8443/panel%20one/%2Fkeep/stalker_portal/server/load.php"),PortalDiscovery.candidates("https://fixture.example:8443/panel%20one/%2Fkeep/c/index.html"));
        assertEquals(2,PortalDiscovery.candidates("https://fixture.example/stalker_portal/c/").size());
    }
    @Test public void explicitEndpointAlwaysFirstAndNoDuplicate()throws Exception{
        List<String> values=PortalDiscovery.candidates("https://fixture.example/panel/server/load.php");assertEquals("https://fixture.example/panel/server/load.php",values.get(0));assertEquals(3,values.size());assertEquals(3,new java.util.HashSet<>(values).size());
        assertEquals("https://fixture.example/custom/api.php",PortalDiscovery.candidates("https://fixture.example/custom/api.php").get(0));
        assertEquals("https://fixture.example/c/",PortalDiscovery.input("https://fixture.example/c/"));
    }
    @Test public void rejectsUnsafePortalInputBeforeRequests()throws Exception{
        for(String url:new String[]{"https://user:secret@fixture.example/c/","https://fixture.example/c/?token=secret","https://fixture.example/c/#fragment","file:///test","http://localhost/c/"})rejects(()->PortalDiscovery.candidates(url));
    }
    @Test public void requiresNonemptyGenreShape()throws Exception{
        assertEquals(PortalDiscovery.Kind.DIRECT,PortalDiscovery.classify(GENRES));assertEquals(PortalDiscovery.Kind.DIRECT,PortalDiscovery.classify("{\"js\":{\"data\":[{\"id\":1,\"title\":\"News\"}]}}"));
        for(String text:new String[]{"{\"js\":[]}","{\"js\":{\"data\":[]}}","{\"js\":[{}]}","{\"js\":[{\"id\":{},\"title\":\"TV\"}]}","{\"js\":[{\"id\":1,\"title\":false}]}","{\"js\":false}","<html>portal</html>","{js:[]}","{\"js\":[],}"})assertEquals(text,PortalDiscovery.Kind.UNKNOWN,PortalDiscovery.classify(text));
    }
    @Test public void denialCannotMasqueradeAsDirectCatalog(){
        for(String text:new String[]{"{\"status\":1,\"js\":[{\"id\":1,\"title\":\"TV\"}]}","{\"error\":\"blocked\",\"js\":[{\"id\":1,\"title\":\"TV\"}]}","{\"js\":{\"blocked\":true,\"data\":[{\"id\":1,\"title\":\"TV\"}]}}"})assertEquals(PortalDiscovery.Kind.DENIED,PortalDiscovery.classify(text));
        assertEquals(PortalDiscovery.Kind.AUTH_REQUIRED,PortalDiscovery.classify(" Authorization failed. "));assertEquals(PortalDiscovery.Kind.AUTH_REQUIRED,PortalDiscovery.classify("{\"js\":{\"error\":\"Access denied\"}}"));
        for(String text:new String[]{"Unauthorized request.","Authorization failed. 75","{\"js\":\"Authorization failed.\"}","{\"js\":{\"error\":\"Invalid token\"}}"})assertEquals(PortalDiscovery.Kind.AUTH_REQUIRED,PortalDiscovery.classify(text));
        for(String auth:new String[]{"0","\"0\"","false"})assertEquals(PortalDiscovery.Kind.DENIED,PortalDiscovery.classify("{\"auth\":"+auth+",\"js\":[{\"id\":1,\"title\":\"TV\"}]}"));
        assertNotEquals(PortalDiscovery.Kind.DIRECT,PortalDiscovery.classify("{\"js\":{\"msg\":\"Access denied\",\"data\":[{\"id\":1,\"title\":\"TV\"}]}}"));
        for(String text:new String[]{"Account blocked","{\"js\":\"Account disabled\"}","{\"js\":{\"blocked\":true,\"error\":\"Invalid token\"}}","{\"status\":1,\"error\":\"Invalid token\",\"js\":[]}","{\"js\":[{\"id\":1,\"title\":\"TV\"},{\"error\":\"Account blocked\"}]}"})assertEquals(text,PortalDiscovery.Kind.DENIED,PortalDiscovery.classify(text));
        assertEquals(PortalDiscovery.Kind.UNKNOWN,PortalDiscovery.classify("{\"js\":[{\"id\":1,\"title\":\"TV\"},false]}"));
        assertNotEquals(PortalDiscovery.Kind.DIRECT,PortalDiscovery.classify("{\"js\":{\"errors\":[\"Access denied\"],\"data\":[{\"id\":1,\"title\":\"TV\"}]}}"));
        for(String flag:new String[]{"status","blocked"})assertEquals(PortalDiscovery.Kind.DENIED,PortalDiscovery.classify("{\""+flag+"\":\"fal\u017fe\",\"js\":[{\"id\":1,\"title\":\"TV\"}]}"));
    }
    @Test public void directCatalogNeverInvokesProfileOrHandshake()throws Exception{
        List<String> actions=new ArrayList<>();JSONObject config=config("https://fixture.example/c/");
        ProviderAdapters.PortalSession result=ProviderAdapters.portalSession(config,(url,headers,cancel)->{actions.add(url);assertEquals(PortalDiscovery.USER_AGENT,headers.get("User-Agent"));assertEquals("mac=02:00:00:00:00:01; stb_lang=pl; timezone=Europe%2FWarsaw",headers.get("Cookie"));assertFalse(headers.containsKey("Authorization"));assertFalse(url.contains("handshake")||url.contains("get_profile"));return doc(url,GENRES);},new AtomicBoolean());
        assertEquals(PortalDiscovery.DIRECT,result.mode);assertEquals("https://fixture.example/portal.php",result.endpoint);assertEquals("https://fixture.example/c/",config.getString("url"));assertEquals(1,actions.size());
    }
    @Test public void explicitFullPortalRequiresStrictProfile()throws Exception{
        List<String> actions=new ArrayList<>();ProviderAdapters.PortalSession session=ProviderAdapters.portalSession(config("https://fixture.example/server/load.php"),(url,h,c)->{actions.add(url);assertTrue(url.startsWith("https://fixture.example/server/load.php?"));if(url.contains("get_genres"))throw new ProviderHttp.StatusException(401);if(url.contains("handshake")){assertFalse(h.containsKey("Authorization"));return doc(url,"{\"js\":{\"token\":\"synthetic-token\"}}");}assertEquals("Bearer synthetic-token",h.get("Authorization"));return doc(url,"{\"js\":{\"id\":12,\"status\":0,\"blocked\":false}}");},new AtomicBoolean());assertEquals(PortalDiscovery.FULL,session.mode);assertEquals(3,actions.size());
    }
    @Test public void fullProfileDenialNeverFallsBackToSibling()throws Exception{
        for(String profile:new String[]{"{\"id\":0}","{\"id\":12,\"blocked\":true}","{\"id\":12,\"status\":\"unknown\"}","{\"id\":12,\"status\":1}","{\"id\":12,\"status\":0.0}"}){
            AtomicInteger count=new AtomicInteger();rejects(()->ProviderAdapters.portalSession(config("https://fixture.example/c/"),(url,h,c)->{count.incrementAndGet();assertTrue(url.startsWith("https://fixture.example/portal.php?"));if(url.contains("get_genres"))return doc(url,"Authorization failed.");if(url.contains("handshake"))return doc(url,"{\"js\":{\"token\":\"synthetic-token\"}}");return doc(url,"{\"js\":"+profile+"}");},new AtomicBoolean()));assertEquals(3,count.get());
        }
    }
    @Test public void notFoundTriesOnlyBoundedSameOriginCandidates()throws Exception{
        List<String> seen=new ArrayList<>();ProviderAdapters.PortalSession session=ProviderAdapters.portalSession(config("https://fixture.example/prefix/c/"),(url,h,c)->{seen.add(url);if(url.contains("/portal.php?"))throw new ProviderHttp.StatusException(404);return doc(url,GENRES);},new AtomicBoolean());assertEquals("https://fixture.example/prefix/server/load.php",session.endpoint);assertEquals(2,seen.size());
        AtomicInteger count=new AtomicInteger();rejects(()->ProviderAdapters.portalSession(config("https://fixture.example/c/"),(url,h,c)->{count.incrementAndGet();return doc(url,"{\"js\":[]}");},new AtomicBoolean()));assertEquals(3,count.get());
    }
    @Test public void networkFailureIsNotReclassifiedAsAccountOrDirect()throws Exception{
        AtomicInteger count=new AtomicInteger();rejects(()->ProviderAdapters.portalSession(config("https://fixture.example/c/"),(url,h,c)->{count.incrementAndGet();throw new IOException("Connection unavailable");},new AtomicBoolean()));assertEquals(1,count.get());
    }
    @Test public void explicitCatalogRefusalStopsBeforeAnySibling()throws Exception{
        AtomicInteger count=new AtomicInteger();rejects(()->ProviderAdapters.portalSession(config("https://fixture.example/c/"),(url,h,c)->{count.incrementAndGet();return doc(url,"{\"auth\":false,\"js\":[{\"id\":1,\"title\":\"TV\"}]}");},new AtomicBoolean()));assertEquals(1,count.get());
        assertEquals(PortalDiscovery.Kind.UNKNOWN,PortalDiscovery.classify("{\"js\":{\"error\":\"Unknown action\"}}"));
    }
    @Test public void cancelledDiscoveryNeverReturnsAResolvedSession()throws Exception{
        AtomicInteger count=new AtomicInteger();AtomicBoolean cancel=new AtomicBoolean(true);rejects(()->ProviderAdapters.portalSession(config("https://fixture.example/c/"),(url,h,c)->{count.incrementAndGet();return doc(url,GENRES);},cancel));assertEquals(0,count.get());
        cancel.set(false);rejects(()->ProviderAdapters.portalSession(config("https://fixture.example/c/"),(url,h,c)->{count.incrementAndGet();c.set(true);return doc(url,GENRES);},cancel));assertEquals(1,count.get());
    }
    @Test public void crossOriginProbeAndProtectedRedirectAreRejected()throws Exception{
        rejects(()->ProviderAdapters.portalSession(config("https://fixture.example/c/"),(url,h,c)->doc("https://other.example/portal.php",GENRES),new AtomicBoolean()));
        rejects(()->ProviderHttp.redirect("https://fixture.example/portal.php","https://other.example/portal.php",true));rejects(()->ProviderHttp.redirect("https://fixture.example/portal.php","http://fixture.example/portal.php",true));assertEquals("https://fixture.example/api.php",ProviderHttp.redirect("https://fixture.example/portal.php","/api.php",true));
    }
    @Test public void everyPlaybackResolutionGetsFreshLinkAndMode()throws Exception{
        AtomicInteger links=new AtomicInteger(),probes=new AtomicInteger();ProviderHttp.Fetcher fetcher=(url,h,c)->{if(url.contains("get_genres")){probes.incrementAndGet();return doc(url,GENRES);}assertTrue(url.contains("create_link"));assertFalse(h.containsKey("Authorization"));return doc(url,"{\"js\":{\"cmd\":\"ffmpeg https://media.example/live.ts?attempt="+links.incrementAndGet()+"\"}}");};
        String first="",second="";for(int i=0;i<2;i++){ProviderAdapters.PortalSession session=ProviderAdapters.portalSession(config("https://fixture.example/c/"),fetcher,new AtomicBoolean());String link=ProviderAdapters.portalLink(session,"ffmpeg http://localhost/ch/7",fetcher,new AtomicBoolean());if(i==0)first=link;else second=link;}assertNotEquals(first,second);assertEquals(2,links.get());assertEquals(2,probes.get());
    }
    @Test public void catalogNoticeCannotClaimPlaybackOrActiveAccount(){assertTrue(PortalDiscovery.DIRECT_NOTICE.contains("Aktywność konta i odtwarzanie niepotwierdzone"));assertTrue(PortalDiscovery.FULL_NOTICE.contains("Odtwarzanie niepotwierdzone"));}
    @Test public void largerLimitIsOnlyForExplicitPortalChannelRequest(){
        assertEquals(64_000_000,ProviderHttp.documentLimit("https://fixture.example/portal.php?type=itv&action=get_all_channels&JsHttpRequest=1-xml"));
        for(String suffix:new String[]{"list.m3u","portal.php?type=stb&action=get_all_channels","portal.php?action=get_all_channels","portal.php?type=itv&action=get_genres","portal.php?type=itv&action=get_all_channels&action=other","portal.php?type=itv&type=stb&action=get_all_channels"})assertEquals(12*1024*1024,ProviderHttp.documentLimit("https://fixture.example/"+suffix));
    }
    @Test public void optionalChannelFlagsAreNeverCoercedToFalse(){
        for(Object value:new Object[]{false,0,0L,"0","false"})assertEquals(Boolean.FALSE,PortalDiscovery.channelFlag(value));
        for(Object value:new Object[]{true,1,1L,"1","true"})assertEquals(Boolean.TRUE,PortalDiscovery.channelFlag(value));
        for(Object value:new Object[]{JSONObject.NULL,"","unknown","0.0",0.0,2,"FALSE","TRUE","fal\u017fe"," false ",new JSONObject()})assertNull(PortalDiscovery.channelFlag(value));
        assertNull(PortalDiscovery.channelFlag(null));
    }
    @Test public void originalMediaRequiresBothExplicitFalseFlagsAndSimpleHttpCommand()throws Exception{
        JSONObject entry=new JSONObject().put("command","ffmpeg https://media.example/live.ts");assertEquals("",PortalDiscovery.originalCommand(entry));entry.put("use_http_tmp_link",false);assertEquals("",PortalDiscovery.originalCommand(entry));entry.put("use_load_balancing",false);assertEquals("https://media.example/live.ts",PortalDiscovery.originalCommand(entry));
        entry.put("use_load_balancing",true);assertEquals("",PortalDiscovery.originalCommand(entry));entry.put("use_load_balancing",false);
        for(String cmd:new String[]{"ffmpeg http://localhost/ch/1","file:///tmp/video","ffmpeg -headers secret https://media.example/live","https://user:secret@media.example/live","https://media.example/live|User-Agent=secret"}){entry.put("command",cmd);assertEquals("",PortalDiscovery.originalCommand(entry));}
    }
}
