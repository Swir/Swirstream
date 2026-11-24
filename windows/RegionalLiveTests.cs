using System.Text.Json;

namespace SwirStreamAI;
internal static class RegionalLiveTests
{
    internal static int Run(string folder,string report){
        var results=new List<object>();bool success=true;
        try{
            using var limit=new CancellationTokenSource(TimeSpan.FromMinutes(4));
            var prior=M3u.Parse(File.ReadAllText(Path.Combine(folder,"world.feed")),"https://iptv-org.github.io/iptv/index.m3u").Select(s=>s.Url).ToHashSet();
            foreach(var (name,url,country) in new[]{("de",RegionalPlaylists.Kodinerds,"DE"),("es",RegionalPlaylists.Tdt,"ES")}){
                var rows=SharedPlaylists.Parse(File.ReadAllText(Path.Combine(folder,name+".feed")),url,[],[country],"");
                var extra=rows.Where(s=>!prior.Contains(s.Url)).DistinctBy(s=>s.Url).ToList();
                string? played=null;var ordered=extra.OrderByDescending(s=>s.Url.Contains(".m3u8")).Take(8);
                foreach(var row in ordered){
                    if(StreamProbe.Run(row,folder,limit.Token).GetAwaiter().GetResult().Decoded){played=row.Name;break;}
                }
                results.Add(new{country,matched=rows.Count,extraUrls=extra.Count,decodedSample=played});success&=played!=null;
            }
        }catch(Exception e){results.Add(new{error=e.Message});success=false;}
        File.WriteAllText(report,JsonSerializer.Serialize(new{success,results}));return success?0:1;
    }
    internal static async Task<int> Update(string report){
        string path=report+".installer";try{
            using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(4));
            var asset=await ReleaseUpdates.Latest(new Version(0,0,0),timeout.Token)??throw new IOException("Missing stable release");
            await ReleaseUpdates.Download(asset,path,timeout.Token);
            File.WriteAllText(report,JsonSerializer.Serialize(new{success=true,version=asset.Version.ToString(),sha256=asset.Digest}));return 0;
        }catch(Exception e){File.WriteAllText(report,JsonSerializer.Serialize(new{success=false,error=e.Message}));return 1;}
        finally{if(File.Exists(path))File.Delete(path);}
    }
}
