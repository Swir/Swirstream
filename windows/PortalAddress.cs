using System.Text.RegularExpressions;
namespace SwirStreamAI;

// A local parser for addresses explicitly supplied by the user. It performs no
// discovery, authentication, MAC inference, network requests or credential storage.
public static class PortalAddress
{
    static readonly Regex KnownClient=new(@"/stalker_portal(?:/c(?:/index\.html?)?)?/?$",RegexOptions.IgnoreCase|RegexOptions.Compiled);
    static readonly Regex ClientSuffix=new(@"/c(?:/index\.html?)?/?$",RegexOptions.IgnoreCase|RegexOptions.Compiled);
    public static bool ExplicitClient(string value)
    {
        try{var u=PrivateProviderClient.Url(XtreamLink.Clean(value));return u.Query.Length==0&&KnownClient.IsMatch(u.AbsolutePath);}
        catch(InvalidDataException){return false;}
    }
    public static string Endpoint(string value)
    {
        var u=PrivateProviderClient.Url(XtreamLink.Clean(value));
        if(u.Query.Length>0)throw new InvalidDataException("Podaj adres portalu bez parametrów sesji, MAC ani klucza debugowania.");
        string path=u.AbsolutePath;
        // Existing explicitly configured direct portal.php/server/load.php remain compatible.
        if(path.EndsWith(".php",StringComparison.OrdinalIgnoreCase))return u.AbsoluteUri;
        path=ClientSuffix.Replace(path,"").TrimEnd('/');
        return u.GetLeftPart(UriPartial.Authority)+path+"/server/load.php";
    }
    // Only a small set of API paths under the explicitly supplied installation.
    // This is protocol negotiation, never a search for other hosts or accounts.
    internal static IReadOnlyList<string> Candidates(string value)
    {
        var u=PrivateProviderClient.Url(XtreamLink.Clean(value));
        if(u.Query.Length>0)throw new InvalidDataException("Podaj adres portalu bez parametrów sesji, MAC ani klucza debugowania.");
        string path=u.AbsolutePath.TrimEnd('/'),origin=u.GetLeftPart(UriPartial.Authority);
        bool explicitFile=path.EndsWith(".php",StringComparison.OrdinalIgnoreCase);
        var paths=new List<string>();
        if(explicitFile)
        {
            paths.Add(path);
            path=Regex.Replace(path,@"/(?:server/load|portal)\.php$","",RegexOptions.IgnoreCase);
            path=Regex.Replace(path,@"/[^/]*\.php$","",RegexOptions.IgnoreCase);
        }
        else path=ClientSuffix.Replace(path,"").TrimEnd('/');
        paths.Add(path+"/portal.php");paths.Add(path+"/server/load.php");
        if(!Regex.IsMatch(path,@"/stalker_portal(?:/|$)",RegexOptions.IgnoreCase))paths.Add(path+"/stalker_portal/server/load.php");
        return paths.Distinct(StringComparer.Ordinal).Take(3).Select(p=>PrivateProviderClient.Url(origin+p).AbsoluteUri).ToArray();
    }
}
