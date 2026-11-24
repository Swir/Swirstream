namespace SwirStreamAI;

internal static class RegionalPlaylists
{
    internal const string Kodinerds="https://raw.githubusercontent.com/jnk22/kodinerds-iptv/master/iptv/clean/clean_tv.m3u";
    internal const string Tdt="https://www.tdtchannels.com/lists/tv.m3u8";
    static readonly HashSet<string> SpanishRegions=["Andalucía","Cataluña","C. Valenciana","Canarias","País Vasco","Galicia","Castilla-La Mancha","C. de Madrid","Castilla y León","R. de Murcia","Cantabria","C. Foral de Navarra","Extremadura","La Rioja","Illes Balears","Melilla","P. de Asturias","Aragón","Ceuta","Islas Baleares"];

    internal static IEnumerable<string> Seeds(string[] countries)
    {
        if(countries.Length==0 || countries.Contains("ES")) yield return Tdt;
        yield return Kodinerds;
        if(countries.Length>0 && !countries.Contains("ES")) yield return Tdt;
    }

    internal static string Country(string url,string group,string original)
    {
        if(url==Kodinerds && group.StartsWith("IPTV-") && group.Length==7){
            string code=WorldMarkets.Canonical(group[5..]);
            if(WorldMarkets.Name(code)!=code)return code;
        }
        if(url==Tdt){
            if(SpanishRegions.Contains(group)) return "ES";
            // The provider uses .TV in guide IDs for stations in many countries.
            if(original=="TV")return "";
        }
        return original;
    }
}
