using System.Diagnostics;
using System.Text.RegularExpressions;

namespace SwirStreamAI;

public sealed partial class MainForm
{
    PrivateSourceStore privateSources=null!;
    CancellationTokenSource? privatePlayCancel;
    Action<Form>? privateDialogPreview;
    string localProviderMessage="";
    const bool ClearKeySupported=true; // Native AXINOM single-key CENC/H264 decoding verified; not a general CDM.
    void InitPrivateStore()
    {
        privateSources?.InvalidatePending();
        privateSources=new(profiles.Folder(profiles.Index.ActiveId));
        store.BeforeSave=ProtectLegacyPrivateEntries;
        ProtectLegacyPrivateEntries();store.HydratePrivate(privateSources.Display);store.Save();
    }
    static bool SensitiveAddress(Station s)
    {
        if(PrivateStation.IsPrivate(s)||!Uri.TryCreate(s.Url,UriKind.Absolute,out var u))return false;
        return u.UserInfo.Length>0||Regex.IsMatch(u.Query,@"(?:[?&])(?:username|password|passwd|token|auth|authorization|key|signature|sig)=",RegexOptions.IgnoreCase)||Regex.IsMatch(u.AbsolutePath,@"/(?:live|movie|series)/[^/]+/[^/]+/",RegexOptions.IgnoreCase);
    }
    void ProtectLegacyPrivateEntries()
    {
        var legacy=store.Data.Playlist.Concat(store.Data.Favorites).Concat(store.Data.History.Select(v=>v.Station)).Where(SensitiveAddress).DistinctBy(s=>s.Url).ToList();
        if(legacy.Count==0)return;
        var refs=privateSources.ImportSaved("Przeniesione prywatne kanały",legacy);
        Station Replace(Station s)=>refs.GetValueOrDefault(s.Url,s);
        store.Data.Playlist=store.Data.Playlist.Select(Replace).ToList();store.Data.Favorites=store.Data.Favorites.Select(Replace).ToList();store.Data.History=store.Data.History.Select(v=>v with{Station=Replace(v.Station)}).ToList();
    }
    async Task PlayPrivate(Station station,bool fallbackPlay)
    {
        privatePlayCancel?.Cancel();using var cancel=new CancellationTokenSource(TimeSpan.FromSeconds(70));privatePlayCancel=cancel;var repository=privateSources;
        status.Text="Łączenie z własnym źródłem…";
        try
        {
            var resolved=await repository.Resolve(station,cancel.Token);
            if(cancel.IsCancellationRequested||closing||!ReferenceEquals(repository,privateSources))return;
            Play(repository.Display(station),fallbackPlay,resolved);
            status.Text="Wybrano kanał z własnego źródła.";
        }
        catch(OperationCanceledException){if(!closing&&ReferenceEquals(privatePlayCancel,cancel)&&ReferenceEquals(repository,privateSources))status.Text="Anulowano połączenie z własnym źródłem.";}
        catch(Exception error)
        {
            if(!closing&&!cancel.IsCancellationRequested&&ReferenceEquals(privatePlayCancel,cancel)&&ReferenceEquals(repository,privateSources))
            {
                string reason=ProviderFailures.Describe(ProviderFailures.FromException(error));
                status.Text=reason;
                // Keep an already playing channel intact; make a failed request
                // visible even when no media was handed to the native player.
                if(!expectedPlayback){playbackFailure=reason;playback.Text=reason;}
                details.Text=reason+"\r\nLista kanałów pozostaje zapisana. Szczegóły tej próby znajdziesz w module źródła.";
            }
        }
        finally{if(ReferenceEquals(privatePlayCancel,cancel))privatePlayCancel=null;}
    }
    EpgSlot Slot(Station s)=>privateSources.Guide(s,DateTimeOffset.UtcNow)??epg.Get(s);
    async Task AddOwnAddress(string value,PrivateSourceKind? type=null)
    {
        var uri=PrivateProviderClient.Url(value);string extension=Path.GetExtension(uri.AbsolutePath).ToLowerInvariant();
        var kind=type??(new[]{".mp3",".aac",".mp4",".ts",".mpd",".ogg"}.Contains(extension)?PrivateSourceKind.DirectUrl:PrivateSourceKind.M3u);
        var source=new ProviderSource(Guid.NewGuid().ToString("N"),uri.Host,kind,value);
        using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(3));var repository=privateSources;
        bool loaded=await SaveConnectSource(source,timeout.Token,message=>status.Text=message);
        if(!loaded&&ReferenceEquals(repository,privateSources))await ShowPrivateSources();
    }
    async Task LoadOwnChannels(string id)
    {
        var state=privateSources.Sources.Single(s=>s.Source.Id==id);var source=state.Source;
        var stations=privateSources.Stations(id);
        if(stations.Count>0){store.Data.Playlist=MergeStations(stations,store.Data.Playlist.Where(s=>s.PrivateSourceId!=id));store.HydratePrivate(privateSources.Display);store.Save();}
        string title=ModuleTitle(ModuleFor(source.Kind))+" • "+source.Name;
        await ShowLocal(title+(stations.Count==0?" • BRAK KANAŁÓW":""),stations);
        localProviderMessage=SourceConnectionMessage(state);
        if(stations.Count==0){details.Text=localProviderMessage;status.Text=localProviderMessage;}
        else if(state.CatalogFailure!=null)status.Text=localProviderMessage;
        results.Invalidate();RefreshEpgTooltips();
    }
    static ProviderFailureInfo? SourceCatalogFailure(ProviderState state)=>state.CatalogFailure??
        (state.Status.StartsWith("Odrzucono",StringComparison.Ordinal)?new ProviderFailureInfo("connection","access_denied"):null);
    static string SourceConnectionMessage(ProviderState state)
    {
        string failure=SourceCatalogFailure(state) is {} info?ProviderFailures.Describe(info):"";
        if(state.Catalog==null)return (failure.Length>0?failure+" ":"")+"Nie pobrano kanałów tego źródła. Publiczny katalog jest oddzielną listą. Otwórz moduł źródła, aby poprawić dane lub ponowić połączenie.";
        if(failure.Length>0)return failure+" Wyświetlam zapisane kanały z "+state.Catalog.DownloadedAt.ToLocalTime().ToString("dd.MM HH:mm")+". Nie potwierdzono nowego pobrania.";
        return "Lista tego źródła została pobrana. Dostępność obrazu i dźwięku jest sprawdzana po uruchomieniu kanału.";
    }
}
