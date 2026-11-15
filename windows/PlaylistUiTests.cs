namespace SwirStreamAI;

public sealed partial class MainForm
{
    public void PlaylistUiTest(Action<string,bool,object?> check)
    {
        store.Data.SelectedCountries=["PL"];candidates=[];catalogDataAt=DateTimeOffset.MinValue;
        string raw="#EXTM3U x-tvg-url=\"https://example.org/guide.xml\"\n#EXTINF:-1 tvg-id=\"PlaylistPL.pl\" tvg-country=\"PL\",Playlist PL\nhttps://example.org/pl.m3u8\n#EXTINF:-1 tvg-id=\"PlaylistDE.de\" tvg-country=\"DE\",Playlist DE\nhttps://example.org/de.m3u8\n";
        var p=PlaylistRepository.Create(new("https://example.org/list.m3u",raw,"application/x-mpegurl",DateTimeOffset.UtcNow),[],["PL"],"");
        foundPlaylists.Save(p);
        var pl=foundPlaylists.GetStations(p.Id,["PL"]);
        ImportFoundStations(pl,p.Name,p.FetchedAt);
        check("Found playlist import respects selected countries and persists library",shown.Count==1&&shown[0].Country=="PL"&&store.Data.Playlist.Any(s=>s.Url==pl[0].Url)&&store.Data.SelectedCountries.SequenceEqual(new[]{"PL"}),shown.Count);
        ImportFoundStations(foundPlaylists.GetStations(p.Id,[]),p.Name,p.FetchedAt);
        check("Whole playlist import expands country scope consistently",shown.Count==2&&store.Data.SelectedCountries.Length==0&&catalogScope==Scope(kind.Text,[]),shown.Count);
        string export=Path.Combine(dataFolder,"ui-playlist-export.m3u");foundPlaylists.ExportFiltered(p.Id,export,["DE"]);
        var roundtrip=M3u.Parse(File.ReadAllText(export),export,true);
        check("UI playlist repository export reloads country and EPG metadata",roundtrip.Count==1&&roundtrip[0].Country=="DE"&&File.ReadAllText(export).Contains("x-tvg-url="),roundtrip.Count);
        check("Import alone never marks a source as decoded",!ranker.Results.ContainsKey(pl[0].Url),null);
    }
}
