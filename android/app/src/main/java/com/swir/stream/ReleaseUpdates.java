package com.swir.stream;

import android.app.Activity;
import android.app.AlertDialog;
import android.content.Intent;
import android.content.pm.PackageInfo;
import android.net.Uri;
import android.os.Build;
import android.provider.Settings;
import android.widget.Toast;
import org.json.*;
import java.io.*;
import java.net.*;
import java.security.MessageDigest;
import java.util.Locale;
import java.util.regex.*;

final class ReleaseUpdates {
    static final String API="https://api.github.com/repos/Swir/Swirstream/releases/latest";
    private static final String VERSION="[0-9]{1,5}\\.[0-9]{1,5}\\.[0-9]{1,5}";
    private static final Pattern NAME=Pattern.compile("SwirStreamAI_Android_("+VERSION+")\\.apk");
    static final class Asset {
        final String version,url,digest;final long size;
        Asset(String v,String u,String d,long n){version=v;url=u;digest=d;size=n;}
    }
    static int compare(String a,String b){
        String[] left=a.split("\\."),right=b.split("\\.");
        for(int i=0;i<3;i++){int n=Integer.compare(Integer.parseInt(left[i]),Integer.parseInt(right[i]));if(n!=0)return n;}
        return 0;
    }
    static Asset select(JSONObject json,String installed)throws JSONException,IOException {
        String tag=json.getString("tag_name");
        if(json.getBoolean("draft")||json.getBoolean("prerelease")||!tag.matches("v"+VERSION))return null;
        JSONArray assets=json.getJSONArray("assets");Asset best=null;
        for(int i=0;i<assets.length();i++){
            JSONObject item=assets.getJSONObject(i);String name=item.optString("name");Matcher m=NAME.matcher(name);
            if(!m.matches()||compare(m.group(1),installed)<=0)continue;
            String url="https://github.com/Swir/Swirstream/releases/download/"+tag+"/"+name;
            if(!url.equals(item.optString("browser_download_url")))continue;
            String digest=item.optString("digest");long size=item.optLong("size",0);
            if(!digest.matches("sha256:[0-9a-f]{64}")||size<1||size>150L*1024*1024)throw new IOException("Invalid APK integrity metadata");
            if(best==null||compare(m.group(1),best.version)>0)best=new Asset(m.group(1),url,digest.substring(7),size);
        }
        return best;
    }
    private final Activity activity;private final boolean english;
    private boolean busy,manualRequest;private File pending;
    ReleaseUpdates(Activity a,boolean en){activity=a;english=en;}
    boolean active(){return !activity.isFinishing()&&!activity.isDestroyed();}
    private String label(String en,String pl){return english?en:pl;}
    private void toast(String msg){if(active())Toast.makeText(activity,msg,Toast.LENGTH_LONG).show();}
    private static HttpURLConnection open(String url)throws IOException{
        HttpURLConnection c=(HttpURLConnection)new URL(url).openConnection();c.setConnectTimeout(10000);c.setReadTimeout(30000);
        c.setRequestProperty("User-Agent","SwirStream-Updater/1");return c;
    }
    void check(boolean manual){
        if(pending!=null&&manual){install(pending);return;}
        if(busy){manualRequest|=manual;return;}
        busy=true;manualRequest=manual;
        new Thread(()->{
            try{
                PackageInfo pkg=activity.getPackageManager().getPackageInfo(activity.getPackageName(),0);
                HttpURLConnection c=open(API);Asset asset=null;
                try{
                    if(c.getResponseCode()!=404){
                        try(InputStream in=c.getInputStream();ByteArrayOutputStream data=new ByteArrayOutputStream()){
                            byte[] buf=new byte[8192];int n;
                            while((n=in.read(buf))!=-1){if(data.size()+n>2*1024*1024)throw new IOException("Metadata too large");data.write(buf,0,n);}
                            asset=select(new JSONObject(data.toString("UTF-8")),pkg.versionName);
                        }
                    }
                }finally{c.disconnect();}
                Asset found=asset;
                activity.runOnUiThread(()->{
                    busy=false;if(!active())return;
                    if(found==null){if(manualRequest)toast(label("No newer stable APK.","Masz aktualną wersję APK."));return;}
                    new AlertDialog.Builder(activity).setTitle("SwirStream AI "+found.version)
                      .setMessage(label("Download the update and open the Android installer?","Pobrać aktualizację i otworzyć instalator Androida?"))
                      .setPositiveButton(label("Update","Aktualizuj"),(d,w)->download(found)).setNegativeButton(label("Later","Później"),null).show();
                });
            }catch(Exception e){activity.runOnUiThread(()->{busy=false;if(manualRequest)toast(label("Could not check GitHub Releases.","Nie udało się sprawdzić wydań na GitHubie."));});}
        },"SwirReleaseCheck").start();
    }
    private void download(Asset asset){
        if(busy)return;busy=true;toast(label("Downloading update…","Pobieram aktualizację…"));
        new Thread(()->{
            File temp=null;
            try{
                File dir=new File(activity.getCacheDir(),"release-updates");if(!dir.isDirectory()&&!dir.mkdirs())throw new IOException("mkdir");
                File[] old=dir.listFiles((folder,name)->name.matches("update-[0-9]+\\.apk"));
                if(old!=null)for(File file:old)if(file.lastModified()<System.currentTimeMillis()-7*86400000L)file.delete();
                temp=File.createTempFile("update-",".apk",dir);
                MessageDigest hash=MessageDigest.getInstance("SHA-256");long count=0;
                HttpURLConnection c=open(asset.url);
                try(InputStream in=c.getInputStream();OutputStream out=new FileOutputStream(temp)){
                    byte[] buf=new byte[65536];int n;
                    while((n=in.read(buf))!=-1){count+=n;if(count>asset.size)throw new IOException("size");out.write(buf,0,n);hash.update(buf,0,n);}
                }finally{c.disconnect();}
                StringBuilder actual=new StringBuilder();for(byte b:hash.digest())actual.append(String.format(Locale.ROOT,"%02x",b&255));
                if(count!=asset.size||!actual.toString().equals(asset.digest))throw new IOException("digest");
                PackageInfo info=activity.getPackageManager().getPackageArchiveInfo(temp.toString(),0);
                if(info==null||!activity.getPackageName().equals(info.packageName)||!asset.version.equals(info.versionName))throw new IOException("package");
                File verified=temp;temp=null;activity.runOnUiThread(()->{busy=false;if(active()){pending=verified;install(verified);}else verified.delete();});
            }catch(Exception e){activity.runOnUiThread(()->{busy=false;toast(label("Could not verify or download the update.","Nie udało się pobrać lub zweryfikować aktualizacji."));});}
            finally{if(temp!=null)temp.delete();}
        },"SwirReleaseDownload").start();
    }
    void resumeInstaller(){if(pending!=null && waitingForSettings){waitingForSettings=false;if(Build.VERSION.SDK_INT>=26&&activity.getPackageManager().canRequestPackageInstalls())install(pending);}}
    private boolean waitingForSettings;
    private void install(File file){
        try{
            if(Build.VERSION.SDK_INT>=26&&!activity.getPackageManager().canRequestPackageInstalls()){
                waitingForSettings=true;activity.startActivity(new Intent(Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES,Uri.parse("package:"+activity.getPackageName())));return;
            }
            Uri uri=new Uri.Builder().scheme("content").authority(activity.getPackageName()+".updates").appendPath(file.getName()).build();
            activity.startActivity(new Intent(Intent.ACTION_VIEW).setDataAndType(uri,"application/vnd.android.package-archive").addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION));
        }catch(RuntimeException error){toast(label("The system installer is unavailable.","Instalator systemowy jest niedostępny."));}
    }
}
