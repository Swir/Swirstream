using System.Security.Cryptography;
using System.Text.Json;

namespace SwirStreamAI;

public sealed class PrivateSourceStore
{
    readonly string path;readonly PrivateProviderClient client;readonly object gate=new();List<ProviderState> items=[];bool locked,recoveredFromBackup;
    readonly Dictionary<string,long> revisions=[],catalogRequests=[],playbackRequests=[];
    long generation,sequence;
    readonly record struct Pending(long Generation,long Revision,long Request);
    Pending Begin(string id,Dictionary<string,long> requests)
    {long request=++sequence;requests[id]=request;return new(generation,revisions.GetValueOrDefault(id),request);}
    void CheckPending(string id,Pending pending,Dictionary<string,long> requests,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if(pending.Generation!=generation||pending.Revision!=revisions.GetValueOrDefault(id)||pending.Request!=requests.GetValueOrDefault(id))
            throw new OperationCanceledException("Źródło lub bieżąca operacja zostały zmienione. Zachowano nowszy zapis.");
    }
    // Called when the owning UI changes profile or closes. No in-flight result
    // can then write to this profile, even if a transport ignores cancellation.
    public void InvalidatePending(){lock(gate)generation++;}
    public string Notice {get;private set;}="";
    public IReadOnlyList<ProviderState> Sources {get{lock(gate)return items.ToArray();}}
    static string ConfigKey(ProviderSource source)=>Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(source)));
    public PrivateSourceStore(string profileFolder,PrivateProviderClient? client=null)
    {
        Directory.CreateDirectory(profileFolder);path=Path.Combine(profileFolder,"private-sources.dpapi");this.client=client??new();
        if(!File.Exists(path))return;
        try{items=Load(path);}
        catch(Exception ex)when(ex is IOException or CryptographicException or JsonException or InvalidDataException)
        {
            try{items=Load(path+".bak");recoveredFromBackup=true;Notice="Odczytano chronioną kopię źródeł. Uszkodzony zapis pozostawiono bez nadpisania.";}
            catch(Exception backup)when(backup is IOException or CryptographicException or JsonException or InvalidDataException)
            {locked=true;Notice="Źródła są chronione dla innego konta Windows albo zapis jest uszkodzony. Nie nadpisano plików. Użyj pierwotnego konta Windows lub odzyskaj chronioną kopię.";}
        }
    }
    static List<ProviderState> Load(string file)
    {
        var vault=PrivateData.Read<ProviderVault>(file,"provider-vault-v1");
        if(vault.Schema!=1||vault.Sources is not {Count:<=12}||vault.Sources.Any(s=>s==null)||vault.Sources.Select(s=>s.Source.Id).Distinct().Count()!=vault.Sources.Count)throw new InvalidDataException("Nieprawidłowy chroniony katalog.");
        foreach(var s in vault.Sources)
        {
            PrivateProviderClient.Validate(s.Source);
            if(s.Catalog is {} c&&(c.Channels is not {Count:>0 and <=30000}||c.DownloadedAt>DateTimeOffset.UtcNow.AddMinutes(10)||c.Channels.Any(x=>x==null||x.Station==null||string.IsNullOrWhiteSpace(x.Id)||x.Id.Length>512||string.IsNullOrWhiteSpace(x.Station.Name))))throw new InvalidDataException("Nieprawidłowy chroniony katalog.");
        }
        return vault.Sources.Select(s=>s with{CatalogFailure=ProviderFailures.Normalize(s.CatalogFailure),PlaybackFailure=ProviderFailures.Normalize(s.PlaybackFailure)}).ToList();
    }
    void Save(){if(locked)throw new InvalidOperationException(Notice);PrivateData.Write(path,new ProviderVault(1,items),"provider-vault-v1",backup:!recoveredFromBackup);recoveredFromBackup=false;}
    public void Put(ProviderSource source)
    {
        source=XtreamLink.Normalize(source);PrivateProviderClient.Validate(source);lock(gate)
        {
            if(locked)throw new InvalidOperationException(Notice);
            int index=items.FindIndex(s=>s.Source.Id==source.Id);if(index<0){if(items.Count>=12)throw new InvalidOperationException("Maksymalnie 12 własnych źródeł w profilu.");items.Add(new(source));}
            else items[index]=items[index] with{Source=source,Status="Zmieniono ustawienia. Odśwież źródło przed odtwarzaniem."};
            revisions[source.Id]=++sequence;
            Save();
        }
    }
    public void Remove(string id){lock(gate){items.RemoveAll(s=>s.Source.Id==id);revisions[id]=++sequence;Save();}}
    public Dictionary<string,Station> ImportSaved(string name,IEnumerable<Station> stations)
    {
        var config=new ProviderSource(Guid.NewGuid().ToString("N"),name,PrivateSourceKind.SavedChannels,"");
        var rows=stations.DistinctBy(s=>s.Url).Select(s=>new ProviderChannel(Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(s.Url))).ToLowerInvariant(),s)).Take(30000).ToList();
        lock(gate){if(locked)throw new InvalidOperationException(Notice);if(items.Count>=12)throw new InvalidOperationException("Brak miejsca na chronioną migrację. Usuń nieużywane źródło i spróbuj ponownie.");items.Add(new(config,new(rows,"",DateTimeOffset.UtcNow,null),Status:"Przeniesiono wcześniejsze prywatne adresy. Nie wykonywano nowego pobrania sieciowego.",CatalogConfigKey:ConfigKey(config)));Save();}
        return rows.ToDictionary(c=>c.Station.Url,c=>PrivateStation.Reference(config.Id,c));
    }
    public Task<ProviderState> Refresh(string id,CancellationToken ct)
    {
        lock(gate){var previous=items.Single(s=>s.Source.Id==id);return ConnectInternal(previous.Source,ct,previous);}
    }
    // Candidate settings are committed only with their successful catalogue. A
    // failed edit retains the usable previous credentials and config fingerprint.
    public Task<ProviderState> Connect(ProviderSource source,CancellationToken ct)=>ConnectInternal(source,ct,null);
    async Task<ProviderState> ConnectInternal(ProviderSource source,CancellationToken ct,ProviderState? expected)
    {
        source=XtreamLink.Normalize(source);PrivateProviderClient.Validate(source);ct.ThrowIfCancellationRequested();
        ProviderState? before;Pending pending;lock(gate)
        {
            if(locked)throw new InvalidOperationException(Notice);
            before=items.FirstOrDefault(s=>s.Source.Id==source.Id);
            if(expected!=null&&!ReferenceEquals(before,expected))throw new OperationCanceledException("Źródło zostało zmienione przed odświeżeniem. Zachowano nowszy zapis.");
            if(before==null&&items.Count>=12)throw new InvalidOperationException("Maksymalnie 12 własnych źródeł w profilu.");
            pending=Begin(source.Id,catalogRequests);
        }
        var previous=before??new ProviderState(source);
        ProviderState replacement;var attempted=DateTimeOffset.UtcNow;
        try
        {
            var catalog=await client.Refresh(source,ct).ConfigureAwait(false);ct.ThrowIfCancellationRequested();
            if(catalog.Channels.Count==0)throw new InvalidDataException("Źródło zwróciło pustą listę. Poprzednia lista została zachowana.");
            EpgFeed? guide=previous.Guide;string epgNote="";
            if(catalog.EpgUrl.Length>0)
            {
                try
                {
                    byte[] xml=await client.DownloadEpg(source,catalog.EpgUrl,ct).ConfigureAwait(false);
                    guide=EpgService.Parse(xml,"","Moje źródło",null,DateTimeOffset.UtcNow,ct);
                    epgNote=" • EPG: "+guide.Channels.Count+" kanałów";
                }
                catch(Exception ex)when(ex is not OperationCanceledException){epgNote=" • EPG niedostępne; zachowano poprzednią ramówkę.";}
            }
            replacement=previous with{Source=source,Catalog=catalog,LastAttempt=attempted,Guide=guide,CatalogConfigKey=ConfigKey(source),CatalogFailure=null,Status="Pobrano "+catalog.Channels.Count+" kanałów "+catalog.DownloadedAt.ToLocalTime().ToString("dd.MM HH:mm")+epgNote+(catalog.Notice.Length>0?" • "+catalog.Notice:"")};
        }
        catch(OperationCanceledException){throw;}
        catch(Exception ex)
        {
            var failure=ProviderFailures.FromException(ex);
            replacement=previous with{LastAttempt=attempted,CatalogFailure=failure,Status=ProviderFailures.Describe(failure)+(before!=null?" Zachowano poprzednie dane dostępu, kanały i datę ostatniego udanego pobrania. Nie zapisano nieudanej zmiany.":" Zapisano konfigurację nowego źródła do poprawienia; kanały nie zostały pobrane.")};
        }
        lock(gate)
        {
            CheckPending(source.Id,pending,catalogRequests,ct);
            int index=items.FindIndex(s=>s.Source.Id==source.Id);
            if(before==null?index>=0:index<0)throw new OperationCanceledException("Źródło zmieniło się podczas pobierania. Zachowano nowszy zapis.");
            var current=index<0?null:items[index];
            // A concurrent playback attempt only owns playback diagnostics; it
            // must neither cancel a refresh nor have its result overwritten.
            if(current!=null)replacement=replacement with{PlaybackFailure=current.PlaybackFailure,LastPlaybackAttempt=current.LastPlaybackAttempt};
            if(index<0){if(items.Count>=12)throw new InvalidOperationException("Maksymalnie 12 własnych źródeł w profilu.");items.Add(replacement);}else items[index]=replacement;
            try{Save();}catch{if(index<0)items.Remove(replacement);else items[index]=current!;throw;}
            if(current==null||current.Source!=replacement.Source)revisions[source.Id]=++sequence;
            return replacement;
        }
    }
    public List<Station> Stations(string id){lock(gate){var s=items.FirstOrDefault(s=>s.Source.Id==id);return s?.Catalog?.Channels.Select(c=>PrivateStation.Reference(id,c)).ToList()??[];}}
    public Station Display(Station station)
    {
        if(!PrivateStation.IsPrivate(station))return station;
        lock(gate){var source=items.FirstOrDefault(s=>s.Source.Id==station.PrivateSourceId);var channel=source?.Catalog?.Channels.FirstOrDefault(c=>c.Id==station.PrivateStreamId);return channel==null?PrivateStation.ForStorage(station) with{Name="Niedostępne prywatne źródło — odśwież lub dodaj je w tym profilu"}:PrivateStation.Reference(source!.Source.Id,channel);}
    }
    public async Task<ProviderPlayback> Resolve(Station station,CancellationToken ct)
    {
        ProviderState source;Pending pending;
        lock(gate)
        {
            ct.ThrowIfCancellationRequested();
            source=items.FirstOrDefault(s=>s.Source.Id==station.PrivateSourceId)??throw new InvalidOperationException("Tego prywatnego źródła nie ma w bieżącym profilu.");
            pending=Begin(source.Source.Id,playbackRequests);
        }
        DateTimeOffset attempted=DateTimeOffset.UtcNow;
        ProviderPlayback playback;
        try
        {
            if(source.CatalogConfigKey!=ConfigKey(source.Source))throw new InvalidOperationException("Odśwież źródło po zmianie danych dostępu.");
            var channel=source.Catalog?.Channels.FirstOrDefault(c=>c.Id==station.PrivateStreamId)??throw new InvalidOperationException("Kanał jest niedostępny w zapisanym źródle. Odśwież jego listę.");
            playback=await client.Resolve(source.Source,channel,ct).ConfigureAwait(false);
        }
        catch(OperationCanceledException){throw;}
        catch(Exception ex)
        {
            CommitPlayback(source.Source.Id,pending,attempted,ProviderFailures.FromException(ex),ct);
            throw;
        }
        CommitPlayback(source.Source.Id,pending,attempted,null,ct);
        return playback;
    }
    void CommitPlayback(string id,Pending pending,DateTimeOffset attempted,ProviderFailureInfo? failure,CancellationToken ct)
    {
        lock(gate)
        {
            CheckPending(id,pending,playbackRequests,ct);
            int index=items.FindIndex(s=>s.Source.Id==id);
            if(index<0)throw new OperationCanceledException("Prywatne źródło zostało usunięte.");
            var current=items[index];
            items[index]=current with{PlaybackFailure=failure,LastPlaybackAttempt=attempted};
            try{Save();}
            catch
            {
                items[index]=current;
                // Diagnostic persistence must not replace the original access
                // error or turn an otherwise valid media link into a failure.
                Notice="Nie zapisano wyniku ostatniej próby odtwarzania. Sprawdź możliwość zapisu chronionych danych profilu.";
            }
        }
    }
    public EpgSlot? Guide(Station station,DateTimeOffset now)
    {
        if(!PrivateStation.IsPrivate(station))return null;
        lock(gate)
        {
            var s=items.FirstOrDefault(x=>x.Source.Id==station.PrivateSourceId);var raw=s?.Catalog?.Channels.FirstOrDefault(x=>x.Id==station.PrivateStreamId)?.Station;
            if(s?.Guide is not {} feed||raw==null)return new(null,null,null);
            var channel=feed.Channels.FirstOrDefault(c=>raw.ChannelId.Length>0&&c.Id.Equals(raw.ChannelId,StringComparison.OrdinalIgnoreCase))??feed.Channels.FirstOrDefault(c=>c.Names.Any(n=>ChannelSearch.Key(n)==ChannelSearch.Key(raw.Name)));
            if(channel==null)return new(null,null,null);
            return new(channel.Programmes.FirstOrDefault(p=>p.Start<=now&&p.Stop>now),channel.Programmes.FirstOrDefault(p=>p.Start>now),new(channel,feed,"EPG prywatnego źródła"));
        }
    }
}
