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
        var primary=exact.FirstOrDefault()??seeds.FirstOrDefault();string requested=query.Trim();if(requested.Length==0)requested=primary?.Name??"public TV";
        string country=primary?.Country??countries.FirstOrDefault()??"";
        string native=country switch{"PL"=>"na żywo","DE" or "AT" or "CH"=>"Livestream Fernsehen","FR"=>"télévision en direct","ES"=>"televisión en directo","IT"=>"TV diretta","PT" or "BR"=>"TV ao vivo",_=>"live TV"};
        var countryNames=Countries.Load().ToDictionary(c=>c.Code,c=>c.Name);string region=string.Join(' ',countries.Take(3).Select(c=>countryNames.GetValueOrDefault(c,c)));
        List<string> result=[requested+" "+(mode=="Radio"?"radio listen live official":"TV live official")+" "+region];
        if(primary!=null&&PublicWeb.Allowed(primary.Url))result.Add("site:"+new Uri(primary.Url).Host+" "+requested+" "+native);
        result.Add(requested+" "+native+" "+region);
        if(primary!=null)result.AddRange(ChannelSearch.Names(primary).Where(n=>ChannelSearch.Key(n)!=ChannelSearch.Key(requested)).Distinct().Take(3).Select(n=>n+" "+native+" "+region));
        result.Add(requested+" livestream watch online "+region);
        return result.Select(q=>q.Trim()).Distinct().Take(limit).ToList();
    }
}
