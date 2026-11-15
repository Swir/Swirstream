package com.swir.stream;

import static org.junit.Assert.*;
import java.io.IOException;
import java.net.InetAddress;
import java.net.UnknownHostException;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.Collections;
import java.util.List;
import java.util.concurrent.TimeUnit;
import okhttp3.Call;
import okhttp3.Connection;
import okhttp3.Dns;
import okhttp3.HttpUrl;
import okhttp3.Interceptor;
import okhttp3.MediaType;
import okhttp3.Protocol;
import okhttp3.Request;
import okhttp3.RequestBody;
import okhttp3.Response;
import okhttp3.ResponseBody;
import org.junit.Test;

public class ProviderDataSourceTest {
    private static final HttpUrl ORIGIN = HttpUrl.get("https://provider.example:8443/live/secret");
    private static Dns publicDns() { return host -> Collections.singletonList(InetAddress.getByAddress(new byte[]{8,8,8,8})); }

    @Test public void exactOriginIncludesSchemeAndEffectivePort() {
        assertTrue(ProviderDataSource.sameOrigin(HttpUrl.get("https://provider.example/a"), HttpUrl.get("https://provider.example:443/b")));
        assertFalse(ProviderDataSource.sameOrigin(ORIGIN, HttpUrl.get("https://provider.example/segment")));
        assertFalse(ProviderDataSource.sameOrigin(ORIGIN, HttpUrl.get("http://provider.example:8443/segment")));
    }

    @Test public void publicProviderPortsAreAllowedWithoutChangingCrawlerPolicy() throws Exception {
        assertEquals(8443, ProviderDataSource.checkedUrl(ORIGIN.toString()).port());
        assertFalse(PublicHttp.allowedDocument(ORIGIN.toString()));
    }

    @Test public void invalidSchemesUserinfoAndLocalNamesAreRejected() {
        for (String url : Arrays.asList("file:///etc/passwd", "http://user:secret@provider.example/", "http://localhost/", "http://router.lan/", "http://router.local./", "http://intranet/")) {
            try { ProviderDataSource.checkedUrl(url); fail(url); }
            catch (IOException expected) { assertFalse(expected.toString().contains("secret")); }
        }
    }

    @Test public void dnsRejectsMixedPublicAndPrivateAnswers() throws Exception {
        ProviderDataSource.PublicDns dns = new ProviderDataSource.PublicDns(host -> Arrays.asList(
                InetAddress.getByAddress(new byte[]{8,8,8,8}), InetAddress.getByAddress(new byte[]{127,0,0,1})));
        try { dns.lookup("provider.example"); fail(); }
        catch (UnknownHostException expected) { assertFalse(expected.getMessage().contains("provider.example")); }
    }

    @Test public void dnsRejectsPrivateReservedAndIpv6TransitionAddresses() throws Exception {
        for (String address : Arrays.asList("0.0.0.0", "10.0.0.1", "127.0.0.1", "100.64.0.1", "169.254.169.254", "172.16.0.1", "192.168.1.1", "198.18.0.1", "224.0.0.1", "::1", "fd00::1", "fe80::1", "2002:7f00:1::", "2001:db8::1"))
            assertFalse(address, ProviderDataSource.publicAddress(InetAddress.getByName(address)));
        assertTrue(ProviderDataSource.publicAddress(InetAddress.getByName("8.8.8.8")));
        assertTrue(ProviderDataSource.publicAddress(InetAddress.getByName("2606:4700:4700::1111")));
    }

    private static Request credentialRequest(String url) {
        return new Request.Builder().url(url).header("Authorization", "Bearer secret").header("Cookie", "account=secret")
                .header("X-Api-Key", "secret").header("Referer", "https://provider.example/private?token=secret")
                .header("User-Agent", "PrivateDeviceID").header("Range", "bytes=8-15").header("Accept", "video/mp2t").build();
    }

    @Test public void separateCdnSegmentOpenCannotInheritProviderCredentials() {
        Request scoped = ProviderDataSource.scopeHeaders(credentialRequest("https://cdn.example/segment.ts"), ORIGIN);
        for (String header : Arrays.asList("Authorization", "Cookie", "X-Api-Key", "Referer")) assertNull(scoped.header(header));
        assertEquals("bytes=8-15", scoped.header("Range"));
        assertEquals("video/mp2t", scoped.header("Accept"));
        assertEquals(ProviderDataSource.USER_AGENT, scoped.header("User-Agent"));
        assertEquals("Bearer secret", ProviderDataSource.scopeHeaders(credentialRequest(ORIGIN.toString()), ORIGIN).header("Authorization"));
    }

    @Test public void redirectsValidateEveryDestinationAndStripCrossOriginHeaders() throws Exception {
        FakeChain chain = new FakeChain(credentialRequest(ORIGIN.toString()), new int[]{302,200}, new String[]{"https://cdn.example/segment.ts"});
        List<String> lookedUp = new ArrayList<>();
        Dns dns = host -> { lookedUp.add(host); return publicDns().lookup(host); };
        new ProviderDataSource.RedirectGuard(ORIGIN, dns).intercept(chain).close();
        assertEquals(Arrays.asList("provider.example", "cdn.example"), lookedUp);
        assertEquals(2, chain.sent.size());
        assertNull(chain.sent.get(1).header("Authorization"));
        assertNull(chain.sent.get(1).header("Cookie"));
        assertEquals("bytes=8-15", chain.sent.get(1).header("Range"));
    }

    @Test public void redirectToPrivateAddressIsStoppedBeforeRequest() throws Exception {
        FakeChain chain = new FakeChain(credentialRequest(ORIGIN.toString()), new int[]{302,200}, new String[]{"https://127.0.0.1/private"});
        Dns dns = new ProviderDataSource.PublicDns(host -> host.equals("127.0.0.1")
                ? Collections.singletonList(InetAddress.getByAddress(new byte[]{127,0,0,1})) : publicDns().lookup(host));
        try { new ProviderDataSource.RedirectGuard(ORIGIN, dns).intercept(chain); fail(); }
        catch (IOException expected) { assertEquals(1, chain.sent.size()); }
    }

    @Test public void pooledRequestStillRevalidatesChangedDns() throws Exception {
        final int[] calls = {0};
        Dns dns = new ProviderDataSource.PublicDns(host -> Collections.singletonList(InetAddress.getByAddress(
                ++calls[0] == 1 ? new byte[]{8,8,8,8} : new byte[]{127,0,0,1})));
        FakeChain chain = new FakeChain(credentialRequest(ORIGIN.toString()), new int[]{302,200}, new String[]{"/next"});
        try { new ProviderDataSource.RedirectGuard(ORIGIN, dns).intercept(chain); fail(); }
        catch (IOException expected) { assertEquals(1, chain.sent.size()); assertEquals(2, calls[0]); }
    }

    @Test public void licensePostNeverForwardsBodyToAnotherOrigin() throws Exception {
        Request post = credentialRequest(ORIGIN.toString()).newBuilder()
                .post(RequestBody.create(MediaType.get("application/json"), "{\"token\":\"secret\"}")).build();
        for (int code : new int[]{301,302,303,307,308}) {
            FakeChain chain = new FakeChain(post, new int[]{code,200}, new String[]{"https://cdn.example/license"});
            try { new ProviderDataSource.RedirectGuard(ORIGIN, publicDns()).intercept(chain); fail("code=" + code); }
            catch (IOException expected) { assertEquals(1, chain.sent.size()); assertFalse(expected.toString().contains("secret")); }
        }
    }

    @Test public void sameOrigin307PreservesLicensePostAndBody() throws Exception {
        Request post = credentialRequest(ORIGIN.toString()).newBuilder()
                .post(RequestBody.create(MediaType.get("application/json"), "{\"kids\":[]}")).build();
        FakeChain chain = new FakeChain(post, new int[]{307,200}, new String[]{"/license-new"});
        new ProviderDataSource.RedirectGuard(ORIGIN, publicDns()).intercept(chain).close();
        assertEquals("POST", chain.sent.get(1).method());
        assertSame(post.body(), chain.sent.get(1).body());
        assertEquals("Bearer secret", chain.sent.get(1).header("Authorization"));
    }

    @Test public void httpsDowngradeAndRedirectLoopAreStopped() throws Exception {
        FakeChain downgrade = new FakeChain(credentialRequest(ORIGIN.toString()), new int[]{302,200}, new String[]{"http://provider.example:8443/live"});
        try { new ProviderDataSource.RedirectGuard(ORIGIN, publicDns()).intercept(downgrade); fail(); }
        catch (IOException expected) { assertEquals(1, downgrade.sent.size()); }
        FakeChain loop = new FakeChain(credentialRequest(ORIGIN.toString()), new int[]{302,302,302,302,302,302,200}, new String[]{"/a","/a","/a","/a","/a","/a"});
        try { new ProviderDataSource.RedirectGuard(ORIGIN, publicDns()).intercept(loop); fail(); }
        catch (IOException expected) { assertEquals(6, loop.sent.size()); }
    }

    /** Controlled response fixture: no external accounts or real network requests. */
    static final class FakeChain implements Interceptor.Chain {
        final Request original;
        final int[] codes;
        final String[] locations;
        final List<Request> sent = new ArrayList<>();
        FakeChain(Request original, int[] codes, String[] locations) { this.original = original; this.codes = codes; this.locations = locations; }
        public Request request() { return original; }
        public Response proceed(Request request) {
            int i = sent.size(); sent.add(request);
            Response.Builder response = new Response.Builder().request(request).protocol(Protocol.HTTP_1_1)
                    .code(codes[i]).message("Fixture").body(ResponseBody.create(MediaType.get("application/octet-stream"), new byte[]{1,2,3}));
            if (i < locations.length) response.header("Location", locations[i]);
            return response.build();
        }
        public Connection connection() { return null; }
        public Call call() { return null; }
        public int connectTimeoutMillis() { return 1000; }
        public Interceptor.Chain withConnectTimeout(int timeout, TimeUnit unit) { return this; }
        public int readTimeoutMillis() { return 1000; }
        public Interceptor.Chain withReadTimeout(int timeout, TimeUnit unit) { return this; }
        public int writeTimeoutMillis() { return 1000; }
        public Interceptor.Chain withWriteTimeout(int timeout, TimeUnit unit) { return this; }
    }
}
