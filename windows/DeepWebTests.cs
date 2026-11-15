using System.Text.Json;
namespace SwirStreamAI;
public static class DeepWebTests
{
    public static int Run(string report)
    {
        bool success=true;List<object> checks=[];void Check(string name,bool ok,object? detail=null){success&=ok;checks.Add(new{name,ok,detail});}
        try
        {
            var hits=SearchExpansion.ParseDuck("<a class='result__a' href='//duckduckgo.com/l/?uddg=https%3A%2F%2Fnews.example%2Flive&amp;rut=x'>News <b>live</b></a><a class='result__a' href='http://127.0.0.1/live'>private</a><a class='result__a' href='https://user:secret@news.example/live'>credentials</a>");
            Check("DDG redirects decoded; private and credential URLs refused",hits.Count==1&&hits[0].Url=="https://news.example/live");
            bool rejected=false;try{SearchExpansion.ParseDuck("<form id='challenge-form'>captcha</form>");}catch(InvalidDataException){rejected=true;}Check("Challenge is reported without bypass",rejected);
            Station news=new("News","https://news.example/",Country:"PL",ChannelId:"News.pl",Homepage:"https://news.example/");
            var requests=new List<string>();const string live="#EXTM3U\n#EXT-X-MEDIA-SEQUENCE:42\n#EXTINF:4,\nsegment.ts";
            Task<WebDocument> Fetch(string url,CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();requests.Add(url);if(url.Contains("search.example"))throw new HttpRequestException("503");
                string body=url.Contains("duckduckgo.com")?"<a class='result__a' href='https://news.example/live'>News live</a><a class='result__a' href='https://unknown.example/live'>News live</a>":url.EndsWith(".m3u8")?live:url.EndsWith("/live")?"<source src='https://cdn.example/live.m3u8'>":"<title>News</title>";
                return Task.FromResult(new WebDocument(url,body,"text/html"));
            }
            var found=new WebDiscovery(Fetch).Search("News","TV",["PL"],[news],[],"https://search.example/?q={query}",null,CancellationToken.None,WebSearchPlan.Deep).GetAwaiter().GetResult();
            Check("Independent engine finds actual manifest despite primary outage",found.Stations.Count==1&&found.Stations[0].Country=="PL",found.Evidence);
            Check("Specific live page precedes generic homepage",requests.First(u=>!u.Contains("search.example")&&!u.Contains("duckduckgo.com"))=="https://news.example/live");
            Check("Unrecognized domain not crawled and primary failure not repeated",!requests.Contains("https://unknown.example/live")&&requests.Count(u=>u.Contains("search.example"))==1);
            requests.Clear();Task<WebDocument> Nested(string url,CancellationToken ct)
            {
                requests.Add(url);string body=url.Contains("search.example")?"<rss><channel/></rss>":url.Contains("duckduckgo.com")?"<div class='no-results'>No results</div>":url.EndsWith(".m3u8")?live:url==news.Url?"<a href='/live'>Live</a>":url.EndsWith("/live")?"<a href='/live/player'>Player</a>":url.EndsWith("/player")?"<iframe src='/live/player/embed'></iframe>":"<source src='https://cdn.example/live.m3u8'><a href='/live/player/embed/deeper'>More</a>";return Task.FromResult(new WebDocument(url,body,"text/html"));
            }
            found=new WebDiscovery(Nested).Search("News","TV",["PL"],[news],[],"https://search.example/?q={query}",null,CancellationToken.None,WebSearchPlan.Deep).GetAwaiter().GetResult();
            Check("Deep crawl reaches third-level player but respects depth limit",found.Stations.Count==1&&requests.Contains("https://news.example/live/player/embed")&&!requests.Contains("https://news.example/live/player/embed/deeper"));
            var named=new Station("Das Erste","https://daserste.example/",Country:"DE",Aliases:["ARD"]);var plan=SearchExpansion.Queries("ARD","TV",["DE"],[named],[named],8);
            Check("Canonical names and native language expand query",plan.Any(q=>q.Contains("Das Erste"))&&plan.Any(q=>q.Contains("Livestream Fernsehen"))&&plan.Count<=8,plan);
            string playlist="https://raw.githubusercontent.com/public/channels/main/news.m3u";requests.Clear();
            Task<WebDocument> PlaylistFetch(string url,CancellationToken ct){requests.Add(url);if(url.Contains("search.example"))throw new HttpRequestException("429");return Task.FromResult(new WebDocument(url,url.Contains("duckduckgo.com")?$"<a class='result__a' href='{playlist}'>News public playlist</a>":"#EXTM3U\n#EXTINF:-1 tvg-id='News.pl',News\nhttps://cdn.example/live.m3u8","text/plain"));}
            var lists=new SharedPlaylists(PlaylistFetch).Search("News",["PL"],[news],"https://search.example/?q={query}",[],null,CancellationToken.None,true).GetAwaiter().GetResult();
            Check("Playlist discovery also uses independent engine",lists.Stations.Count==1&&requests.Contains(playlist),lists.Evidence);
            var withHeaders=news with {Url="https://cdn.example/live.m3u8",UserAgent="PublishedAgent/1",Referrer="https://news.example/player"};var plain=withHeaders with {UserAgent="",Referrer=""};
            Check("Late M3U cannot erase API playback headers",MainForm.MergeStations([plain],[withHeaders]).Single() is var keep&&keep.UserAgent==withHeaders.UserAgent&&keep.Referrer==withHeaders.Referrer);
            Check("Late API enriches earlier M3U without duplicating URL",MainForm.MergeStations([withHeaders],[plain]) is var merged&&merged.Count==1&&merged[0].UserAgent==withHeaders.UserAgent&&merged[0].Referrer==withHeaders.Referrer);
            Check("Newer explicitly published headers replace older ones",MainForm.MergeStations([withHeaders with {UserAgent="PublishedAgent/2"}],[withHeaders]).Single().UserAgent=="PublishedAgent/2");
        }
        catch(Exception ex){Check("Unexpected failure",false,ex.ToString());}
        File.WriteAllText(report,JsonSerializer.Serialize(new{success,checks},new JsonSerializerOptions{WriteIndented=true}));return success?0:1;
    }
}
