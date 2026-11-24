using System.Text;
using System.Text.RegularExpressions;

namespace SwirStreamAI;

internal static class Bouquet
{
    internal static bool IsBouquet(string text)=>Regex.IsMatch(text.TrimStart('\uFEFF'),@"(?m)^#SERVICE ");
    internal static string ToM3u(string text)
    {
        var output=new StringBuilder("#EXTM3U\n");string url="",name="";int count=0;
        void Append(){
            if(url.Length==0)return;
            output.Append("#EXTINF:-1,").AppendLine(name.Length>0?name:new Uri(url).Host).AppendLine(url);
            count++;url="";name="";
        }
        foreach(string raw in text.Split('\n')){
            string line=raw.Trim().TrimStart('\uFEFF');
            if(line.StartsWith("#SERVICE ")){
                Append();
                string[] parts=line[9..].Split(':',11);
                if(parts.Length!=11)continue;
                var encoded=Regex.Match(parts[10],@"^(https?%3a(?://|%2f%2f)[^:\s]+)(?::(.*))?$",RegexOptions.IgnoreCase);
                string address=parts[10];
                if(encoded.Success){
                    string field=encoded.Groups[1].Value;
                    address=field.Contains("%3a%2f%2f",StringComparison.OrdinalIgnoreCase)?Uri.UnescapeDataString(field):Regex.Replace(field,"%3a",":",RegexOptions.IgnoreCase);
                }
                if(Uri.TryCreate(address,UriKind.Absolute,out var uri) && uri.Scheme is "http" or "https" && uri.UserInfo.Length==0 && !address.Any(char.IsControl)){
                    url=uri.AbsoluteUri;name=encoded.Success?encoded.Groups[2].Value.Trim():"";
                }
            }else if(line.StartsWith("#DESCRIPTION ") && url.Length!=0)name=line[13..].Trim();
        }
        Append();
        if(count==0)throw new InvalidDataException(UiLanguage.IsEnglish?"This bouquet has no IPTV stream URLs. DVB services require a tuner.":"Ten bukiet nie zawiera adresów IPTV. Usługi DVB wymagają tunera.");
        return output.ToString();
    }
}
