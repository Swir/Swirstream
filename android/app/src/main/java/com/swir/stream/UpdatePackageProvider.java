package com.swir.stream;

import android.content.*;
import android.database.Cursor;
import android.database.MatrixCursor;
import android.net.Uri;
import android.os.ParcelFileDescriptor;
import android.provider.OpenableColumns;
import java.io.*;

public final class UpdatePackageProvider extends ContentProvider {
    @Override public boolean onCreate(){return true;}
    private File file(Uri uri) {
        String name=uri.getLastPathSegment();
        if(uri.getPathSegments().size()!=1 || name==null || !name.matches("update-[0-9]+\\.apk"))throw new IllegalArgumentException("Unknown package");
        return new File(new File(getContext().getCacheDir(),"release-updates"),name);
    }
    @Override public String getType(Uri uri){file(uri);return "application/vnd.android.package-archive";}
    @Override public ParcelFileDescriptor openFile(Uri uri,String mode)throws FileNotFoundException{
        if(!"r".equals(mode))throw new SecurityException("Read-only package");
        return ParcelFileDescriptor.open(file(uri),ParcelFileDescriptor.MODE_READ_ONLY);
    }
    @Override public Cursor query(Uri uri,String[] cols,String where,String[] args,String order){
        File packageFile=file(uri);
        if(cols==null)cols=new String[]{OpenableColumns.DISPLAY_NAME,OpenableColumns.SIZE};
        MatrixCursor cursor=new MatrixCursor(cols);MatrixCursor.RowBuilder row=cursor.newRow();
        for(String col:cols)row.add(OpenableColumns.DISPLAY_NAME.equals(col)?packageFile.getName():OpenableColumns.SIZE.equals(col)?packageFile.length():null);
        return cursor;
    }
    @Override public Uri insert(Uri uri,ContentValues values){throw new UnsupportedOperationException();}
    @Override public int delete(Uri uri,String where,String[] args){throw new UnsupportedOperationException();}
    @Override public int update(Uri uri,ContentValues values,String where,String[] args){throw new UnsupportedOperationException();}
}
