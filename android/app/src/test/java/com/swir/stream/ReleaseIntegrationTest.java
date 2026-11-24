package com.swir.stream;

import org.json.*;
import org.junit.Test;
import java.io.IOException;
import static org.junit.Assert.*;

public class ReleaseIntegrationTest {
    final String name="SwirStreamAI_Android_0.10.0.apk";
    final String url="https://github.com/Swir/Swirstream/releases/download/v0.14.0/"+name;
    JSONObject release()throws Exception{return new JSONObject().put("draft",false).put("prerelease",false).put("tag_name","v0.14.0").put("assets",new JSONArray().put(new JSONObject().put("name",name).put("browser_download_url",url).put("size",200L).put("digest","sha256:"+new String(new char[64]).replace('\0','a'))));}
    @Test public void selectsNumericallyNewerApkAndSkipsPreview()throws Exception{
        assertEquals("0.10.0",ReleaseUpdates.select(release(),"0.6.0").version);
        assertNull(ReleaseUpdates.select(release(),"0.10.0"));assertNull(ReleaseUpdates.select(release(),"1.0.0"));
        assertNull(ReleaseUpdates.select(release().put("prerelease",true),"0.6.0"));
    }
    @Test public void checksOriginAndHash()throws Exception{
        JSONObject json=release(),asset=json.getJSONArray("assets").getJSONObject(0);
        asset.put("browser_download_url","https://example.com/fake.apk");assertNull(ReleaseUpdates.select(json,"0.6.0"));
        asset.put("browser_download_url",url).put("digest","");try{ReleaseUpdates.select(json,"0.6.0");fail();}catch(IOException expected){}
    }
    @Test public void bouquetNamesAndCountryFilters()throws Exception{
        String text="#NAME Demo\n#SERVICE 4097:0:1:0:0:0:0:0:0:0:https%3a//tv.example/live%2F1.m3u8:Old\n#DESCRIPTION Example HD\n#SERVICE 1:0:1:0:1:0:0:0:0:0:\n#DESCRIPTION DVB";
        String out=Bouquet.toM3u(text);
        assertTrue(out.contains(",Example HD\nhttps://tv.example/live%2F1.m3u8"));assertFalse(out.contains("DVB"));
        assertEquals("GB",RegionalPlaylists.country(RegionalPlaylists.KODINERDS,"IPTV-UK",""));
        assertEquals("",RegionalPlaylists.country(RegionalPlaylists.TDT,"Int. Asia","TV"));
        assertEquals("ES",RegionalPlaylists.country(RegionalPlaylists.TDT,"Andalucía","TV"));
    }
}
