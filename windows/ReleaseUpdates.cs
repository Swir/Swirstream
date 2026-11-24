using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SwirStreamAI;

internal record ReleaseAsset(Version Version,string Url,string Digest,long Size);
internal static class ReleaseUpdates
{
    internal const string Api="https://api.github.com/repos/Swir/Swirstream/releases/latest";
    const int SizeLimit=250*1024*1024;
    static readonly HttpClient Client=new(){Timeout=TimeSpan.FromMinutes(8)};
    static ReleaseUpdates(){Client.DefaultRequestHeaders.UserAgent.ParseAdd("SwirStream-Updater/1");}
    internal static ReleaseAsset? Select(JsonElement release,Version installed){
        if(release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean())return null;
        string tag=release.GetProperty("tag_name").GetString()??"";
        if(!Regex.IsMatch(tag,@"^v\d{1,5}\.\d{1,5}\.\d{1,5}$"))return null;
        string name="SwirStreamAI_"+tag[1..]+"_Setup_x64.exe";
        Version version=Version.Parse(tag[1..]);if(version<=installed)return null;
        string address="https://github.com/Swir/Swirstream/releases/download/"+tag+"/"+name;
        foreach(var asset in release.GetProperty("assets").EnumerateArray()){
            if(asset.GetProperty("name").GetString()!=name || asset.GetProperty("browser_download_url").GetString()!=address)continue;
            string digest=asset.GetProperty("digest").GetString()??"";
            long size=asset.GetProperty("size").GetInt64();
            if(!Regex.IsMatch(digest,"^sha256:[0-9a-f]{64}$")||size<=0||size>SizeLimit)throw new InvalidDataException("Invalid release integrity metadata.");
            return new(version,address,digest[7..],size);
        }
        return null;
    }
    internal static async Task<ReleaseAsset?> Latest(Version installed,CancellationToken ct){
        using var response=await Client.GetAsync(Api,HttpCompletionOption.ResponseHeadersRead,ct);
        if(response.StatusCode==HttpStatusCode.NotFound)return null;
        response.EnsureSuccessStatusCode();
        await response.Content.LoadIntoBufferAsync(2*1024*1024,ct);
        using var json=JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync(ct));
        return Select(json.RootElement,installed);
    }
    internal static async Task Download(ReleaseAsset asset,string destination,CancellationToken ct){
        using var response=await Client.GetAsync(asset.Url,HttpCompletionOption.ResponseHeadersRead,ct);
        response.EnsureSuccessStatusCode();
        await using var input=await response.Content.ReadAsStreamAsync(ct);
        await using var output=new FileStream(destination,FileMode.CreateNew,FileAccess.Write,FileShare.None);
        using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] chunk=new byte[81920];long total=0;int read;
        while((read=await input.ReadAsync(chunk,ct))>0){
            total+=read;if(total>asset.Size)throw new InvalidDataException("Unexpected installer size.");
            hash.AppendData(chunk,0,read);await output.WriteAsync(chunk.AsMemory(0,read),ct);
        }
        if(total!=asset.Size || !asset.Digest.Equals(Convert.ToHexString(hash.GetHashAndReset()),StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Installer checksum mismatch.");
    }
}
public sealed partial class MainForm
{
    bool checkingRelease;
    async Task CheckRelease(bool manual){
        bool en=UiLanguage.IsEnglish;
        if(checkingRelease || closing)return;
        checkingRelease=true;string? file=null;
        using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(8));
        FormClosedEventHandler cancel=(_,_)=>timeout.Cancel();FormClosed+=cancel;
        try{
            if(manual)status.Text=en?"Checking GitHub Releases…":"Sprawdzam wydania na GitHubie…";
            var version=typeof(MainForm).Assembly.GetName().Version!;
            var current=new Version(version.Major,version.Minor,version.Build);
            var asset=await ReleaseUpdates.Latest(current,timeout.Token);
            if(closing)return;
            if(asset==null){if(manual)status.Text=en?"No newer stable Windows installer.":"Masz aktualną wersję dla Windows.";return;}
            string prompt=en?"Download and install the update? The player will close.":"Pobrać i zainstalować aktualizację? Odtwarzacz zostanie zamknięty.";
            if(MessageBox.Show(this,prompt,"SwirStream AI "+asset.Version,MessageBoxButtons.YesNo,MessageBoxIcon.Information)!=DialogResult.Yes)return;
            status.Text=en?"Downloading the update…":"Pobieram aktualizację…";
            file=Path.Combine(Path.GetTempPath(),"SwirStreamAI-"+Guid.NewGuid().ToString("N")+".exe");
            await ReleaseUpdates.Download(asset,file,timeout.Token);
            if(closing)return;
            var setup=new ProcessStartInfo(file){UseShellExecute=false};
            setup.ArgumentList.Add("/SILENT");setup.ArgumentList.Add("/NORESTART");setup.ArgumentList.Add("/RELAUNCH=1");
            setup.ArgumentList.Add("/DIR="+AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
            Process.Start(setup);file=null;Close();
        }catch(Exception e){if(!closing && (manual||file!=null))status.Text=(en?"Update failed. ":"Aktualizacja nie powiodła się. ")+e.Message;}
        finally{FormClosed-=cancel;checkingRelease=false;if(file!=null)try{File.Delete(file);}catch(IOException){}}
    }
}
