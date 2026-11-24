using System.Net;
using System.Text.RegularExpressions;

namespace SwirStreamAI;

public static class SearchExpansion
{
    public const string DuckTemplate = "https://html.duckduckgo.com/html/?q={query}";
    public static List<WebHit> ParseDuck(string html)
    {
        if (html.Length > 2_000_000 || new[]{"anomaly.js","challenge-form","g-recaptcha"}.Any(s=>html.Contains(s,StringComparison.OrdinalIgnoreCase))) throw new InvalidDataException("DuckDuckGo wymaga weryfikacji albo zmieniło stronę. Pominięto usługę.");
        List<WebHit> hits=[];
        foreach(Match link in Regex.Matches(html,"<a\\b([^>]{0,8192})>([\\s\\S]{0,8192}?)</a\\s*>",RegexOptions.IgnoreCase,TimeSpan.FromSeconds(2)))
        {
            var fields=Regex.Matches(link.Groups[1].Value,"([a-z_-]+)\\s*=\\s*([\"'])(.*?)\\2",RegexOptions.IgnoreCase).GroupBy(m=>m.Groups[1].Value.ToLowerInvariant()).ToDictionary(g=>g.Key,g=>g.First().Groups[3].Value);
            if(!fields.GetValueOrDefault("class","").Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries).Contains("result__a") || !fields.TryGetValue("href",out var href))continue;
            if(!Uri.TryCreate(new Uri("https://html.duckduckgo.com/"),WebUtility.HtmlDecode(href),out var address))continue;
            if(address.Host is "duckduckgo.com" or "www.duckduckgo.com")
            {
                if(address.AbsolutePath!="/l/")continue;
                var target=address.Query.TrimStart('?').Split('&').Select(p=>p.Split('=',2)).FirstOrDefault(p=>p.Length==2&&p[0]=="uddg");
                if(target==null||!Uri.TryCreate(WebUtility.UrlDecode(target[1]),UriKind.Absolute,out address))continue;
            }
            if(!PublicWeb.Allowed(address.AbsoluteUri)||address.Host.EndsWith("duckduckgo.com",StringComparison.OrdinalIgnoreCase))continue;
            hits.Add(new(WebUtility.HtmlDecode(Regex.Replace(link.Groups[2].Value,"<[^>]*>"," ")).Trim(),address.AbsoluteUri));if(hits.Count==10)break;
        }
        if(hits.Count==0&&!html.Contains("no-results",StringComparison.OrdinalIgnoreCase)&&!html.Contains("no results",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Nie rozpoznano wyników DuckDuckGo.");
        return hits.DistinctBy(h=>h.Url).ToList();
    }
    public static bool LivePage(string url)=>new[]{"live","na-zywo","na_zywo","player","embed","stream","direct","diretta","ao-vivo"}.Any(s=>url.Contains(s,StringComparison.OrdinalIgnoreCase));
    public static List<string> Queries(string query,string mode,string[] countries,IReadOnlyList<Station> exact,IReadOnlyList<Station> seeds,int limit)
    {
        bool radio = mode == "Radio";
        string requested = query.Trim();
        var primary = exact.FirstOrDefault() ?? seeds.FirstOrDefault();
        string country = primary?.Country ?? countries.FirstOrDefault() ?? "";
        string region = WorldMarkets.Name(country), native = WorldMarkets.Terms(country, radio).First();
        string generic = radio ? "radio listen live official" : "TV live official";
        List<string> result = [requested+" "+generic+" "+(countries.Length == 1 ? region : "")];
        if(primary!=null && PublicWeb.Allowed(primary.Url))
            result.Add("site:"+new Uri(primary.Url).Host+" "+(requested.Length == 0 ? primary.Name : requested)+" "+native);
        var selectedMarkets = countries.ToHashSet(StringComparer.Ordinal);
        var markets = seeds.Select(s => s.Country).Where(c => selectedMarkets.Contains(c)).Concat(countries).Distinct();
        if (countries.Length == 0) markets = seeds.Select(s => s.Country).Where(c => c.Length > 0).Distinct();
        foreach (string selected in markets.Where(c => c != country).Take(Math.Max(0,limit-result.Count-1)))
            result.Add(requested + " " + WorldMarkets.Terms(selected,radio).First() + " " + WorldMarkets.Name(selected));
        result.Add(requested+" "+native+" "+region);
        if(primary!=null)result.AddRange(ChannelSearch.Names(primary).Where(n=>ChannelSearch.Key(n)!=ChannelSearch.Key(requested)).Distinct().Take(3).Select(n=>n+" "+native+" "+region));
        foreach(string alternative in WorldMarkets.Terms(country,radio).Skip(1))
            result.Add(requested+" "+alternative+" "+region);
        result.Add(requested+" livestream watch online "+region);
        return result.Select(q=>q.Trim()).Distinct().Take(limit).ToList();
    }
}
