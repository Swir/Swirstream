using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace SwirStreamAI;

public record ProviderRequest(string Url,string Method="GET",string Body="",Dictionary<string,string>? Headers=null)
{internal bool PortalChannelCatalog {get;init;}}
public record ProviderDocument(byte[] Bytes,string Url,string ContentType="",DateTimeOffset? Modified=null,int Status=200,string Redirect="")
{
    public string Text=>Encoding.UTF8.GetString(Bytes);
    internal PortalChannelBatch? PortalChannels {get;init;}
    public long BodyBytes=>PortalChannels?.RawBytes??Bytes.LongLength;
    public PortalCatalogReadInfo? CatalogReadInfo=>PortalChannels?.Info;
}

// Explicitly configured personal sources only. Never used by the public crawler.
public sealed class PrivateProviderClient
{
    readonly Func<ProviderRequest,CancellationToken,Task<ProviderDocument>> transport;
    readonly Func<string,CancellationToken,Task<IPAddress[]>> mediaDns;
    static readonly HttpClient http=CreateHttp();
    static HttpClient CreateHttp()
    {
        var handler=new SocketsHttpHandler{AllowAutoRedirect=false,UseCookies=false,UseProxy=false,ConnectTimeout=TimeSpan.FromSeconds(8),AutomaticDecompression=DecompressionMethods.All};
        handler.ConnectCallback=async(context,ct)=>
        {
            var addresses=await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host,ct);
            foreach(var address in addresses.Where(PublicWeb.IsPublic))
            {
                var socket=new Socket(address.AddressFamily,SocketType.Stream,ProtocolType.Tcp);
                try{await socket.ConnectAsync(new IPEndPoint(address,context.DnsEndPoint.Port),ct);return new NetworkStream(socket,true);}
                catch{socket.Dispose();ct.ThrowIfCancellationRequested();}
            }
            throw new HttpRequestException("Brak publicznego adresu źródła.");
        };
        return new(handler){Timeout=Timeout.InfiniteTimeSpan};
    }
    public PrivateProviderClient(Func<ProviderRequest,CancellationToken,Task<ProviderDocument>>? transport=null)
    {this.transport=transport??Send;mediaDns=(host,ct)=>Dns.GetHostAddressesAsync(host,ct);}
    internal PrivateProviderClient(Func<ProviderRequest,CancellationToken,Task<ProviderDocument>> transport,Func<string,CancellationToken,Task<IPAddress[]>> mediaDns):this(transport)=>this.mediaDns=mediaDns;
    public static Uri Url(string value)
    {
        if(!Uri.TryCreate(value,UriKind.Absolute,out var uri)||uri.Scheme is not("http" or "https")||uri.UserInfo.Length>0||uri.Fragment.Length>0)throw new InvalidDataException("Podaj adres HTTP/HTTPS bez danych logowania przed nazwą serwera.");
        if(uri.IsLoopback||IPAddress.TryParse(uri.Host,out var address)&&!PublicWeb.IsPublic(address)||!uri.Host.Contains('.')||new[]{".local",".localhost",".internal",".lan",".home"}.Any(x=>uri.Host.EndsWith(x,StringComparison.OrdinalIgnoreCase)))throw new InvalidDataException("Źródła sieciowe wymagają publicznego adresu serwera; adresy lokalnej sieci nie są obsługiwane.");
        return uri;
    }
    static bool SameOrigin(string a,string b){var x=Url(a);var y=Url(b);return x.Scheme==y.Scheme&&x.Host==y.Host&&x.Port==y.Port;}
    internal static HttpRequestMessage BuildRequest(ProviderRequest request)
    {
        var message=new HttpRequestMessage(new HttpMethod(request.Method),request.Url);
        try
        {
            message.Headers.UserAgent.ParseAdd("SwirStreamAI/0.12.0");
            foreach(var pair in request.Headers??[])
            {
                if(pair.Key.Any(c=>c<=32||c>=127)||pair.Value.Any(c=>c is '\r' or '\n' or '\0'))throw new InvalidDataException("Nieobsługiwane nagłówki źródła.");
                if(pair.Key.Equals("User-Agent",StringComparison.OrdinalIgnoreCase))
                {
                    message.Headers.UserAgent.Clear();
                    if(!message.Headers.UserAgent.TryParseAdd(pair.Value))throw new InvalidDataException("Nieobsługiwany User-Agent źródła.");
                }
                else if(!message.Headers.TryAddWithoutValidation(pair.Key,pair.Value))throw new InvalidDataException("Nieobsługiwane nagłówki źródła.");
            }
            if(request.Body.Length>0)message.Content=new StringContent(request.Body,Encoding.UTF8,"application/json");
            return message;
        }
        catch{message.Dispose();throw;}
    }
    static async Task<ProviderDocument> Send(ProviderRequest request,CancellationToken ct)
    {
        using var message=BuildRequest(request);
        using var response=await http.SendAsync(message,HttpCompletionOption.ResponseHeadersRead,ct).ConfigureAwait(false);
        if((int)response.StatusCode is >=300 and <400)return new([],request.Url,Status:(int)response.StatusCode,Redirect:response.Headers.Location?.ToString()??"");
        if(request.PortalChannelCatalog&&response.IsSuccessStatusCode)
        {
            if(response.Content.Headers.ContentLength>PortalChannelReader.RawByteLimit)throw new InvalidDataException("Katalog portalu przekracza limit 64 MB. Zachowano poprzednią listę.");
            await using var channelStream=await response.Content.ReadAsStreamAsync(ct);
            var batch=await PortalChannelReader.ReadAsync(channelStream,ct);
            return new([],request.Url,response.Content.Headers.ContentType?.MediaType??"",response.Content.Headers.LastModified,(int)response.StatusCode){PortalChannels=batch};
        }
        if(response.Content.Headers.ContentLength>24_000_000)throw new InvalidDataException("Odpowiedź źródła przekracza 24 MB.");
        await using var input=await response.Content.ReadAsStreamAsync(ct);using var output=new MemoryStream();var buffer=new byte[16384];int n;
        while((n=await input.ReadAsync(buffer,ct))>0){if(output.Length+n>24_000_000)throw new InvalidDataException("Odpowiedź źródła przekracza 24 MB.");output.Write(buffer,0,n);}
        return new(output.ToArray(),request.Url,response.Content.Headers.ContentType?.MediaType??"",response.Content.Headers.LastModified,(int)response.StatusCode);
    }
    async Task<ProviderDocument> Read(ProviderRequest request,CancellationToken ct,bool portalProbe=false)
    {
        ct.ThrowIfCancellationRequested();
        Url(request.Url);using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(30));
        string original=request.Url;
        try
        {
            for(int hop=0;hop<5;hop++)
            {
                var doc=await transport(request,deadline.Token).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                if(doc.Status is >=300 and <400)
                {
                    if(!Uri.TryCreate(Url(request.Url),doc.Redirect,out var next))throw new InvalidDataException("Nieprawidłowe przekierowanie źródła.");
                    Url(next.AbsoluteUri);
                    bool secret=Url(original).Query.Length>0||request.Method!="GET"||request.Headers?.Count>0;
                    if(!SameOrigin(original,next.AbsoluteUri)&&(secret||next.IsLoopback||IPAddress.TryParse(next.Host,out var ip)&&!PublicWeb.IsPublic(ip)))throw new InvalidDataException("Zablokowano przekierowanie danych dostępu na inny serwer.");
                    if(Url(request.Url).Scheme=="https"&&next.Scheme!="https")throw new InvalidDataException("Zablokowano przekierowanie z HTTPS na HTTP.");
                    request=request with{Url=next.AbsoluteUri};continue;
                }
                if(doc.BodyBytes>(request.PortalChannelCatalog?PortalChannelReader.RawByteLimit:24_000_000))throw new InvalidDataException("Odpowiedź źródła przekracza obsługiwany limit rozmiaru.");
                if(doc.Modified>DateTimeOffset.UtcNow.AddMinutes(10))throw new InvalidDataException("Źródło podaje datę z przyszłości.");
                if(!(portalProbe&&doc.Status is 401 or 403 or 404))
                {
                    if(doc.Status is 401 or 403)throw new UnauthorizedAccessException("Źródło odrzuciło dane dostępu. Sprawdź własne konto lub przypisanie urządzenia.");
                    if(doc.Status is <200 or >=300)throw new IOException("Źródło zwróciło HTTP "+doc.Status+". Poprzednia lista została zachowana.");
                }
                return doc with {Url=request.Url};
            }
            throw new InvalidDataException("Zbyt wiele przekierowań źródła.");
        }
        catch(HttpRequestException){throw new IOException("Nie udało się połączyć ze źródłem. Sprawdź adres, sieć i certyfikat serwera.");}
        catch(OperationCanceledException) when(!ct.IsCancellationRequested){throw new IOException("Przekroczono czas odpowiedzi źródła. Poprzednia lista została zachowana.");}
    }
    static string Value(JsonElement x,string name)=>x.ValueKind==JsonValueKind.Object&&x.TryGetProperty(name,out var v)&&v.ValueKind is JsonValueKind.String or JsonValueKind.Number?v.ToString():"";
    static JsonDocument Json(ProviderDocument d){try{return JsonDocument.Parse(d.Bytes);}catch(JsonException){throw new InvalidDataException("Źródło zwróciło nieprawidłowy JSON.");}}
    static string Hash(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    public static void Validate(ProviderSource source)
    {
        try
        {
        if(source.DiscoveredPublic&&(source.Kind!=PrivateSourceKind.M3u||PublicPlaylistPrivacy.ContainsCredentials(source.Url)))throw new InvalidDataException("Publiczna subskrypcja wymaga adresu playlisty bez danych konta.");
        if(!Regex.IsMatch(source.Id,"^[a-f0-9]{32}$")||string.IsNullOrWhiteSpace(source.Name)||source.Name.Length>80||!Enum.IsDefined(source.Kind))throw new InvalidDataException("Nieprawidłowa konfiguracja źródła.");
        if(source.Kind==PrivateSourceKind.M3uFile){if(!Path.IsPathFullyQualified(source.Url))throw new InvalidDataException("Wybierz pełną ścieżkę pliku M3U.");}
        else if(source.Kind!=PrivateSourceKind.SavedChannels)Url(source.Url);
        if(source.Kind==PrivateSourceKind.Xtream&&(string.IsNullOrWhiteSpace(source.Username)||string.IsNullOrWhiteSpace(source.Password)))throw new InvalidDataException("Wpisz login i hasło własnego konta operatora.");
        if(source.Kind==PrivateSourceKind.Portal&&!Regex.IsMatch(source.Mac,"^(?:[0-9A-Fa-f]{2}:){5}[0-9A-Fa-f]{2}$"))throw new InvalidDataException("Wpisz MAC własnego, przypisanego urządzenia (sześć par cyfr szesnastkowych).");
        if(source.Kind==PrivateSourceKind.ClearKeyDash)
        {
            if(source.KeyHex.Length==0)Url(source.LicenseUrl);else if(!Regex.IsMatch(source.KeyHex,"^[a-fA-F0-9]{32}$"))throw new InvalidDataException("Klucz CENC operatora musi mieć 32 znaki hex.");
            if(!Regex.IsMatch(source.KeyId.Replace("-",""),"^[a-fA-F0-9]{32}$"))throw new InvalidDataException("ClearKey wymaga identyfikatora KID operatora (32 znaki hex).");
        }
        if(source.Username.Length>1024||source.Password.Length>4096||source.Url.Length>8192||source.LicenseUrl.Length>8192)throw new InvalidDataException("Dane źródła są zbyt długie.");
        }
        catch(Exception ex)when(ex is not OperationCanceledException){ProviderFailures.Tag(ex,"configuration");throw;}
    }
    public async Task<ProviderCatalog> Refresh(ProviderSource source,CancellationToken ct)
    {
        source=XtreamLink.Normalize(source);
        Validate(source);
        if(source.Kind==PrivateSourceKind.SavedChannels)throw new InvalidDataException("To chroniony zapis wcześniejszych kanałów, bez adresu odświeżania. Dodaj oryginalną playlistę w Moich źródłach.");
        if(source.Kind==PrivateSourceKind.DirectUrl)return new([new("direct",new(source.Name,source.Url,"Live",Source:"Moje źródło"))],"",DateTimeOffset.UtcNow,null,"Zapisano własny adres. Odtwarzanie nie jest jeszcze potwierdzone.");
        if(source.Kind==PrivateSourceKind.Xtream)return await Xtream(source,ct);
        if(source.Kind==PrivateSourceKind.Portal)return await Portal(source,ct);
        ProviderDocument doc;
        if(source.Kind==PrivateSourceKind.M3uFile)
        {
            if(!File.Exists(source.Url)||new FileInfo(source.Url).Length>20_000_000)throw new InvalidDataException("Plik M3U jest niedostępny albo przekracza 20 MB.");
            doc=new(await File.ReadAllBytesAsync(source.Url,ct),source.Url,Modified:new DateTimeOffset(File.GetLastWriteTimeUtc(source.Url)));
        }
        else doc=await Read(new(source.Url),ct);
        if(source.Kind==PrivateSourceKind.ClearKeyDash)
        {
            using var manifestBytes=new MemoryStream(doc.Bytes);
            using var reader=XmlReader.Create(manifestBytes,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null});
            XDocument xml;try{xml=XDocument.Load(reader);}catch(XmlException){throw new InvalidDataException("Źródło ClearKey nie zwróciło manifestu DASH.");}
            if(xml.Root?.Name.LocalName!="MPD")throw new InvalidDataException("Źródło ClearKey nie zwróciło manifestu DASH.");
            string kid=source.KeyId.Replace("-","");
            var ids=xml.Descendants().Attributes().Where(a=>a.Name.LocalName=="default_KID").Select(a=>a.Value.Replace("-","")).ToArray();
            if(ids.Length==0||ids.Any(id=>!id.Equals(kid,StringComparison.OrdinalIgnoreCase)))throw new InvalidDataException("Ten manifest wymaga innego KID lub wielu kluczy; obsługiwany jest pojedynczy klucz CENC.");
            return new([new("dash",new(source.Name,source.Url,"TV",Source:"Moje źródło"))],"",DateTimeOffset.UtcNow,doc.Modified,"DASH ClearKey: wymagane własne uprawnienie i działający serwer licencji. Widevine/PlayReady nie są obsługiwane.");
        }
        string body=doc.Text.TrimStart('\uFEFF',' ','\r','\n');
        string publicNotice="";
        if(source.DiscoveredPublic)
        {
            var cleaned=PublicPlaylistPrivacy.SanitizeM3u(doc.Url,body);body=cleaned.Body;
            if(M3u.IsHls(body))throw new InvalidDataException("Wynik AI jest manifestem jednej transmisji, nie listą kanałów.");
            publicNotice=cleaned.Sanitized?" • Oczyszczono publiczną kopię: pominięte wpisy "+cleaned.RemovedEntries+" • SHA256 odpowiedzi: "+cleaned.OriginalSha256:" • Publiczna subskrypcja: sprawdzono brak danych konta w każdym pobraniu.";
        }
        if(source.Kind==PrivateSourceKind.M3uFile && Bouquet.IsBouquet(body)) body=Bouquet.ToM3u(body);
        if(!body.StartsWith("#EXTM3U",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Adres nie zwrócił playlisty M3U. Sprawdź URL otrzymany od operatora.");
        if(Regex.IsMatch(body,@"(?im)^\s*#(?:EXTHTTP:|EXTVLCOPT:http-(?:cookie|header|authorization)|KODIPROP:.*(?:license|headers))")||Regex.IsMatch(body,@"(?im)^https?://[^\r\n]*\|[^\r\n]*(?:authorization|cookie)="))throw new InvalidDataException("Playlista wymaga dodatkowych nagłówków Cookie/Authorization lub konfiguracji DRM, której ten odtwarzacz nie obsługuje. Nie przekazano ich do odtwarzacza.");
        if(M3u.IsHls(body))return new([new("live",new(source.Name,doc.Url,"Live",Source:"Moje źródło"))],"",DateTimeOffset.UtcNow,doc.Modified,"Dodano pojedynczą transmisję HLS.");
        var stations=M3u.Parse(body,doc.Url,source.Kind==PrivateSourceKind.M3uFile).Where(s=>SharedPlaylists.AllowedStream(s.Url)||source.Kind==PrivateSourceKind.M3uFile&&File.Exists(s.Url)).ToList();
        if(stations.Count==0)throw new InvalidDataException("Źródło zwróciło pustą playlistę. Poprzednia lista została zachowana.");
        string header=body.Split('\n',2)[0];var epg=Regex.Match(header,"(?:x-tvg-url|url-tvg|tvg-url)\\s*=\\s*[\"']([^\"']+)[\"']",RegexOptions.IgnoreCase).Groups[1].Value.Split(',')[0].Trim();
        if(epg.Length>0&&!TextTools.IsWeb(epg))epg="";
        var channels=stations.Select((s,i)=>new ProviderChannel(Hash(s.ChannelId+"|"+s.Feed+"|"+s.Name+"|"+(TextTools.IsWeb(s.Url)?new Uri(s.Url).GetLeftPart(UriPartial.Path):s.Url)),s with{Name=TextTools.IsWeb(s.Name)?"Transmisja "+(i+1):s.Name,Kind="Live",Source="Moje źródło"})).DistinctBy(s=>s.Id).ToList();
        return new(channels,epg,DateTimeOffset.UtcNow,doc.Modified,"Pobrano M3U na podstawie treści odpowiedzi; rozszerzenie URL nie jest wymagane."+publicNotice);
    }
    static string XtreamBase(ProviderSource s)
    {
        var u=Url(s.Url);if(u.Query.Length>0)throw new InvalidDataException("W polu serwera Xtream podaj adres bez loginu i hasła w URL; użyj osobnych pól.");
        string path=u.AbsolutePath.TrimEnd('/');if(Regex.IsMatch(path,@"/(?:player_api|get|xmltv)\.php$",RegexOptions.IgnoreCase))path=path[..path.LastIndexOf('/')];
        return new UriBuilder(u){Path=path+"/",Query="",Fragment=""}.Uri.AbsoluteUri;
    }
    static string XUrl(ProviderSource s,string file,string action="")=>XtreamBase(s)+file+"?username="+Uri.EscapeDataString(s.Username)+"&password="+Uri.EscapeDataString(s.Password)+(action.Length>0?"&action="+action:"");
    async Task<ProviderCatalog> Xtream(ProviderSource s,CancellationToken ct)
    {
        using var auth=Json(await Read(new(XUrl(s,"player_api.php")),ct));
        if(!auth.RootElement.TryGetProperty("user_info",out var user)||Value(user,"auth")!="1"||Value(user,"status") is var status&&status.Length>0&&!status.Equals("Active",StringComparison.OrdinalIgnoreCase))throw new UnauthorizedAccessException("Konto Xtream jest nieaktywne albo dane dostępu są nieprawidłowe.");
        if(long.TryParse(Value(user,"exp_date"),out var expiry)&&expiry>0&&expiry<DateTimeOffset.UtcNow.ToUnixTimeSeconds())throw new UnauthorizedAccessException("Wygasła ważność konta Xtream.");
        string extension="ts";if(user.TryGetProperty("allowed_output_formats",out var formats)&&formats.ValueKind==JsonValueKind.Array&&formats.EnumerateArray().Any(x=>x.ToString()=="m3u8"))extension="m3u8";
        var categories=new Dictionary<string,string>();string notice="";
        try
        {
            using var cats=Json(await Read(new(XUrl(s,"player_api.php","get_live_categories")),ct));
            if(cats.RootElement.ValueKind==JsonValueKind.Array)foreach(var c in cats.RootElement.EnumerateArray()){string id=Value(c,"category_id"),name=Value(c,"category_name");if(id.Length>0)categories[id]=name;}
        }
        catch(Exception ex)when(ex is IOException or InvalidDataException){notice="Kategorie chwilowo niedostępne; kanały pobrano osobno.";}
        var response=await Read(new(XUrl(s,"player_api.php","get_live_streams")),ct);using var streams=Json(response);
        if(streams.RootElement.ValueKind!=JsonValueKind.Array)throw new InvalidDataException("Niezgodna odpowiedź listy kanałów Xtream.");
        var channels=new List<ProviderChannel>();
        foreach(var item in streams.RootElement.EnumerateArray())
        {
            string id=Value(item,"stream_id"),name=Value(item,"name");if(!Regex.IsMatch(id,"^[0-9]+$")||name.Length==0)continue;
            string link=XtreamBase(s)+"live/"+Uri.EscapeDataString(s.Username)+"/"+Uri.EscapeDataString(s.Password)+"/"+id+"."+extension;
            channels.Add(new(id,new(name,link,"TV",categories.GetValueOrDefault(Value(item,"category_id"),""),"Moje źródło",ChannelId:Value(item,"epg_channel_id"))));
            if(channels.Count>=30000)break;
        }
        if(channels.Count==0)throw new InvalidDataException("Operator nie zwrócił kanałów live. Poprzednia lista została zachowana.");
        return new(channels.DistinctBy(c=>c.Id).ToList(),XUrl(s,"xmltv.php"),DateTimeOffset.UtcNow,response.Modified,notice);
    }
    static ProviderRequest PortalMessage(ProviderSource s,PortalConnection session,string type,string action,Dictionary<string,string>? extra=null)
    {
        var values=new Dictionary<string,string>{{"type",type},{"action",action},{"JsHttpRequest","1-xml"}};foreach(var p in extra??[])values[p.Key]=p.Value;
        string url=session.Endpoint+"?"+string.Join('&',values.Select(p=>Uri.EscapeDataString(p.Key)+"="+Uri.EscapeDataString(p.Value)));
        var headers=new Dictionary<string,string>{{"Cookie","mac="+s.Mac.ToUpperInvariant()+"; stb_lang=en; timezone=UTC"},{"User-Agent",PortalProtocol.UserAgent}};
        if(session.Mode==PortalProtocolMode.FullProfile&&session.Token.Length>0)headers["Authorization"]="Bearer "+session.Token;
        return new(url,Headers:headers);
    }
    async Task<JsonDocument> PortalRequest(ProviderSource s,PortalConnection session,string type,string action,CancellationToken ct,Dictionary<string,string>? extra=null)=>Json(await Read(PortalMessage(s,session,type,action,extra),ct));
    async Task<PortalConnection> PortalSession(ProviderSource s,CancellationToken ct)
    {
        try
        {
            foreach(string endpoint in PortalAddress.Candidates(s.Url))
            {
                var direct=new PortalConnection(endpoint,PortalProtocolMode.DirectMac);
                var probe=await Read(PortalMessage(s,direct,"itv","get_genres"),ct,portalProbe:true);
                switch(PortalProtocol.Classify(probe))
                {
                    case PortalProbeKind.DirectMac:return direct;
                    case PortalProbeKind.AuthenticationRequired:return await FullPortalSession(s,endpoint,ct);
                    case PortalProbeKind.Denied:throw new UnauthorizedAccessException("Portal odrzucił dostęp do listy kategorii. Sprawdź uprawnienia u operatora.");
                }
            }
            throw new InvalidDataException("Nie rozpoznano obsługiwanego API portalu pod podanym adresem. Poprzednia lista została zachowana.");
        }
        catch(Exception ex)when(ex is not OperationCanceledException){ProviderFailures.Tag(ex,"portal_handshake");throw;}
    }
    async Task<PortalConnection> FullPortalSession(ProviderSource s,string endpoint,CancellationToken ct)
    {
        var session=new PortalConnection(endpoint,PortalProtocolMode.FullProfile);
        string token;
        try
        {
            using var hello=await PortalRequest(s,session,"stb","handshake",ct);
            if(hello.RootElement.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("Portal zwrócił nieprawidłową odpowiedź połączenia.");
            token=hello.RootElement.TryGetProperty("js",out var js)?Value(js,"token"):"";
            if(token.Length==0||token.Length>8192||token.Contains('\r')||token.Contains('\n'))throw new UnauthorizedAccessException("Portal nie potwierdził urządzenia. Sprawdź własny MAC i aktywację u operatora.");
            session=session with{Token=token};
        }
        catch(Exception ex)when(ex is not OperationCanceledException){ProviderFailures.Tag(ex,"portal_handshake");throw;}
        try
        {
            using var profile=await PortalRequest(s,session,"stb","get_profile",ct,new(){{"hd","1"},{"client_type","STB"}});
            var root=profile.RootElement;
            ValidatePortalProfile(root.ValueKind==JsonValueKind.Object&&root.TryGetProperty("js",out var p)?p:default);
        }
        catch(Exception ex)when(ex is not OperationCanceledException){ProviderFailures.Tag(ex,"portal_profile");throw;}
        return session;
    }
    internal static void ValidatePortalProfile(JsonElement profile)
    {
        const string failure="Portal nie potwierdził aktywnego profilu urządzenia. Sprawdź przypisanie i uprawnienia u operatora.";
        if(profile.ValueKind!=JsonValueKind.Object||!profile.TryGetProperty("id",out var id)||!PositivePortalId(id)||!InactivePortalFlag(profile,"status")||!InactivePortalFlag(profile,"blocked"))throw new UnauthorizedAccessException(failure);
    }
    static bool PositivePortalId(JsonElement value)
    {
        string raw=value.ValueKind switch{JsonValueKind.Number=>value.GetRawText(),JsonValueKind.String=>value.GetString()!.Trim(' ','\t','\r','\n'),_=>""};
        return Regex.IsMatch(raw,"^[0-9]+$")&&long.TryParse(raw,System.Globalization.NumberStyles.None,System.Globalization.CultureInfo.InvariantCulture,out long id)&&id>0;
    }
    static bool InactivePortalFlag(JsonElement profile,string name)
    {
        if(!profile.TryGetProperty(name,out var value)||value.ValueKind==JsonValueKind.Null)return true;
        if(value.ValueKind==JsonValueKind.False)return true;
        if(value.ValueKind==JsonValueKind.String){string text=value.GetString()!.Trim(' ','\t','\r','\n');return text.Length==0||text=="0"||text.Equals("false",StringComparison.OrdinalIgnoreCase);}
        // Reject fractions/exponents before parsing: coercion could silently turn
        // malformed values or a tiny nonzero number into an inactive flag.
        return value.ValueKind==JsonValueKind.Number&&Regex.IsMatch(value.GetRawText(),"^-?[0-9]+$")&&value.TryGetInt64(out long flag)&&flag==0;
    }
    async Task<ProviderCatalog> Portal(ProviderSource s,CancellationToken ct)
    {
        var session=await PortalSession(s,ct);
        try
        {
        var request=PortalMessage(s,session,"itv","get_all_channels",new(){{"force_ch_link_check",""}}) with{PortalChannelCatalog=true};
        var document=await Read(request,ct);
        var batch=document.PortalChannels??await PortalChannelReader.ReadAsync(new MemoryStream(document.Bytes,writable:false),ct);
        string mode=session.Mode==PortalProtocolMode.DirectMac?"Portal MAC: pobrano katalog bez sesji tokenowej.":"Portal MAC: potwierdzono profil i pobrano katalog.";
        string limited=batch.Truncated?$" Lista ograniczona do pierwszych {batch.Channels.Count} unikalnych kanałów (limit liczby lub rozmiaru). Pozostałe pozycje nie zostały zapisane.":"";
        return new(batch.Channels,"",DateTimeOffset.UtcNow,null,mode+limited+" Lista kanałów nie potwierdza odtwarzania. Przy wyborze kanału ponownie sprawdzana jest sesja portalu; sposób uzyskania adresu wynika z danych kanału. Portale wymagające SN, certyfikatu lub parowania oraz EPG portalu nie są obsługiwane.");
        }
        catch(Exception ex)when(ex is not OperationCanceledException){ProviderFailures.Tag(ex,"portal_channels");throw;}
    }
    static string Base64Url(byte[] data)=>Convert.ToBase64String(data).TrimEnd('=').Replace('+','-').Replace('/','_');
    static byte[] Decode64(string value){string s=value.Replace('-','+').Replace('_','/');return Convert.FromBase64String(s.PadRight((s.Length+3)/4*4,'='));}
    async Task<string?> StaticPortalCommand(ProviderChannel channel,CancellationToken ct)
    {
        if(channel.UseHttpTmpLink!=false||channel.UseLoadBalancing!=false)return null;
        string command=channel.Command.Trim(' ','\t');
        foreach(string prefix in new[]{"ffmpeg ","ffrt "})
            if(command.StartsWith(prefix,StringComparison.OrdinalIgnoreCase)){command=command[prefix.Length..].TrimStart(' ','\t');break;}
        // A catalogue command is data, never a shell command. No loose URL
        // extraction: options, quotes, relative URLs and local placeholders
        // must still be resolved by the portal's create_link API.
        if(command.Length==0||command.Any(c=>char.IsWhiteSpace(c)||char.IsControl(c)||c is '\\' or '\"' or '\''))return null;
        Uri uri;try{uri=Url(command);}catch(InvalidDataException){return null;}
        if(IPAddress.TryParse(uri.Host,out _))return command;
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            var addresses=await mediaDns(uri.DnsSafeHost,deadline.Token).WaitAsync(deadline.Token).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            return addresses.Length>0&&addresses.All(PublicWeb.IsPublic)?command:null;
        }
        catch(SocketException){return null;}
        catch(OperationCanceledException)when(!ct.IsCancellationRequested){return null;}
    }
    public async Task<ProviderPlayback> Resolve(ProviderSource source,ProviderChannel channel,CancellationToken ct)
    {
        source=XtreamLink.Normalize(source);
        Validate(source);
        // Native mpv applies supplied headers to its media requests, including child segments.
        // Never send authentication material as a global media header.
        if(channel.Station.UserAgent.Contains('\r')||channel.Station.UserAgent.Contains('\n'))throw new InvalidDataException("Nieobsługiwane nagłówki źródła.");
        if(channel.Station.Referrer.Length>0)
        {
            var referer=Url(channel.Station.Referrer);
            if(referer.Query.Length>0||Regex.IsMatch(referer.AbsolutePath,@"/(?:live|movie|series)/[^/]+/[^/]+/",RegexOptions.IgnoreCase))throw new InvalidDataException("Odtwarzanie wymaga prywatnego Referer. Ten wariant nie przekazuje danych dostępu jako globalnych nagłówków odtwarzacza.");
        }
        if(source.Kind==PrivateSourceKind.Portal)
        {
            var session=await PortalSession(source,ct);
            try
            {
            string? original=await StaticPortalCommand(channel,ct);
            if(original!=null)return new(channel.Station with{Url=original,UserAgent=PortalProtocol.UserAgent});
            using var result=await PortalRequest(source,session,"itv","create_link",ct,new(){{"cmd",channel.Command},{"series",""},{"forced_storage","undefined"},{"disable_ad","0"},{"download","0"}});
            if(result.RootElement.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("Portal nie udostępnił adresu odtwarzania tego kanału.");
            string command=result.RootElement.TryGetProperty("js",out var js)?Value(js,"cmd"):"";
            var match=Regex.Match(command,@"https?://[^\s\""']+",RegexOptions.IgnoreCase);
            if(!match.Success||!TextTools.IsWeb(match.Value))throw new InvalidDataException("Portal nie udostępnił adresu odtwarzania tego kanału.");
            Url(match.Value);
            return new(channel.Station with{Url=match.Value,UserAgent=PortalProtocol.UserAgent});
            }
            catch(Exception ex)when(ex is not OperationCanceledException){ProviderFailures.Tag(ex,"portal_link");throw;}
        }
        if(source.Kind==PrivateSourceKind.ClearKeyDash)
        {
            if(source.KeyHex.Length>0)return new(channel.Station,source.KeyHex.ToLowerInvariant());
            string kid=Base64Url(Convert.FromHexString(source.KeyId.Replace("-","")));
            using var license=Json(await Read(new(source.LicenseUrl,"POST",JsonSerializer.Serialize(new{kids=new[]{kid},type="temporary"})),ct));
            if(license.RootElement.TryGetProperty("keys",out var keys)&&keys.ValueKind==JsonValueKind.Array)
                foreach(var k in keys.EnumerateArray())if(Value(k,"kid")==kid&&Value(k,"kty")=="oct")
                {try{var key=Decode64(Value(k,"k"));if(key.Length==16)return new(channel.Station,Convert.ToHexString(key).ToLowerInvariant());}catch(FormatException){break;}}
            throw new InvalidDataException("Serwer licencji nie zwrócił obsługiwanego klucza dla tego KID.");
        }
        return new(channel.Station);
    }
    public async Task<byte[]> DownloadEpg(ProviderSource source,string url,CancellationToken ct)
    {Validate(source);Url(url);return (await Read(new(url),ct)).Bytes;}
}
