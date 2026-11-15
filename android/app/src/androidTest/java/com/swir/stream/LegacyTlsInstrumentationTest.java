package com.swir.stream;

import android.content.Context;
import android.os.Build;
import androidx.test.ext.junit.runners.AndroidJUnit4;
import androidx.test.platform.app.InstrumentationRegistry;
import java.io.InputStream;
import java.lang.reflect.Proxy;
import java.net.URL;
import java.security.cert.CertificateException;
import java.security.cert.CertificateFactory;
import java.security.cert.X509Certificate;
import java.util.concurrent.atomic.AtomicBoolean;
import javax.net.ssl.HttpsURLConnection;
import javax.net.ssl.SSLException;
import javax.net.ssl.SSLSession;
import org.junit.Assume;
import org.junit.Test;
import org.junit.runner.RunWith;
import static org.junit.Assert.*;

@RunWith(AndroidJUnit4.class)
public final class LegacyTlsInstrumentationTest {
    private Context target(){return InstrumentationRegistry.getInstrumentation().getTargetContext();}
    private X509Certificate untrusted()throws Exception{try(InputStream input=InstrumentationRegistry.getInstrumentation().getContext().getAssets().open("tls/untrusted-self-signed.crt")){return (X509Certificate)CertificateFactory.getInstance("X.509").generateCertificate(input);}}
    @Test public void bundledAnchorIsExactValidPublicCa()throws Exception{
        X509Certificate root=LegacyTls.root(target());root.checkValidity();root.verify(root.getPublicKey());assertTrue(root.getBasicConstraints()>=0);assertEquals("CN=ISRG Root X1,O=Internet Security Research Group,C=US",root.getSubjectX500Principal().getName());
        boolean present=false;for(X509Certificate accepted:LegacyTls.createTrustManager(target()).getAcceptedIssuers())if(accepted.equals(root))present=true;assertTrue(present);
    }
    @Test public void unknownCertificateAndEmptyChainAreRejected()throws Exception{
        try{LegacyTls.createTrustManager(target()).checkServerTrusted(new X509Certificate[]{untrusted()},"RSA");fail("Unknown self-signed certificate must never become trusted");}catch(CertificateException expected){}
        try{LegacyTls.createTrustManager(target()).checkServerTrusted(new X509Certificate[0],"RSA");fail("Empty chain must be rejected");}catch(CertificateException expected){}
    }
    @Test public void defaultHostnameValidationIsStillEnforced()throws Exception{
        X509Certificate certificate=untrusted();SSLSession session=(SSLSession)Proxy.newProxyInstance(SSLSession.class.getClassLoader(),new Class<?>[]{SSLSession.class},(proxy,method,args)->{
            if(method.getName().equals("getPeerCertificates"))return new java.security.cert.Certificate[]{certificate};
            if(method.getName().equals("getPeerHost"))return "untrusted.swir-test.invalid";
            if(method.getReturnType()==boolean.class)return false;if(method.getReturnType()==int.class)return 0;if(method.getReturnType()==long.class)return 0L;return null;
        });
        assertTrue("Verifier recognizes matching SAN independently of chain trust",HttpsURLConnection.getDefaultHostnameVerifier().verify("untrusted.swir-test.invalid",session));
        assertFalse("Valid chain cannot authorize a different hostname",HttpsURLConnection.getDefaultHostnameVerifier().verify("different.swir-test.invalid",session));
    }
    @Test public void applicationInstallsOnlyOnLegacyAndroid(){
        assertTrue(target().getApplicationContext() instanceof SwirApplication);
        assertEquals(Build.VERSION.SDK_INT<=25?"system + ISRG Root X1":"system",LegacyTls.status());
    }
    @Test public void realSourcesWorkAndInvalidTlsIsRejected()throws Exception{
        Assume.assumeTrue("Explicit live network check",Boolean.parseBoolean(InstrumentationRegistry.getArguments().getString("network","false")));
        for(String url:new String[]{"https://iptv-org.github.io/iptv/countries/pl.m3u","https://raw.githubusercontent.com/Free-TV/IPTV/master/playlist.m3u8","https://de1.api.radio-browser.info/json/stats","https://dwamdstream102.akamaized.net/hls/live/2015525/dwstream102/master.m3u8"})assertFalse(url,PublicHttp.fetch(url,new AtomicBoolean()).text.isEmpty());
        for(String url:new String[]{"https://self-signed.badssl.com/","https://wrong.host.badssl.com/","https://expired.badssl.com/"}){
            HttpsURLConnection connection=(HttpsURLConnection)new URL(url).openConnection();connection.setConnectTimeout(8000);connection.setReadTimeout(8000);
            try{connection.getResponseCode();fail("Invalid TLS was accepted: "+url);}catch(SSLException expected){}finally{connection.disconnect();}
        }
    }
}
