using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
namespace SwirStreamAI;
public static class PublicProviderHandoffTests
{
    public static int Run(string report)
    {
        var checks=new List<object>();bool success=true;
        void Check(string name,bool ok){checks.Add(new{name,ok});success&=ok;}
        try{RunChecks().GetAwaiter().GetResult();}catch(Exception ex){checks.Add(new{name="Unexpected failure",ok=false,detail=ex.ToString()});success=false;}
        File.WriteAllText(report,JsonSerializer.Serialize(new{success,checks},new JsonSerializerOptions{WriteIndented=true}));return success?0:1;
        async Task RunChecks()
        {
            string folder=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(report))!,"public-handoff-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
            string body="#EXTM3U x-tvg-url=\"https://fixture.example/public.xml\"\n#EXTINF:-1 tvg-id=\"good.pl\",Publiczny kanał\nhttps://fixture.example/good.m3u8?token=published-cdn-token\n#EXTINF:-1,Account fixture\nhttps://fixture.example/live/fixture-user/fixture-password/123.ts\n";
            string initialBody=body;var now=DateTimeOffset.UtcNow;
            string xml=$"<tv><channel id=\"good.pl\"><display-name>Publiczny kanał</display-name></channel><programme channel=\"good.pl\" start=\"{now.AddMinutes(-5):yyyyMMddHHmmss} +0000\" stop=\"{now.AddMinutes(30):yyyyMMddHHmmss} +0000\"><title>Program kontrolny</title></programme></tv>";
            var client=new PrivateProviderClient((request,ct)=>Task.FromResult(new ProviderDocument(Encoding.UTF8.GetBytes(request.Url.EndsWith(".xml")?xml:body),request.Url)));
            var source=new ProviderSource(Guid.NewGuid().ToString("N"),"Public discovered",PrivateSourceKind.M3u,"https://fixture.example/public.m3u",DiscoveredPublic:true);
            var store=new PrivateSourceStore(folder,client);store.Put(source);var first=await store.Refresh(source.Id,CancellationToken.None);
            Check("AI subscription removes account entry while keeping signed public CDN",first.Catalog?.Channels.Count==1&&first.Catalog.Channels[0].Station.Url.Contains("published-cdn-token"));
            Check("AI subscription keeps safe EPG and reports sanitized response provenance",first.Catalog?.EpgUrl=="https://fixture.example/public.xml"&&first.Catalog.Notice.Contains("SHA256")&&first.Guide?.Channels.Count==1);
            var reopened=new PrivateSourceStore(folder,client);Check("Public-discovery safety mode survives DPAPI reopen",reopened.Sources.Single().Source.DiscoveredPublic);
            body=body.Replace("fixture-password","changed-fixture-password");var again=await reopened.Refresh(source.Id,CancellationToken.None);Check("Each later AI refresh sanitizes newly returned account entries",again.Catalog?.Channels.Count==1&&!again.Catalog.Channels.Any(c=>c.Station.Url.Contains("changed-fixture-password")));
            body="#EXTM3U\n#EXTINF:-1,Only account\nhttps://fixture.example/live/fixture-user/fixture-password/123.ts\n";
            var failed=await reopened.Refresh(source.Id,CancellationToken.None);Check("No safe entries preserves previous catalogue and download date",failed.Catalog==again.Catalog&&failed.Catalog?.DownloadedAt==again.Catalog?.DownloadedAt);
            var manual=source with{Id=Guid.NewGuid().ToString("N"),DiscoveredPublic=false};var manualCatalog=await client.Refresh(manual,CancellationToken.None);Check("Manual operator playlist remains separate and accepts own account path",manualCatalog.Channels.Count==1&&manualCatalog.Channels[0].Station.Url.Contains("fixture-password"));
            string serialized=JsonSerializer.Serialize(manual);Check("Default false mode leaves 0.9.0 source serialization byte-compatible",!serialized.Contains("DiscoveredPublic"));
            var oldSource=new{manual.Id,manual.Name,manual.Kind,manual.Url,manual.Username,manual.Password,manual.Mac,manual.LicenseUrl,manual.KeyId,manual.KeyHex};
            Check("Existing source fingerprint remains identical across 0.9.0 migration",SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(oldSource)).SequenceEqual(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(manual))));
            var settings=new SourceSettings{TvTemplates=["https://fixture.example/{path}"]};
            var catalog=new SmartCatalog(folder,settings,(url,ct)=>Task.FromResult((initialBody,(DateTimeOffset?)now.AddHours(-2))));
            var publicResult=await catalog.Search("","TV",["PL"],true,CancellationToken.None);
            Check("Smart catalog retains public entries while filtering accounts before disk cache",publicResult.Stations.Count==1&&Directory.GetFiles(Path.Combine(folder,"catalog-cache"),"*.json").All(f=>!File.ReadAllText(f).Contains("fixture-password")));
            var cacheOnly=new SmartCatalog(folder,settings,(url,ct)=>throw new IOException("Controlled offline"));
            var restored=await cacheOnly.Search("","TV",["PL"],true,CancellationToken.None);
            Check("Public sanitized cache fallback retains original source data date",restored.Stations.Count==1&&restored.DataAt==publicResult.DataAt);
            body="#EXTM3U\n#EXT-X-TARGETDURATION:10\n#EXTINF:10,\nsegment.ts\n";bool refused=false;try{await client.Refresh(source,CancellationToken.None);}catch(InvalidDataException){refused=true;}Check("AI playlist subscription rejects a single-stream HLS manifest",refused);
        }
    }
}
