using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SwirStreamAI;

// Current Windows user only. No credentials, source URLs or channel metadata are
// written outside the protected payload. DPAPI also protects temporary probes.
public static class PrivateData
{
    [StructLayout(LayoutKind.Sequential)] struct Blob { public int Size; public IntPtr Data; }
    [DllImport("crypt32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool CryptProtectData(ref Blob input,string description,ref Blob entropy,IntPtr reserved,IntPtr prompt,int flags,out Blob output);
    [DllImport("crypt32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool CryptUnprotectData(ref Blob input,IntPtr description,ref Blob entropy,IntPtr reserved,IntPtr prompt,int flags,out Blob output);
    [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr memory);
    static readonly byte[] Magic=Encoding.ASCII.GetBytes("SWIR-DPAPI-1\n");
    static Blob Alloc(byte[] bytes){var b=new Blob{Size=bytes.Length,Data=Marshal.AllocHGlobal(bytes.Length)};Marshal.Copy(bytes,0,b.Data,bytes.Length);return b;}
    static byte[] Transform(byte[] bytes,string purpose,bool protect)
    {
        var input=Alloc(bytes);var entropy=Alloc(Encoding.UTF8.GetBytes("SwirStreamAI|"+purpose));Blob output=default;
        try
        {
            bool ok=protect?CryptProtectData(ref input,"SwirStream AI",ref entropy,IntPtr.Zero,IntPtr.Zero,1,out output):CryptUnprotectData(ref input,IntPtr.Zero,ref entropy,IntPtr.Zero,IntPtr.Zero,1,out output);
            if(!ok)throw new CryptographicException("Dane źródła są niedostępne dla tego konta Windows albo zostały uszkodzone.");
            var result=new byte[output.Size];Marshal.Copy(output.Data,result,0,result.Length);return result;
        }
        finally
        {
            if(input.Data!=IntPtr.Zero){for(int i=0;i<input.Size;i++)Marshal.WriteByte(input.Data,i,0);Marshal.FreeHGlobal(input.Data);}
            if(entropy.Data!=IntPtr.Zero)Marshal.FreeHGlobal(entropy.Data);
            if(output.Data!=IntPtr.Zero)LocalFree(output.Data);
        }
    }
    public static byte[] Protect(byte[] bytes,string purpose)=>Magic.Concat(Transform(bytes,purpose,true)).ToArray();
    public static byte[] Unprotect(byte[] bytes,string purpose)
    {
        if(bytes.Length<=Magic.Length||!bytes.AsSpan(0,Magic.Length).SequenceEqual(Magic))throw new CryptographicException("Nieprawidłowy chroniony zapis.");
        return Transform(bytes[Magic.Length..],purpose,false);
    }
    public static T Read<T>(string path,string purpose,int maxBytes=100_000_000)
    {
        if(new FileInfo(path).Length>maxBytes)throw new InvalidDataException("Chroniony zapis jest zbyt duży.");
        byte[] plain=Unprotect(File.ReadAllBytes(path),purpose);
        try{return JsonSerializer.Deserialize<T>(plain)??throw new InvalidDataException("Pusty chroniony zapis.");}
        finally{CryptographicOperations.ZeroMemory(plain);}
    }
    public static void Write<T>(string path,T value,string purpose,bool backup=true)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        byte[] plain=JsonSerializer.SerializeToUtf8Bytes(value),cipher;
        try{cipher=Protect(plain,purpose);}finally{CryptographicOperations.ZeroMemory(plain);}
        string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{File.WriteAllBytes(temp,cipher);if(backup&&File.Exists(path))File.Copy(path,path+".bak",true);File.Move(temp,path,true);}
        finally{if(File.Exists(temp))File.Delete(temp);}
    }
}

public static class PrivateStation
{
    public static bool IsPrivate(Station s)=>!string.IsNullOrEmpty(s.PrivateSourceId)&&!string.IsNullOrEmpty(s.PrivateStreamId);
    public static string Address(string source,string channel)=>"swir-private://"+source+"/"+Uri.EscapeDataString(channel);
    public static Station Reference(string source,ProviderChannel channel)=>channel.Station with {Url=Address(source,channel.Id),Source="Moje źródło",Homepage="",UserAgent="",Referrer="",PrivateSourceId=source,PrivateStreamId=channel.Id};
    public static Station ForStorage(Station s)=>!IsPrivate(s)?s:new Station("Kanał z prywatnego źródła",Address(s.PrivateSourceId,s.PrivateStreamId),Kind:s.Kind,Source:"Moje źródło",PrivateSourceId:s.PrivateSourceId,PrivateStreamId:s.PrivateStreamId);
    public static string DisplayAddress(Station s)=>IsPrivate(s)?"Adres prywatnego źródła jest chroniony.":s.Url;
}

public enum PrivateSourceKind { M3u, Xtream, Portal, ClearKeyDash, DirectUrl, M3uFile, SavedChannels }
public record ProviderSource(string Id,string Name,PrivateSourceKind Kind,string Url,string Username="",string Password="",string Mac="",string LicenseUrl="",string KeyId="",string KeyHex="",[property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] bool DiscoveredPublic=false);
public record ProviderChannel(string Id,Station Station,string Command="")
{
    // Missing flags in older protected catalogues stay unknown: never guess that
    // an old command is a static stream instead of a create_link placeholder.
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public bool? UseHttpTmpLink {get;init;}
    [System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public bool? UseLoadBalancing {get;init;}
}
public record ProviderCatalog(List<ProviderChannel> Channels,string EpgUrl,DateTimeOffset DownloadedAt,DateTimeOffset? ModifiedAt,string Notice="");
public record ProviderPlayback(Station Station,string ClearKeyHex="");
public record ProviderState(ProviderSource Source,ProviderCatalog? Catalog=null,DateTimeOffset? LastAttempt=null,string Status="Dodano źródło. Odśwież, aby pobrać kanały.",EpgFeed? Guide=null,string CatalogConfigKey="",ProviderFailureInfo? CatalogFailure=null,ProviderFailureInfo? PlaybackFailure=null,DateTimeOffset? LastPlaybackAttempt=null);
public record ProviderVault(int Schema,List<ProviderState> Sources);
