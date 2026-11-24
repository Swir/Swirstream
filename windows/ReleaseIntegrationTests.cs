using System.Text.Json;

namespace SwirStreamAI;
internal static class ReleaseIntegrationTests {
    internal static int Run(string report){
        var checks=new List<string>();
        void Check(string name,bool ok){if(!ok)throw new InvalidDataException(name);checks.Add(name);}
        try{
            string name="SwirStreamAI_0.13.0_Setup_x64.exe",url="https://github.com/Swir/Swirstream/releases/download/v0.13.0/"+name;
            JsonElement Metadata(string digest,string address,bool preview=false)=>JsonSerializer.SerializeToElement(new{draft=false,prerelease=preview,tag_name="v0.13.0",assets=new[]{new{name,browser_download_url=address,digest,size=913}}});
            var good=Metadata("sha256:"+new string('a',64),url);
            Check("Stable upgrade",ReleaseUpdates.Select(good,new Version(0,12,0))?.Version==new Version(0,13,0));
            Check("Same release and downgrades skipped",ReleaseUpdates.Select(good,new Version(0,13,0))==null && ReleaseUpdates.Select(good,new Version(1,0,0))==null);
            Check("Unrelated origin and previews skipped",ReleaseUpdates.Select(Metadata("","https://example.org/a.exe"),new Version(0,12,0))==null && ReleaseUpdates.Select(Metadata("",url,true),new Version(0,12,0))==null);
            try{ReleaseUpdates.Select(Metadata("sha256:bad",url),new Version(0,12,0));throw new Exception("Accepted bad hash");}catch(InvalidDataException){checks.Add("Malformed hash rejected");}
            string text="#NAME Demo\n#SERVICE 4097:0:1:0:0:0:0:0:0:0:https%3a//tv.example/live%3a8000/a%2Fb.m3u8:Old\n#DESCRIPTION Example HD\n#SERVICE 1:0:1:1:11:0:0:0:0:0:\n#DESCRIPTION DVB only";
            var rows=M3u.Parse(Bouquet.ToM3u(text),"demo.tv");Check("Dreambox IPTV decoded, DVB skipped",rows.Count==1&&rows[0].Name=="Example HD"&&rows[0].Url.EndsWith("/a%2Fb.m3u8"));
            Check("Kodinerds country",RegionalPlaylists.Country(RegionalPlaylists.Kodinerds,"IPTV-UK","")=="GB");
            Check("TDT Spain and international groups",RegionalPlaylists.Country(RegionalPlaylists.Tdt,"Andalucía","TV")=="ES" && RegionalPlaylists.Country(RegionalPlaylists.Tdt,"Int. Europa","TV")=="");
            File.WriteAllText(report,JsonSerializer.Serialize(new{success=true,checks}));return 0;
        }catch(Exception e){File.WriteAllText(report,JsonSerializer.Serialize(new{success=false,checks,error=e.Message}));return 1;}
    }
}
