using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace SwirStreamAI;

// Imported account configuration lives only in memory until the user selects Add.
// Do not serialize these objects to diagnostics, reports or public discovery.
public sealed record PortalTxtEntry(int Number,int Line,string Url,string Mac)
{
    public bool HasMac=>Mac.Length>0;
    public string Label=>"Portal "+Number;
    public override string ToString()=>Label;
    public ProviderSource Source(string? id=null)=>new(id??Guid.NewGuid().ToString("N"),"Portal TXT "+Number,PrivateSourceKind.Portal,Url,Mac:Mac);
}
public record PortalTxtIssue(int Line,string Code);
public record PortalTxtImport(IReadOnlyList<PortalTxtEntry> Entries,int InvalidLines,int Duplicates,IReadOnlyList<PortalTxtIssue> Issues);

public static class PortalTxt
{
    public const int MaxBytes=1_048_576,MaxEntries=200,MaxIssueDetails=50;
    static readonly Regex MacLine=new(@"^(?<mac>(?:[0-9a-f]{2}:){5}[0-9a-f]{2})(?:\s+-\s+[^\r\n]*|\s*#[^\r\n]*)?$",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
    static string? Mac(string value)
    {
        var match=MacLine.Match(value);if(!match.Success)return null;
        string mac=match.Groups["mac"].Value.ToUpperInvariant();return mac is "00:00:00:00:00:00" or "FF:FF:FF:FF:FF:FF"?null:mac;
    }
    public static string Url(string value)
    {
        if(value.Length is 0 or >4096||value.Any(char.IsWhiteSpace)||value.Any(char.IsControl)||value.Contains('\\')||Regex.IsMatch(value,@"%(?![0-9A-Fa-f]{2})"))throw new InvalidDataException("Nieprawidłowy adres portalu.");
        string decoded=value;
        for(int i=0;i<3;i++)decoded=Uri.UnescapeDataString(decoded);
        if(decoded.Any(char.IsControl)||decoded.Contains('\\'))throw new InvalidDataException("Nieprawidłowy adres portalu.");
        var uri=PrivateProviderClient.Url(value);
        if(uri.Query.Length>0||uri.UserInfo.Length>0||uri.Fragment.Length>0)throw new InvalidDataException("Adres portalu nie może zawierać parametrów ani danych konta.");
        return uri.AbsoluteUri;
    }
    public static string Identity(string url,string mac)=>Url(url).TrimEnd('/')+"|"+mac.ToUpperInvariant();
    public static async Task<PortalTxtImport> Read(Stream input,CancellationToken ct)
    {
        using var data=new MemoryStream();var buffer=new byte[8192];int n;
        while((n=await input.ReadAsync(buffer,ct))>0)
        {if(data.Length+n>MaxBytes)throw new InvalidDataException("Plik TXT przekracza limit 1 MiB. Nie zaimportowano listy.");data.Write(buffer,0,n);}
        ct.ThrowIfCancellationRequested();return Parse(data.ToArray());
    }
    public static PortalTxtImport Parse(byte[] bytes)
    {
        if(bytes.Length>MaxBytes)throw new InvalidDataException("Plik TXT przekracza limit 1 MiB. Nie zaimportowano listy.");
        string text;
        try
        {
            if(bytes.Length>=4&&(bytes.AsSpan(0,4).SequenceEqual(new byte[]{255,254,0,0})||bytes.AsSpan(0,4).SequenceEqual(new byte[]{0,0,254,255})))throw new DecoderFallbackException();
            if(bytes.Length>=2&&bytes[0]==255&&bytes[1]==254)text=new UnicodeEncoding(false,false,true).GetString(bytes,2,bytes.Length-2);
            else if(bytes.Length>=2&&bytes[0]==254&&bytes[1]==255)text=new UnicodeEncoding(true,false,true).GetString(bytes,2,bytes.Length-2);
            else {int skip=bytes.AsSpan().StartsWith(new byte[]{239,187,191})?3:0;text=new UTF8Encoding(false,true).GetString(bytes,skip,bytes.Length-skip);}
        }
        catch(DecoderFallbackException){throw new InvalidDataException("Nieprawidłowe kodowanie TXT. Użyj UTF-8 lub UTF-16 z BOM.");}
        if(text.Contains('\0'))throw new InvalidDataException("Nieprawidłowy tekst TXT.");
        var entries=new List<PortalTxtEntry>();var issues=new List<PortalTxtIssue>();var keys=new HashSet<string>(StringComparer.Ordinal);int bad=0,duplicate=0,lineNo=0;
        string? pending=null;int pendingLine=0;bool hadMac=false;
        void Invalid(int line){bad++;if(issues.Count<MaxIssueDetails)issues.Add(new(line,"invalid_line"));}
        void Emit(string url,string mac,int line)
        {
            if(!keys.Add(Identity(url,mac))){duplicate++;return;}
            if(entries.Count>=MaxEntries)throw new InvalidDataException("Lista zawiera ponad 200 unikalnych wpisów. Podziel TXT na mniejsze pliki; nie zaimportowano częściowej listy.");
            entries.Add(new(entries.Count+1,line,url,mac));
        }
        void Flush(){if(pending!=null&&!hadMac)Emit(pending,"",pendingLine);pending=null;hadMac=false;}
        using var lines=new StringReader(text);string? raw;
        while((raw=lines.ReadLine())!=null)
        {
            lineNo++;string line=raw.Trim();if(line.Length==0||line.StartsWith('#')||line.StartsWith(';'))continue;
            if(line.StartsWith("http://",StringComparison.OrdinalIgnoreCase)||line.StartsWith("https://",StringComparison.OrdinalIgnoreCase))
            {
                Flush();var fields=line.Split('|');
                try
                {
                    if(fields.Length is <1 or >2)throw new InvalidDataException();
                    string url=Url(fields[0].Trim());string mac="";
                    if(fields.Length==2){mac=Mac(fields[1].Trim())??throw new InvalidDataException();}
                    pending=url;pendingLine=lineNo;hadMac=mac.Length>0;if(hadMac)Emit(url,mac,lineNo);
                }
                catch(InvalidDataException error)when(!error.Message.StartsWith("Lista zawiera ponad 200",StringComparison.Ordinal)){Invalid(lineNo);pending=null;hadMac=false;}
                continue;
            }
            var parsedMac=Mac(line);
            if(parsedMac!=null&&pending!=null){Emit(pending,parsedMac,lineNo);hadMac=true;continue;}
            Flush();Invalid(lineNo);
        }
        Flush();return new(entries,bad,duplicate,issues);
    }
}
