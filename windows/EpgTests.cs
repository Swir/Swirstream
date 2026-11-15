using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace SwirStreamAI;
public static class EpgTests
{
    public static string Fixture(DateTimeOffset time, string id = "TVN.pl", string name = "PL - TVN HD")
    {
        string T(DateTimeOffset x) => x.ToString("yyyyMMddHHmmss zzz").Replace(":", "");
        return $"<tv><channel id='{id}'><display-name>{name}</display-name></channel><programme channel='{id}' start='{T(time.AddHours(-1))}' stop='{T(time.AddHours(1))}'><title>Teraz • wiadomości</title><desc>Opis audycji</desc></programme><programme channel='{id}' start='{T(time.AddHours(1))}' stop='{T(time.AddHours(2))}'><title>Potem • film</title></programme></tv>";
    }
    public static int Run(string report, bool network)
    {
        var checks = new List<object>(); bool success = true; string folder = Path.Combine(Path.GetDirectoryName(report)!, "epg-tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        void Check(string name, bool ok, object? detail = null) { checks.Add(new { name, ok, detail }); success &= ok; }
        async Task Test()
        {
            var time = DateTimeOffset.UtcNow; byte[] bytes = Encoding.UTF8.GetBytes(Fixture(time));
            Check("XMLTV explicit offset converts to UTC", EpgService.TryTime("20261107090000 +0100", out var t) && t.UtcDateTime.Hour == 8);
            Check("XMLTV omitted timezone defaults to UTC", EpgService.TryTime("20261107090000", out t) && t.Offset == TimeSpan.Zero);
            Check("Bad dates are rejected", !EpgService.TryTime("20261399090000 +0100", out _));
            Check("DST fallback offsets remain distinct", EpgService.TryTime("20261025023000 +0200", out var a) && EpgService.TryTime("20261025023000 +0100", out var b) && b-a == TimeSpan.FromHours(1));
            var parsed = EpgService.Parse(bytes, "PL", "fixture", time, time);
            Check("Channels and consecutive programmes parsed without skipped elements", parsed.Channels.Count == 1 && parsed.Channels[0].Programmes.Count == 2);
            using var compressed = new MemoryStream(); using (var gz = new GZipStream(compressed, CompressionLevel.Fastest, true)) gz.Write(bytes);
            Check("Gzip XMLTV supported", EpgService.Parse(compressed.ToArray(),"PL","fixture",time,time).Channels[0].Programmes.Count == 2);
            bool rejected = false; try { EpgService.Parse(Encoding.UTF8.GetBytes(Fixture(time.AddDays(-10))), "PL", "fixture", time, time); } catch (InvalidDataException) { rejected = true; }
            Check("Old schedule rejected even with fresh HTTP date", rejected);
            rejected = false; try { EpgService.Parse(bytes, "PL", "fixture", time.AddDays(1), time); } catch (InvalidDataException) { rejected = true; }
            Check("Future HTTP date rejected", rejected);
            rejected = false; try { EpgService.Parse(Encoding.UTF8.GetBytes("<!DOCTYPE tv [<!ENTITY x SYSTEM 'file:///C:/Windows/win.ini'>]><tv><channel id='x'><display-name>&x;</display-name></channel></tv>"),"PL","fixture",time,time); } catch (Exception ex) when (ex is System.Xml.XmlException or InvalidDataException) { rejected = true; }
            Check("External entities cannot read local files", rejected);
            int calls = 0; bool offline = false;
            Task<(byte[], DateTimeOffset?)> Fetch(string u, CancellationToken ct) { ct.ThrowIfCancellationRequested(); calls++; if (offline) throw new IOException("offline fixture"); return Task.FromResult<(byte[],DateTimeOffset?)>((bytes, time)); }
            var service = new EpgService(folder, Fetch); await service.Refresh(["PL"], true, CancellationToken.None);
            var station = new Station("TVN", "https://example.org/live.m3u8", "TV", Country:"PL", ChannelId:"TVN.pl");
            var slot = service.Get(station, time);
            Check("Exact channel ID gets current and next programme", slot.Now?.Title == "Teraz • wiadomości" && slot.Next?.Title == "Potem • film");
            Check("Stop boundary switches to next programme", service.Get(station, slot.Now!.Stop).Now?.Title == "Potem • film");
            Check("Country mismatch never inherits Polish schedule", service.Get(station with { Country="DE" }).Match == null);
            Check("Unique name maps imported playlist without same ID", service.Get(station with { ChannelId="custom" }).Match?.Method.StartsWith("jednoznaczna") == true);
            Check("TVN7 never matches TVN", service.Get(station with { Name="TVN7", ChannelId="TVN7.pl" }).Match == null);
            Check("Time-shift/language feeds require explicit mapping", service.Get(station with { Feed="EnglishHD" }).Match == null && service.Get(station with { Name="TVN +1" }).Match == null);
            service.Settings.Matches[EpgService.MappingKey(station with { Feed="EnglishHD" })] = "TVN.pl"; service.SaveSettings();
            Check("Manual mapping overrides ambiguous feed deliberately", service.Get(station with { Feed="EnglishHD" }).Match?.Method == "ręczne dopasowanie");
            int before = calls; await service.Refresh(["PL"], false, CancellationToken.None);
            Check("Fresh cache avoids network download", calls == before);
            offline = true; await service.Refresh(["PL"], true, CancellationToken.None);
            Check("Offline keeps valid programmes and labels stale fetch", service.Get(station).Now != null && service.Notice.Contains("OFFLINE"));
            var restored = new EpgService(folder, Fetch); await restored.Refresh(["PL"], false, CancellationToken.None);
            Check("Cache and manual mappings survive restart", restored.Get(station with { Feed="EnglishHD" }).Now != null);
            service.Settings.Matches[EpgService.MappingKey(station)] = "!none";
            Check("User can explicitly disable wrong guide", service.Get(station).Match == null);
            service.Settings.Matches.Remove(EpgService.MappingKey(station));
            var secondary = new EpgService(Path.Combine(folder,"secondary"), (u,ct) => u.Contains("iptv-epg") ? throw new IOException("primary offline") : Task.FromResult<(byte[],DateTimeOffset?)>((bytes,time)));
            await secondary.Refresh(["PL"],true,CancellationToken.None);
            Check("Second EPG provider recovers from primary outage",secondary.Get(station).Now != null && secondary.Notice.Contains("zapasowego"));
            var overlap = new EpgService(Path.Combine(folder,"overlap"), (u,ct) => Task.FromResult<(byte[],DateTimeOffset?)>((Encoding.UTF8.GetBytes(Fixture(time).Replace("</tv>",Fixture(time).Split("<programme")[1].Split("</programme>")[0].Insert(0,"<programme").Replace("wiadomości","sprzeczna audycja")+"</programme></tv>")),time)));
            await overlap.Refresh(["PL"],true,CancellationToken.None);
            Check("Overlapping contradictory programmes do not get a fake now label",overlap.Get(station).Now == null && overlap.Get(station).Match != null);
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            rejected = false; try { await service.Refresh(["PL"], true, cancelled.Token); } catch (OperationCanceledException) { rejected = true; }
            Check("Cancellation preserves ready schedule", rejected && service.Get(station).Now != null);
            var duplicate = new EpgService(Path.Combine(folder,"ambiguous"), (u,ct) => Task.FromResult<(byte[],DateTimeOffset?)>((Encoding.UTF8.GetBytes(Fixture(time,"A","TVN").Replace("</tv>","") + Fixture(time,"B","TVN").Replace("<tv>","")),time)));
            await duplicate.Refresh(["PL"],true,CancellationToken.None);
            Check("Ambiguous names never choose arbitrary channel", duplicate.Get(station with { ChannelId="custom" }).Match == null);
            var tvn = station with { Homepage="https://tvn.pl/" }; var hbo = station with { Name="HBO", ChannelId="HBO.pl", Homepage="https://hbo.pl/" };
            var websites = OfficialViewing.Missing([tvn,hbo], []);
            Check("HBO and TVN have honest browser routes", websites.Count == 2 && websites.All(s=>s.BrowserOnly) && websites[0].Url.StartsWith("https://player.pl/live") && websites[1].AccessNote.Contains("pakietu"));
            Check("Browser routes are excluded from M3U", M3u.Parse(M3u.Export(websites.Append(station)),"https://example.org/list.m3u").Count == 1);
            Check("Existing stream is not counted twice as missing channel", OfficialViewing.Missing([tvn], [station]).Count == 0);
            var probe = await StreamProbe.Run(websites[0],Path.Combine(folder,"no-probe"),CancellationToken.None);
            Check("Browser link never launches player probe", !probe.Decoded && !Directory.Exists(Path.Combine(folder,"no-probe")));

            var directory = Enumerable.Range(0,50).Select(i=>new Station("Channel " + i, $"https://b{i}.example/", "TV", Country:i%2==0?"PL":"DE", ChannelId:"C"+i, Homepage:$"https://b{i}.example/")).ToList();
            var visits = new List<string>();
            Task<WebDocument> WebFetch(string u,CancellationToken ct) { ct.ThrowIfCancellationRequested(); if(u.Contains("search?")) return Task.FromResult(new WebDocument(u,"<rss><channel/></rss>","text/xml")); visits.Add(u); return Task.FromResult(new WebDocument(u,"<a href='/live'>Live</a>","text/html")); }
            var coverage = new DiscoveryCoverage(folder); var web = new WebDiscovery(WebFetch);
            await web.Search("","TV",[],directory,[],"https://search.example/search?q={query}",null,CancellationToken.None,coverage:coverage); var first = visits.Where(u=>new Uri(u).AbsolutePath=="/").ToHashSet(); coverage.Save(); visits.Clear();
            await web.Search("","TV",[],directory,[],"https://search.example/search?q={query}",null,CancellationToken.None,coverage:new DiscoveryCoverage(folder));
            Check("Subsequent search rotates broadcaster roots across restart", first.Count==8 && !visits.Where(u=>new Uri(u).AbsolutePath=="/").Any(first.Contains));
            visits.Clear(); await web.Search("","TV",[],directory,[],"https://search.example/search?q={query}",null,CancellationToken.None,WebSearchPlan.Deep);
            Check("Deep search actually visits more than former 24-page limit", visits.Distinct().Count()>24,visits.Distinct().Count());
            Check("Country selection retains both countries fairly", first.Count(u=>int.Parse(new Uri(u).Host.Split('.')[0][1..])%2==0)==4);
            string seed="https://raw.githubusercontent.com/example/list/main/playlist.m3u"; var listVisits=new List<string>();
            Task<WebDocument> Lists(string u,CancellationToken ct)
            {
                listVisits.Add(u);
                return Task.FromResult(new WebDocument(u,u.Contains("search?")?"<rss><channel>"+string.Join("",Enumerable.Range(0,10).Select(i=>$"<item><title>X</title><link>https://raw.githubusercontent.com/test/r/main/{i}.m3u</link></item>"))+"</channel></rss>":"#EXTM3U\n#EXTINF:-1 tvg-id=\"TVN.pl\",TVN\nhttps://example.org/live.m3u8","text/plain"));
            }
            await new SharedPlaylists(Lists).Search("",["PL"],[tvn],"https://search.example/search?q={query}",[seed],null,CancellationToken.None);
            Check("Published seed is read before search requests and result playlists",listVisits.First()==seed);
            if(network)
            {
                var live=new EpgService(Path.Combine(folder,"production")); await live.Refresh(["PL","DE"],true,CancellationToken.None);
                foreach(var country in new[]{"PL","DE"}) Check("Production XMLTV parses current schedule: "+country,live.Feeds.Any(f=>f.Country==country && f.Channels.Count>50),live.Notice);
                foreach(var s in new[]{station,hbo}) { var actual=live.Get(s); Check("Production now/next matches "+s.ChannelId,actual.Now!=null && actual.Next!=null,new{channel=actual.Match?.Channel.Id,current=actual.Now,next=actual.Next,source=actual.Match?.Feed.Source,downloaded=actual.Match?.Feed.Downloaded}); }
                var catalogue = new SmartCatalog(Path.Combine(folder,"production-catalog"));
                var index = await catalogue.Broadcasters(true,CancellationToken.None);
                var actualWeb = await new WebDiscovery().Search("TVN","TV",["PL"],index.Stations,[],catalogue.WebSearchTemplate,null,CancellationToken.None,WebSearchPlan.Deep,new DiscoveryCoverage(Path.Combine(folder,"production-coverage")));
                Check("Production deep search attempts TVN broadcaster pages and reports actual failures",actualWeb.Evidence.Any(e=>e.Stage is "Strona nadawcy" or "BŁĄD strony") && actualWeb.Evidence.Any(e=>e.Stage=="Zakres"),new{streams=actualWeb.Stations.Count,evidence=actualWeb.Evidence});
            }
        }
        try { Test().GetAwaiter().GetResult(); } catch(Exception ex) { Check("Unexpected error",false,ex.ToString()); }
        File.WriteAllText(report,JsonSerializer.Serialize(new{success,checks},new JsonSerializerOptions{WriteIndented=true})); return success?0:1;
    }
}

public sealed partial class MainForm
{
    public async Task EpgUiTest(Action<string,bool,object?> check)
    {
        var old = epg; var oldCandidates=candidates; var oldKnown=knownChannels; string oldSearch=search.Text;
        try
        {
            var time=DateTimeOffset.UtcNow; epg=new(Path.Combine(dataFolder,"epg-ui-fixture"),(u,ct)=>Task.FromResult<(byte[],DateTimeOffset?)>((Encoding.UTF8.GetBytes(EpgTests.Fixture(time)),time)));
            await epg.Refresh(["PL"],true,CancellationToken.None);
            candidates=[new("TVN HD","https://example.org/tvn.m3u8","TV",Country:"PL",ChannelId:"TVN.pl")]; knownChannels=[]; search.Text=""; lastQuery=""; ShowRecommendations();
            check("EPG now and next are attached to channel rows",results.Items.Count==1 && results.SmallImageList?.ImageSize.Height>=60 && results.Items[0].ToolTipText.Contains("wiadomości") && results.Items[0].ToolTipText.Contains("film"),results.Items[0].ToolTipText);
            var order=shown.Select(s=>s.Url).ToArray(); RefreshEpgTooltips(); results.Invalidate();
            check("EPG refresh does not reorder/remove selected channels",order.SequenceEqual(shown.Select(s=>s.Url)) && Selected?.ChannelId=="TVN.pl",null);
            candidates=[]; knownChannels=[new("TVN","https://tvn.pl/","TV",Homepage:"https://tvn.pl/",Country:"PL",ChannelId:"TVN.pl")]; search.Text="TVN"; ShowRecommendations();
            check("Broadcaster websites never become playable channel rows",shown.Count==0 && WorkingCount(shown)==0,null);
            var legacyWeb=OfficialViewing.Missing(knownChannels,[]).Single();
            Play(legacyWeb);
            check("Legacy WWW item is rejected without browser launch or native playback",pendingStation==null && status.Text.Contains("stroną WWW"),status.Text);
        }
        finally {epg=old;candidates=oldCandidates;knownChannels=oldKnown;search.Text=oldSearch;lastQuery=oldSearch;ShowRecommendations();}
    }
}
