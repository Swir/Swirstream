package com.swir.stream;

import android.content.Context;
import android.net.Uri;
import android.os.Build;
import androidx.media3.common.PlaybackException;
import androidx.media3.common.util.UnstableApi;
import androidx.media3.datasource.DataSpec;
import androidx.media3.datasource.HttpDataSource;
import androidx.media3.datasource.TransferListener;
import androidx.media3.datasource.okhttp.OkHttpDataSource;
import java.io.IOException;
import java.net.InetAddress;
import java.net.Proxy;
import java.net.URI;
import java.net.UnknownHostException;
import java.util.ArrayList;
import java.util.Collections;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.TreeMap;
import java.util.concurrent.TimeUnit;
import javax.net.ssl.SSLContext;
import javax.net.ssl.TrustManager;
import javax.net.ssl.X509TrustManager;
import okhttp3.Authenticator;
import okhttp3.CookieJar;
import okhttp3.Dns;
import okhttp3.HttpUrl;
import okhttp3.Interceptor;
import okhttp3.OkHttpClient;
import okhttp3.Request;
import okhttp3.Response;

/** Provider-only HTTP transport; public crawler policy remains independent. */
@UnstableApi
public final class ProviderDataSource implements HttpDataSource {
    static final String USER_AGENT = "SwirStreamAndroid/0.3.0";
    private static final int MAX_REDIRECTS = 5;

    public static final class Factory implements HttpDataSource.Factory {
        private final RequestProperties defaults = new RequestProperties();
        private final OkHttpDataSource.Factory delegate;

        /** Headers are authorized only for this exact scheme/host/effective-port origin. */
        public Factory(String authorizedOriginUrl, Map<String, String> headers) {
            this(null, authorizedOriginUrl, headers);
        }

        public Factory(Context context, String authorizedOriginUrl, Map<String, String> headers) {
            final HttpUrl origin;
            try { origin = checkedUrl(authorizedOriginUrl); }
            catch (IOException e) { throw new IllegalArgumentException("Nieprawidłowy publiczny adres źródła."); }
            Dns dns = new PublicDns(Dns.SYSTEM);
            OkHttpClient.Builder clientBuilder = new OkHttpClient.Builder()
                    .dns(dns).proxy(Proxy.NO_PROXY)
                    .cookieJar(CookieJar.NO_COOKIES)
                    .authenticator(Authenticator.NONE).proxyAuthenticator(Authenticator.NONE)
                    .followRedirects(false).followSslRedirects(false)
                    .connectTimeout(12, TimeUnit.SECONDS).readTimeout(20, TimeUnit.SECONDS)
                    .addInterceptor(new RedirectGuard(origin, dns));
            if (context != null && Build.VERSION.SDK_INT <= 25) {
                try {
                    X509TrustManager manager = LegacyTls.createTrustManager(context);
                    SSLContext tls = SSLContext.getInstance("TLS");
                    tls.init(null, new TrustManager[]{manager}, null);
                    clientBuilder.sslSocketFactory(tls.getSocketFactory(), manager);
                } catch (Exception ignored) {
                    // Keep normal system validation when the verified compatibility CA is unavailable.
                }
            }
            OkHttpClient client = clientBuilder.build();
            delegate = new OkHttpDataSource.Factory(client).setUserAgent(USER_AGENT);
            setDefaultRequestProperties(headers);
        }

        @Override public Factory setDefaultRequestProperties(Map<String, String> headers) {
            defaults.clearAndSet(checkedHeaders(headers));
            return this;
        }

        @Override public ProviderDataSource createDataSource() {
            return new ProviderDataSource(delegate.createDataSource(), defaults);
        }
    }

    private final HttpDataSource delegate;
    private final RequestProperties defaults;
    private final RequestProperties properties = new RequestProperties();
    private DataSpec safeSpec;

    private ProviderDataSource(HttpDataSource delegate, RequestProperties defaults) {
        this.delegate = delegate;
        this.defaults = defaults;
    }

    @Override public long open(DataSpec spec) throws HttpDataSourceException {
        // A secret-free spec is attached to every outward-facing exception.
        safeSpec = new DataSpec.Builder().setUri("https://provider.invalid/resource")
                .setPosition(spec.position).setLength(spec.length).build();
        try {
            checkedUrl(spec.uri.toString());
            Map<String, String> headers = new TreeMap<>(String.CASE_INSENSITIVE_ORDER);
            headers.putAll(defaults.getSnapshot());
            headers.putAll(properties.getSnapshot());
            headers.putAll(checkedHeaders(spec.httpRequestHeaders));
            return delegate.open(spec.buildUpon().setHttpRequestHeaders(headers).build());
        } catch (IOException | IllegalArgumentException e) {
            try { delegate.close(); } catch (IOException ignored) { }
            throw sanitized(e, HttpDataSourceException.TYPE_OPEN);
        }
    }

    @Override public int read(byte[] buffer, int offset, int length) throws HttpDataSourceException {
        try { return delegate.read(buffer, offset, length); }
        catch (IOException e) { throw sanitized(e, HttpDataSourceException.TYPE_READ); }
    }

    @Override public void close() throws HttpDataSourceException {
        try { delegate.close(); }
        catch (IOException e) { throw sanitized(e, HttpDataSourceException.TYPE_CLOSE); }
    }

    private HttpDataSourceException sanitized(Exception error, int type) {
        if (safeSpec == null) safeSpec = new DataSpec.Builder().setUri("https://provider.invalid/resource").build();
        if (error instanceof InvalidResponseCodeException) {
            return new InvalidResponseCodeException(((InvalidResponseCodeException) error).responseCode,
                    null, null, Collections.emptyMap(), safeSpec, new byte[0]);
        }
        int reason = error instanceof HttpDataSourceException
                ? ((HttpDataSourceException) error).reason
                : PlaybackException.ERROR_CODE_IO_NETWORK_CONNECTION_FAILED;
        return new HttpDataSourceException("Nie można pobrać danych z bezpiecznego adresu źródła.", safeSpec, reason, type);
    }

    @Override public Uri getUri() { return delegate.getUri(); }
    @Override public int getResponseCode() { return delegate.getResponseCode(); }
    @Override public Map<String, List<String>> getResponseHeaders() { return delegate.getResponseHeaders(); }
    @Override public void addTransferListener(TransferListener listener) { delegate.addTransferListener(listener); }
    @Override public void setRequestProperty(String name, String value) {
        checkedHeaders(Collections.singletonMap(name, value)); clearRequestProperty(name); properties.set(name, value);
    }
    @Override public void clearRequestProperty(String name) {
        for (String key : new ArrayList<>(properties.getSnapshot().keySet()))
            if (key.equalsIgnoreCase(name)) properties.remove(key);
    }
    @Override public void clearAllRequestProperties() { properties.clear(); }

    private static Map<String, String> checkedHeaders(Map<String, String> headers) {
        Map<String, String> result = new TreeMap<>(String.CASE_INSENSITIVE_ORDER);
        if (headers == null) return result;
        for (Map.Entry<String, String> entry : headers.entrySet()) {
            String name = entry.getKey(), value = entry.getValue();
            if (name == null || value == null || name.isEmpty() || !name.matches("[!#$%&'*+.^_`|~0-9A-Za-z-]+")
                    || value.indexOf('\r') >= 0 || value.indexOf('\n') >= 0 || value.indexOf('\0') >= 0)
                throw new IllegalArgumentException("Nieprawidłowy nagłówek źródła.");
            // Transport controls Host, body size, and transfer encoding itself.
            if (name.equalsIgnoreCase("Host") || name.equalsIgnoreCase("Content-Length")
                    || name.equalsIgnoreCase("Transfer-Encoding") || name.equalsIgnoreCase("Proxy-Authorization")) continue;
            result.put(name, value);
        }
        return result;
    }

    static HttpUrl checkedUrl(String value) throws IOException {
        try {
            URI uri = new URI(value);
            if (uri.getRawUserInfo() != null || uri.getHost() == null || value.indexOf('\\') >= 0)
                throw new IOException("Nieprawidłowy publiczny adres źródła.");
            HttpUrl url = HttpUrl.parse(value);
            if (url == null || !url.username().isEmpty() || !url.password().isEmpty())
                throw new IOException("Nieprawidłowy publiczny adres źródła.");
            String host = url.host().toLowerCase(Locale.ROOT);
            if (host.endsWith(".")) host = host.substring(0, host.length() - 1);
            if (host.equals("localhost") || (!host.contains(".") && !host.contains(":")))
                throw new IOException("Niepubliczny adres źródła.");
            for (String suffix : new String[]{".localhost", ".local", ".lan", ".internal", ".home", ".localdomain"})
                if (host.endsWith(suffix)) throw new IOException("Niepubliczny adres źródła.");
            // Nonstandard public ports are intentional for user-configured providers.
            return url;
        } catch (java.net.URISyntaxException | IllegalArgumentException e) {
            throw new IOException("Nieprawidłowy publiczny adres źródła.");
        }
    }

    static boolean sameOrigin(HttpUrl a, HttpUrl b) {
        return a.scheme().equals(b.scheme()) && a.host().equals(b.host()) && a.port() == b.port();
    }

    static final class PublicDns implements Dns {
        private final Dns resolver;
        PublicDns(Dns resolver) { this.resolver = resolver; }
        @Override public List<InetAddress> lookup(String host) throws UnknownHostException {
            List<InetAddress> addresses;
            try { addresses = resolver.lookup(host); }
            catch (UnknownHostException e) { throw new UnknownHostException("Nie można rozwiązać publicznego adresu źródła."); }
            if (addresses.isEmpty()) throw new UnknownHostException("Brak publicznego adresu źródła.");
            for (InetAddress address : addresses) {
                if (!publicAddress(address)) throw new UnknownHostException("Źródło wskazuje niepubliczny adres.");
            }
            // OkHttp connects to these exact validated addresses (no later URLConnection DNS lookup).
            return addresses;
        }
    }

    static boolean publicAddress(InetAddress address) {
        if (!PublicHttp.publicAddress(address)) return false;
        byte[] bytes = address.getAddress();
        if (bytes.length == 4) {
            int a = bytes[0] & 255, b = bytes[1] & 255, c = bytes[2] & 255;
            return !(a == 192 && b == 0 && (c == 0 || c == 2))
                    && !(a == 198 && (b == 18 || b == 19 || b == 51 && c == 100))
                    && !(a == 203 && b == 0 && c == 113);
        }
        // Reject documentation, Teredo and 6to4 addresses (which can embed private IPv4).
        int a = bytes[0] & 255, b = bytes[1] & 255, c = bytes[2] & 255, d = bytes[3] & 255;
        return !(a == 0x20 && b == 0x02)
                && !(a == 0x20 && b == 0x01 && (c == 0 && d == 0 || c == 0x0d && d == 0xb8));
    }

    static Request scopeHeaders(Request request, HttpUrl origin) {
        Request.Builder builder = request.newBuilder();
        if (!sameOrigin(request.url(), origin)) {
            for (String name : request.headers().names()) {
                String lower = name.toLowerCase(Locale.ROOT);
                if (!lower.equals("range") && !lower.equals("accept") && !lower.equals("accept-encoding")
                        && !lower.equals("content-type") && !lower.equals("cache-control")) builder.removeHeader(name);
            }
            builder.header("User-Agent", USER_AGENT);
        }
        builder.removeHeader("Host").removeHeader("Proxy-Authorization");
        return builder.build();
    }

    static final class RedirectGuard implements Interceptor {
        private final HttpUrl origin;
        private final Dns dns;
        RedirectGuard(HttpUrl origin, Dns dns) { this.origin = origin; this.dns = dns; }

        @Override public Response intercept(Chain chain) throws IOException {
            Request request = chain.request();
            for (int count = 0; count <= MAX_REDIRECTS; count++) {
                checkedUrl(request.url().toString());
                // Revalidate even when OkHttp may reuse a pooled connection.
                dns.lookup(request.url().host());
                request = scopeHeaders(request, origin);
                Response response = chain.proceed(request);
                int code = response.code();
                if (code != 301 && code != 302 && code != 303 && code != 307 && code != 308) return response;
                String location = response.header("Location");
                response.close();
                if (count == MAX_REDIRECTS || location == null) throw new IOException("Nieprawidłowe przekierowanie źródła.");
                HttpUrl next = request.url().resolve(location);
                if (next == null) throw new IOException("Nieprawidłowe przekierowanie źródła.");
                checkedUrl(next.toString());
                if (request.url().isHttps() && !next.isHttps()) throw new IOException("Źródło obniża zabezpieczenia połączenia.");
                boolean body = request.body() != null;
                if (body && !sameOrigin(request.url(), next))
                    throw new IOException("Licencja wymaga bezpośredniego adresu serwera.");
                Request.Builder redirected = request.newBuilder().url(next);
                if ((code == 301 || code == 302 || code == 303) && !request.method().equals("HEAD"))
                    redirected.method("GET", null).removeHeader("Content-Length").removeHeader("Content-Type").removeHeader("Transfer-Encoding");
                request = redirected.build();
            }
            throw new IOException("Za dużo przekierowań źródła.");
        }
    }
}
