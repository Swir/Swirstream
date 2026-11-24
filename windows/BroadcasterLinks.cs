using System.Net;
using System.Text.RegularExpressions;

namespace SwirStreamAI;

internal static class BroadcasterLinks
{
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    private static readonly Regex Tag = new(@"<(a|iframe)\b([^<>]{0,8192})>", Options);
    private static readonly Regex Attribute = new("""\s(href|src|title|aria-label)\s*=\s*(["'])(.*?)\2""", Options | RegexOptions.Singleline);
    private static readonly Regex Signal = new(@"(?<![\p{L}\p{N}])(?:live(?:stream|streams|tv)?|player|embed|stream(?:ing)?|na[-_ ]?[żz]ywo|direc(?:t|to|tos)|dirett[ae]|ao[-_ ]vivo|en[-_ ]vivo|zive|živě|naživo|наживо|uživo|уживо|canlı|direkte|direktsänd|trực tiếp|siaran langsung|прямой эфир|مباشر|שידור חי|ζωντανά|tiešraidē|tiesiogiai|suora lähetys)(?![\p{L}\p{N}])|(?:直播(?!回放)|生放送|ライブ配信|실시간|생방송|ถ่ายทอดสด|लाइव|সরাসরি)", Options);
    private static readonly Regex Excluded = new(@"(?:^|[/_-])(?:replay|catchup|vod|podcasts?|archiwum|nachrichtenarchiv)(?:[/_.-]|$)|\.(?:js|css|png|jpe?g|webp|svg|gif|ico|mp4|pdf)$", Options);

    internal static int PathScore(Uri address)
    {
        string path = address.AbsolutePath.TrimEnd('/');
        if (Excluded.IsMatch(path)) return -1;
        string tail = Uri.UnescapeDataString(path[(path.LastIndexOf('/') + 1)..]);
        if (tail.Length <= 30 && Signal.IsMatch(tail)) return 3;
        if (tail.Length <= 40 && (path.Contains("/live-tv/",StringComparison.OrdinalIgnoreCase) || path.Contains("/live-radio/",StringComparison.OrdinalIgnoreCase))) return 3;
        return Signal.IsMatch(Uri.UnescapeDataString(path)) || Signal.IsMatch(address.Host.Split('.')[0]) ? 1 : 0;
    }

    internal static List<string> Find(WebDocument doc, bool embedded, int limit, Func<string, bool> inScope)
    {
        var origin = new Uri(doc.Url);
        var ranks = new Dictionary<string, int>();
        foreach (Match tag in Tag.Matches(doc.Body))
        {
            if (tag.Groups[1].Value.Equals("iframe", StringComparison.OrdinalIgnoreCase) != embedded) continue;
            string target = "", label = "";
            foreach (Match attribute in Attribute.Matches(tag.Groups[2].Value))
            {
                string key = attribute.Groups[1].Value.ToLowerInvariant();
                string value = WebUtility.HtmlDecode(attribute.Groups[3].Value);
                if (key == (embedded ? "src" : "href")) target = value.Trim();
                if (key is "title" or "aria-label") label += " " + value;
            }
            if (target.Length == 0 || target.StartsWith('#') || !Uri.TryCreate(origin, target, out var address)) continue;
            string url = address.GetLeftPart(UriPartial.Query);
            if (!PublicWeb.Allowed(url) || PublicPlaylistPrivacy.ContainsCredentials(url) || SharedPlaylists.RequiresBrowser(url) ||
                url == origin.GetLeftPart(UriPartial.Query) || !inScope(url)) continue;
            int score = PathScore(address);
            if (score < 0) continue;
            if (!embedded)
            {
                int start = tag.Index + tag.Length;
                int end = doc.Body.IndexOf("</a", start, Math.Min(512, doc.Body.Length - start), StringComparison.OrdinalIgnoreCase);
                if (end >= start)
                {
                    string body = doc.Body[start..end];
                    if (!Regex.IsMatch(body, @"<a\b", Options))
                        label += " " + WebUtility.HtmlDecode(Regex.Replace(body, "<[^>]*>", " "));
                }
            }
            if (label.Length <= 240 && Signal.IsMatch(label)) score += 2;
            if (score > 0) ranks[url] = Math.Max(score, ranks.GetValueOrDefault(url));
        }
        return ranks.OrderByDescending(pair => pair.Value).Take(limit).Select(pair => pair.Key).ToList();
    }
}
