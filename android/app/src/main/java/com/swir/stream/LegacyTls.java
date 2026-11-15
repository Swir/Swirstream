package com.swir.stream;

import android.content.Context;
import android.os.Build;
import android.util.Log;
import java.io.InputStream;
import java.security.KeyStore;
import java.security.MessageDigest;
import java.security.cert.CertificateException;
import java.security.cert.CertificateFactory;
import java.security.cert.X509Certificate;
import java.util.Arrays;
import javax.net.ssl.HttpsURLConnection;
import javax.net.ssl.SSLContext;
import javax.net.ssl.TrustManager;
import javax.net.ssl.TrustManagerFactory;
import javax.net.ssl.X509TrustManager;

/** Adds one verified public CA missing from older Android, without relaxing TLS validation. */
final class LegacyTls {
    static final String ROOT_SHA256="96bcec06264976f37460779acf28c5a7cfe8a3c0aae11a8ffcee05c0bddf08c6";
    // Published ISRG Root X1 trust horizon; ship an updated app before this date.
    private static final long ROOT_TRUST_UNTIL=1906761600000L; // 2030-06-04T00:00:00Z
    private static boolean installed;
    private static String status="system";
    private LegacyTls(){}

    static synchronized void install(Context context){
        if(Build.VERSION.SDK_INT>25||installed)return;
        try{
            SSLContext tls=SSLContext.getInstance("TLS");
            tls.init(null,new TrustManager[]{createTrustManager(context)},null);
            // HttpsURLConnection is shared by public documents and Media3's HTTP data source.
            // Its existing hostname verifier is deliberately left untouched.
            HttpsURLConnection.setDefaultSSLSocketFactory(tls.getSocketFactory());
            installed=true;status="system + ISRG Root X1";
        }catch(Exception error){status="system (compatibility CA unavailable)";Log.w("SwirTls","Keeping system TLS validation; compatibility CA unavailable",error);}
    }
    static synchronized String status(){return status;}
    static X509Certificate root(Context context)throws Exception{
        X509Certificate root;
        try(InputStream input=context.getAssets().open("tls/isrg-root-x1.crt")){root=(X509Certificate)CertificateFactory.getInstance("X.509").generateCertificate(input);}
        byte[] digest=MessageDigest.getInstance("SHA-256").digest(root.getEncoded());StringBuilder hash=new StringBuilder();for(byte value:digest)hash.append(String.format(java.util.Locale.ROOT,"%02x",value&255));
        if(!ROOT_SHA256.equals(hash.toString())||root.getBasicConstraints()<0||!root.getSubjectX500Principal().equals(root.getIssuerX500Principal()))throw new CertificateException("Unexpected compatibility CA");
        root.verify(root.getPublicKey());root.checkValidity();checkTrustHorizon();return root;
    }
    private static void checkTrustHorizon()throws CertificateException{if(System.currentTimeMillis()>=ROOT_TRUST_UNTIL)throw new CertificateException("Compatibility CA trust horizon reached; update the application");}
    static X509TrustManager createTrustManager(Context context)throws Exception{
        X509TrustManager system=manager(null);X509Certificate certificate=root(context);
        KeyStore extra=KeyStore.getInstance(KeyStore.getDefaultType());extra.load(null,null);extra.setCertificateEntry("isrg-root-x1",certificate);
        return new SystemFirstTrustManager(system,manager(extra));
    }
    private static X509TrustManager manager(KeyStore store)throws Exception{
        TrustManagerFactory factory=TrustManagerFactory.getInstance(TrustManagerFactory.getDefaultAlgorithm());factory.init(store);
        for(TrustManager candidate:factory.getTrustManagers())if(candidate instanceof X509TrustManager)return (X509TrustManager)candidate;
        throw new CertificateException("No system X509 trust manager");
    }
    private static final class SystemFirstTrustManager implements X509TrustManager {
        private final X509TrustManager system,extra;
        SystemFirstTrustManager(X509TrustManager system,X509TrustManager extra){this.system=system;this.extra=extra;}
        @Override public void checkServerTrusted(X509Certificate[] chain,String authType)throws CertificateException{
            if(chain==null||chain.length==0)throw new CertificateException("Empty certificate chain");
            try{system.checkServerTrusted(chain,authType);}
            catch(CertificateException systemFailure){try{checkTrustHorizon();extra.checkServerTrusted(chain,authType);}catch(CertificateException additionalFailure){additionalFailure.addSuppressed(systemFailure);throw additionalFailure;}}
        }
        @Override public void checkClientTrusted(X509Certificate[] chain,String authType)throws CertificateException{system.checkClientTrusted(chain,authType);}
        @Override public X509Certificate[] getAcceptedIssuers(){X509Certificate[] primary=system.getAcceptedIssuers(),additional=extra.getAcceptedIssuers(),all=Arrays.copyOf(primary,primary.length+additional.length);System.arraycopy(additional,0,all,primary.length,additional.length);return all;}
    }
}
