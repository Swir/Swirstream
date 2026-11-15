using System.Net;
using System.Text.RegularExpressions;

namespace SwirStreamAI;

// Public discovery must never adopt account credentials found in someone else's
// published playlist. Manual provider imports have a separate encrypted store.
public record PublicPlaylistClean(string Body, string OriginalSha256, int RemovedEntries, bool Sanitized);

public static class PublicPlaylistPrivacy
{
    static readonly Regex Query = new(@"[?&;](?:password|passwd|authorization|mac)\s*=\s*[^&\s#\""'<>]+", RegexOptions.IgnoreCase);
    static readonly Regex UserInfo = new(@"https?://[^/\s\""'<>]+@", RegexOptions.IgnoreCase);
    // A numeric final stream ID distinguishes the usual Xtream account path from
    // ordinary public paths such as /live/channel/hls/master.m3u8.
    static readonly Regex AccountPath = new(@"/(?:live|movie|series)/[^/\s?\""'<>]+/[^/\s?\""'<>]+/\d+(?:\.(?:m3u8?|ts|mp4|mkv|avi))?(?=[?&#|\s\""'<>]|$)", RegexOptions.IgnoreCase);
    static readonly Regex Header = new(@"(?:^#EXTVLCOPT:\s*http-(?:cookie|authorization)\s*=|[|&](?:Authorization|Cookie)\s*=|[\""'](?:Authorization|Cookie)[\""']\s*:|^#(?:Authorization|Cookie)\s*:|^#KODIPROP:[^\r\n]*license_key\s*=|^#KODIPROP:[^\r\n]*(?:stream_headers|manifest_headers)\s*=[^\r\n]*(?:Authorization|Cookie)\s*=)\s*[^\r\n]+", RegexOptions.IgnoreCase | RegexOptions.Multiline);
    public static bool ContainsCredentials(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        string decoded = WebUtility.HtmlDecode(text);
        // Decode escaped query/header names, including one nested escaping layer.
        for (int i = 0; i < 2 && decoded.Contains('%'); i++) decoded = Uri.UnescapeDataString(decoded);
        return Query.IsMatch(decoded) || UserInfo.IsMatch(decoded) || AccountPath.IsMatch(decoded) || Header.IsMatch(decoded);
    }
    public static PublicPlaylistClean SanitizeM3u(string url, string body)
    {
        if (ContainsCredentials(url)) throw new InvalidDataException("Publiczny adres playlisty zawiera dane konta.");
        string digest=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
        if (!ContainsCredentials(body)) return new(body,digest,0,false);
        var output=new List<string>();var pending=new List<string>();int removed=0;
        foreach(string raw in body.Replace("\r\n","\n").Split('\n'))
        {
            string line=raw.Trim().TrimStart('\uFEFF');
            if(line.StartsWith("#EXTM3U",StringComparison.OrdinalIgnoreCase))
            {
                // Credential-bearing EPG/header configuration is never exported.
                output.Add(ContainsCredentials(line)?"#EXTM3U":raw);continue;
            }
            if(line.Length==0||line.StartsWith('#')){pending.Add(raw);continue;}
            pending.Add(raw);
            if(pending.Any(ContainsCredentials))removed++;else output.AddRange(pending);
            pending.Clear();
        }
        if(!pending.Any(ContainsCredentials))output.AddRange(pending);
        return new(string.Join('\n',output),digest,removed,true);
    }
    public static void RequirePublic(string url, string body)
    {
        if (ContainsCredentials(url) || ContainsCredentials(body))
            throw new InvalidDataException("Pominięto publiczną playlistę zawierającą dane dostępu. Własne konta dodaj w Moich źródłach.");
    }
}
