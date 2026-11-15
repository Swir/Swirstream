package com.swir.stream;

import org.junit.Test;
import java.io.IOException;
import java.util.List;
import static org.junit.Assert.*;

/** Pure local policy fixtures; no HTTP, credentials, device or portal session is used. */
public final class SourceExplorerRulesTest {
    private static final String PORTAL="https://publisher.example/stalker_portal/c/index.html";
    private static final String SCRIPT="<script src='xpcom.common.js'></script>";
    private static String page(String content){return "<html><title>Ministra</title><body>"+content+"</body></html>";}

    @Test public void publicClientNeedsActiveExactTechnicalFingerprint(){
        assertTrue(SourceExplorerRules.portalFingerprint(PORTAL,page(SCRIPT)));
        assertTrue(SourceExplorerRules.portalFingerprint(PORTAL,page("<script src='../server/api/load_js.php'></script>")));
        assertFalse(SourceExplorerRules.portalFingerprint("https://publisher.example/article",page(SCRIPT)));
        assertFalse(SourceExplorerRules.portalFingerprint(PORTAL,page("STB setup instructions")));
    }
    @Test public void genericCPathWithoutFingerprintIsNotPortal(){
        assertFalse(SourceExplorerRules.portalFingerprint("https://publisher.example/c/",page("Read about Stalker portals")));
        assertFalse(SourceExplorerRules.portalFingerprint("https://publisher.example/c/article",page(SCRIPT)));
    }
    @Test public void commentsAndInertDocumentationCannotSupplyFingerprint(){
        assertFalse(SourceExplorerRules.portalFingerprint(PORTAL,page("<script>var x=\"<script src='xpcom.common.js'>\";</script>")));
        assertFalse(SourceExplorerRules.portalFingerprint(PORTAL,page("<script type='text/plain' src='xpcom.common.js'></script>")));
        for(String wrapper:new String[]{"<!--%s-->","<template>%s</template>","<noscript>%s</noscript>","<textarea>%s</textarea>","<pre>%s</pre>","<code>%s</code>"})
            assertFalse(wrapper,SourceExplorerRules.portalFingerprint(PORTAL,page(String.format(wrapper,SCRIPT))));
    }
    @Test public void similarNamesAndNonHttpScriptSchemesAreNotMarkers(){
        for(String value:new String[]{"notxpcom.common.js","xpcom.common.js.bak","/other/load_js.php","data:xpcom.common.js","javascript:xpcom.common.js"})
            assertFalse(value,SourceExplorerRules.portalFingerprint(PORTAL,page("<script src='"+value+"'></script>")));
    }
    @Test public void plainAndQuotedCredentialLabelsArePrivate(){
        for(String value:new String[]{"Password: fixture-secret","Hasło: fixture-secret","Haslo=fixture-secret","Cookie: fixture-secret","Authorization: fixture-secret","\"password\":\"fixture-secret\"","MAC: 02:00:00:00:00:01"})assertTrue(value,SourceExplorerRules.privateText(value));
        assertFalse(SourceExplorerRules.privateText("Public Television — News HD"));
    }
    @Test public void DecimalHexAndNestedEntityLabelsArePrivate(){
        for(String value:new String[]{"Password&#58; fixture-secret","&#112;assword: fixture-secret","&#x70;assword&#x3a; fixture-secret","&amp;#112;assword: fixture-secret","&#38;#112;assword: fixture-secret"})assertTrue(value,SourceExplorerRules.privateText(value));
    }
    @Test public void percentEncodedCredentialProseCannotReachPublicSearch(){
        assertTrue(SourceExplorerRules.privateText("password%3A%20fixture-secret"));
        assertTrue(SourceExplorerRules.privateText("password%253A%2520fixture-secret"));
        assertTrue(SourceExplorerRules.privateText("https://provider.example/get.php?%70assword=fixture-secret"));
    }
    @Test public void accountTitlesUseSafeFallback(){
        assertEquals("publisher.example",SourceExplorerRules.title("<title>Password&#58; fixture-secret</title>","publisher.example"));
        assertEquals("Public News",SourceExplorerRules.title("<title>Public News</title>","publisher.example"));
    }
    @Test public void apiAdminAndPhpPathInfoNeverBecomeLandingRequests(){
        for(String suffix:new String[]{"/server/load.php","/portal.php","/portal.php/","/player_api.php/extra","/get.php","/admin/","/server/adm/","/login/","/api/account"})assertFalse(suffix,SourceExplorerRules.allowedPage("https://provider.example"+suffix));
    }
    @Test public void percentEncodedApiKeysAreComparedAfterDecoding(){
        for(String query:new String[]{"%61ction=handshake","%74ype=stb","JsHttpRequest=1-xml","debug&x=1","debug_key=fixture","mac=02%3A00%3A00%3A00%3A00%3A01"})assertFalse(query,SourceExplorerRules.allowedPage(PORTAL+"?"+query));
        assertTrue(SourceExplorerRules.allowedPage("https://provider.example/page?description=action%3Dstory"));
    }
    @Test public void publicPolicyRejectsPrivateAddressesAndUnapprovedPorts(){
        for(String url:new String[]{"http://127.0.0.1/c/","http://10.0.0.1/c/","http://[::1]/c/","https://provider.example:8080/c/","https://user:fixture-secret@provider.example/c/"})assertFalse(url,SourceExplorerRules.allowedPage(url));
    }
    @Test public void canonicalIdentityNormalizesHostAndDefaultPortWithoutChangingRouting()throws Exception{
        assertEquals("https://provider.example/prefix/c/",SourceExplorerRules.canonical("https://PROVIDER.EXAMPLE:443/prefix/c/"));
        assertEquals("https://provider.example/base%20path/live.m3u8?x=a%2Bb",SourceExplorerRules.canonical("https://provider.example/base%20path/live.m3u8?x=a%2Bb"));
        try{SourceExplorerRules.canonical("https://user:fixture-secret@provider.example/c/");fail();}catch(IOException expected){assertFalse(expected.getMessage().contains("fixture-secret"));}
    }
    @Test public void signedPublicMediaRemainsAllowedWithoutOpeningTokenLandingPages(){
        assertTrue(SourceExplorerRules.allowedPage("https://cdn.example/live.m3u8?token=public-signature&key=public-key"));
        assertFalse(SourceExplorerRules.allowedPage("https://provider.example/c/?token=fixture-secret"));
        assertFalse(SourceExplorerRules.allowedPage("https://cdn.example/live/account/password/123.ts"));
    }
    @Test public void hlsProtectionRecognizesSessionKeyAndDoesNotMislabelIdentityAes(){
        assertTrue(SourceExplorerRules.protectedManifest("#EXTM3U\n#EXT-X-SESSION-KEY:METHOD=SAMPLE-AES,KEYFORMAT='com.widevine',URI='https://license.example/'\n"));
        assertTrue(SourceExplorerRules.protectedManifest("<MPD><ContentProtection schemeIdUri='urn:uuid:fixture'/></MPD>"));
        assertFalse(SourceExplorerRules.protectedManifest("#EXTM3U\n#EXT-X-KEY:METHOD=AES-128,KEYFORMAT='identity',URI='https://cdn.example/key.bin'\n"));
        assertFalse(SourceExplorerRules.protectedManifest("#EXTM3U\n#EXT-X-KEY:METHOD=AES-128,KEYFORMAT=identity,URI='https://cdn.example/key.bin'\n"));
    }
    @Test public void linkedMediaAndIframeAreDeduplicatedWithoutAccountRequests(){
        List<String> links=SourceExplorerRules.links("https://publisher.example/page","<iframe src='/player/live'></iframe><a href='/public/channels.m3u'>TV</a><a href='/public/channels.m3u'>Repeat</a><a href='/server/load.php?action=handshake'>API</a>");
        assertTrue(links.contains("https://publisher.example/player/live"));assertTrue(links.contains("https://publisher.example/public/channels.m3u"));assertEquals(2,links.size());
    }
    @Test public void countryNamesDoNotBecomeCredentialIndexQueries(){
        assertFalse(SourceExplorer.credentialIndex("Macedonia public playlist"));
        assertFalse(SourceExplorer.credentialIndex("Macau TV playlist"));
        assertFalse(SourceExplorer.credentialIndex("Public television France"));
        assertTrue(SourceExplorer.credentialIndex("working MAC list"));
        assertTrue(SourceExplorer.credentialIndex("fresh password dump"));
    }
}
