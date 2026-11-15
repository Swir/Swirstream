using System.Text;
using System.Text.RegularExpressions;

namespace SwirStreamAI;

public sealed class XtreamAccount
{
    public string Server {get;}
    public string Username {get;}
    public string Password {get;}
    public bool PlaylistLink {get;}
    public XtreamAccount(string server,string username,string password,bool playlistLink=false)
    {Server=server;Username=username;Password=password;PlaylistLink=playlistLink;}
    public override string ToString()=>"Xtream • "+new Uri(Server).Authority;
}

// Local parsing only. No clipboard reads, network, storage or credential logging.
public static class XtreamLink
{
    static readonly UTF8Encoding Utf8=new(false,true);
    static readonly Regex Endpoint=new(@"/(get|player_api|xmltv)\.php$",RegexOptions.IgnoreCase);
    static readonly Regex MediaId=new(@"^\d+\.(?:ts|m3u8|mp4)$",RegexOptions.IgnoreCase);
    static InvalidDataException Invalid(string text)=>new(text);
    public static string Clean(string value)
    {
        var text=value.Trim();if(text.Length>=2&&text[0]==text[^1]&&text[0] is '\"' or '\'')text=text[1..^1].Trim();
        if(text.Length==0||text.Length>8192||text.Any(char.IsControl))throw Invalid("Wklej jeden pełny adres HTTP/HTTPS, bez dodatkowego tekstu.");
        // Forum HTML separators, before decoding individual values exactly once.
        int query=text.IndexOf('?');if(query>=0)text=text[..query]+Regex.Replace(text[query..],@"&amp;(?=[A-Za-z0-9_.%~-]+=)","&",RegexOptions.IgnoreCase);
        for(int i=0;i<text.Length;i++)if(text[i]=='%'&&(i+2>=text.Length||!Uri.IsHexDigit(text[i+1])||!Uri.IsHexDigit(text[i+2])))throw Invalid("Link ma nieprawidłowe kodowanie procentowe. Skopiuj cały adres ponownie.");
        return text;
    }
    static string Decode(string text,bool form)
    {
        try
        {
            using var bytes=new MemoryStream();
            for(int i=0;i<text.Length;)
            {
                if(text[i]=='%'){bytes.WriteByte(Convert.ToByte(text.Substring(i+1,2),16));i+=3;}
                else{int end=text.IndexOf('%',i);if(end<0)end=text.Length;string raw=text[i..end];if(form)raw=raw.Replace('+',' ');bytes.Write(Utf8.GetBytes(raw));i=end;}
            }
            string value=Utf8.GetString(bytes.ToArray());if(value.Any(char.IsControl))throw Invalid("Dane konta zawierają niedozwolone znaki sterujące.");return value;
        }
        catch(Exception ex)when(ex is FormatException or ArgumentException){throw Invalid("Link zawiera nieprawidłowo zakodowane dane konta.");}
    }
    static Dictionary<string,string> Query(string query)
    {
        var values=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);if(query.Length==0)return values;
        if(query.Contains(';'))throw Invalid("Nieobsługiwany separator parametrów linku. Wybierz dokładny adres jako M3U albo popraw link operatora.");
        foreach(string part in query.TrimStart('?').Split('&'))
        {
            if(part.Length==0)throw Invalid("Link ma pusty albo powtórzony separator parametrów.");int equal=part.IndexOf('=');if(equal<=0)throw Invalid("Link ma niepełne parametry konta.");
            string key=Decode(part[..equal],true),value=Decode(part[(equal+1)..],true);
            if(!values.TryAdd(key,value))throw Invalid("Link powtarza parametry. Nie można jednoznacznie wybrać danych konta.");
        }
        return values;
    }
    static void Controls(Dictionary<string,string> values,string endpoint)
    {
        foreach(var pair in values)
        {
            if(pair.Key.Equals("username",StringComparison.OrdinalIgnoreCase)||pair.Key.Equals("password",StringComparison.OrdinalIgnoreCase))continue;
            string value=pair.Value.ToLowerInvariant();
            bool valid=pair.Key.Equals("type",StringComparison.OrdinalIgnoreCase)&&value is "m3u" or "m3u_plus"
                ||pair.Key.Equals("output",StringComparison.OrdinalIgnoreCase)&&value is "ts" or "m3u8" or "hls"
                ||endpoint.Equals("player_api",StringComparison.OrdinalIgnoreCase)&&pair.Key.Equals("action",StringComparison.OrdinalIgnoreCase)&&value is "get_live_streams" or "get_live_categories";
            if(!valid)throw Invalid("Link ma dodatkowe parametry, których Xtream nie może bezpiecznie pominąć. Możesz użyć dokładnego adresu jako własnej M3U.");
        }
    }
    static string Base(Uri uri,string path)=>uri.GetLeftPart(UriPartial.Authority)+path.TrimEnd('/')+"/";
    public static string Server(string value)
    {
        var uri=PrivateProviderClient.Url(Clean(value));if(uri.Query.Length>0)throw Invalid("Rozpoznaj pełny link konta lub podaj sam serwer bez parametrów.");
        string path=uri.AbsolutePath.TrimEnd('/');var match=Endpoint.Match(path);if(match.Success)path=path[..match.Index];
        return Base(uri,path);
    }
    public static XtreamAccount? Parse(string value,bool explicitXtream=false)
    {
        var text=Clean(value);var uri=PrivateProviderClient.Url(text);string path=uri.AbsolutePath.TrimEnd('/');var endpoint=Endpoint.Match(path);
        var segments=path.Split('/');var live=Enumerable.Range(0,segments.Length).Where(i=>segments[i].Equals("live",StringComparison.OrdinalIgnoreCase)).ToArray();
        if(live.Length>0&&live[^1]+4==segments.Length&&MediaId.IsMatch(segments[^1]))
        {
            if(live.Length!=1||uri.Query.Length>0)throw Invalid("Adres kanału ma niejednoznaczne dane lub dodatkowe parametry. Użyj go jako pojedynczej transmisji.");
            string user=Decode(segments[live[0]+1],false),password=Decode(segments[live[0]+2],false);CheckCredentials(user,password);
            return new(Base(uri,string.Join('/',segments.Take(live[0]))),user,password);
        }
        bool accountQuery=uri.Query.TrimStart('?').Split('&').Any(part=>{int i=part.IndexOf('=');string key=Decode(i<0?part:part[..i],true);return key.Equals("username",StringComparison.OrdinalIgnoreCase)||key.Equals("password",StringComparison.OrdinalIgnoreCase);});
        if(!accountQuery)return null; // Generic own playlists may legally repeat non-account query fields.
        var values=Query(uri.Query);bool hasUser=values.TryGetValue("username",out var username),hasPass=values.TryGetValue("password",out var pass);
        if(!hasUser&&!hasPass)return null;
        if(!hasUser||!hasPass)throw Invalid("Link musi zawierać jeden login i jedno hasło. Brakuje części danych.");
        if(!endpoint.Success&&(!explicitXtream||Path.HasExtension(path)&&!uri.AbsolutePath.EndsWith('/')))throw Invalid("Ten adres konta jest niejednoznaczny. Wybierz jawnie Xtream dla serwera bazowego albo własną M3U dla dokładnego URL.");
        Controls(values,endpoint.Success?endpoint.Groups[1].Value:"");CheckCredentials(username!,pass!);
        return new(Base(uri,endpoint.Success?path[..endpoint.Index]:path),username!,pass!,endpoint.Success&&endpoint.Groups[1].Value.Equals("get",StringComparison.OrdinalIgnoreCase));
    }
    static void CheckCredentials(string user,string password)
    {if(string.IsNullOrWhiteSpace(user)||string.IsNullOrWhiteSpace(password)||user.Length>1024||password.Length>4096)throw Invalid("Link ma pusty albo zbyt długi login lub hasło.");}
    public static ProviderSource Normalize(ProviderSource source)
    {
        if(source.Kind!=PrivateSourceKind.Xtream)return source;
        var parsed=Parse(source.Url,true);
        if(parsed==null)return source with{Url=Server(source.Url)};
        if(source.Username.Length>0&&source.Username!=parsed.Username||source.Password.Length>0&&source.Password!=parsed.Password)throw Invalid("Dane z linku różnią się od wpisanych pól. Rozpoznaj link i zatwierdź właściwe dane przed połączeniem.");
        return source with{Url=parsed.Server,Username=parsed.Username,Password=parsed.Password};
    }
    public static bool PrivateInput(string text)
    {
        // Cheap guard before any public search/report, including incomplete pasted links.
        string decoded=text;for(int i=0;i<2;i++){try{decoded=Uri.UnescapeDataString(decoded);}catch{break;}}
        return Regex.IsMatch(decoded.Trim().Trim('\"','\''),@"^(?i)https?[:%]")||decoded.Contains("://")||PublicPlaylistPrivacy.ContainsCredentials(decoded)||Regex.IsMatch(decoded,@"(?i)(?:username|password|passwd|access_token|token|auth|authorization|signature|api_key)\s*=|/live/[^/\s]+/[^/\s]+/|https?://[^/\s]+@");
    }
}
