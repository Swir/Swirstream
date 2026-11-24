package com.swir.stream;

import androidx.test.platform.app.InstrumentationRegistry;
import android.content.*;
import android.content.pm.PackageInfo;
import android.net.Uri;
import java.io.*;
import java.nio.file.*;
import org.junit.Test;
import static org.junit.Assert.*;

public class UpdateInstallerTest {
    @Test public void installerReadsExactSignedApkThroughPrivateGrant()throws Exception{
        Context ctx=InstrumentationRegistry.getInstrumentation().getTargetContext();
        File dir=new File(ctx.getCacheDir(),"release-updates");dir.mkdirs();
        File apk=File.createTempFile("update-",".apk",dir);
        try{
            try(InputStream source=new FileInputStream(ctx.getApplicationInfo().sourceDir);OutputStream out=new FileOutputStream(apk)){
                byte[] b=new byte[65536];int n;while((n=source.read(b))!=-1)out.write(b,0,n);
            }
            Uri uri=new Uri.Builder().scheme("content").authority(ctx.getPackageName()+".updates").appendPath(apk.getName()).build();
            Intent install=new Intent(Intent.ACTION_VIEW).setDataAndType(uri,ctx.getContentResolver().getType(uri)).addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);
            assertNotNull(install.resolveActivity(ctx.getPackageManager()));
            try(InputStream input=ctx.getContentResolver().openInputStream(uri)){assertEquals('P',input.read());assertEquals('K',input.read());}
            PackageInfo info=ctx.getPackageManager().getPackageArchiveInfo(apk.getAbsolutePath(),0);
            assertEquals("0.6.0",info.versionName);
            try{ctx.getContentResolver().openFileDescriptor(uri,"rw");fail("write grant");}catch(SecurityException expected){}
            try{ctx.getContentResolver().openInputStream(uri.buildUpon().appendPath("other.apk").build());fail("extra path");}catch(IllegalArgumentException expected){}
        }finally{apk.delete();}
    }
}
