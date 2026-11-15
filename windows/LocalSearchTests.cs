using System.Text.Json;

namespace SwirStreamAI;

public static class LocalSearchTests
{
    public static int Run(string report, bool network = false)
    {
        bool success = true; var checks = new List<object>();
        void Check(string name, bool ok, object? detail = null) { checks.Add(new { name, ok, detail }); success &= ok; }
        static List<Station> Local(string query, params Station[] rows) => ChannelSearch.FilterLocal(query, rows).ToList();
        try
        {
            var missingQuery=ChannelSearch.LocalQuery("Polskie Radio",[new("Example TV","https://cdn.example/tv","TV")]);
            Check("A missing literal radio title never becomes an empty query matching all channels",missingQuery=="Polskie Radio"&&ChannelSearch.FilterLocal(missingQuery,[new("Example TV","https://cdn.example/tv","TV")]).Count==0);
            var legacyDw = new Station("DW English", "https://cdn.example/dw-en.m3u8", "TV", Country: "DE", ChannelId: "DWEnglish.de", Aliases: ["Deutsche Welle English"], Feed: "EnglishHD");
            Check("Local canonical query retains an already downloaded legacy ID", Local("DW", legacyDw).Single().ChannelId == "DWEnglish.de");
            Check("Local full feed title and existing alias both find the same source", Local("DW English", legacyDw).Single().Url == legacyDw.Url && Local("Deutsche Welle English", legacyDw).Single().Url == legacyDw.Url);
            var idOnlyName = legacyDw with { Name = "DW", Aliases = [], Feed = "" };
            Check("Legacy ID words remain searchable when a playlist shortened the title", Local("DW English", idOnlyName).Single().ChannelId == legacyDw.ChannelId);

            var tv = new Station("News", "https://cdn.example/news-tv.m3u8", "TV", Country: "US", ChannelId: "News.us");
            var radio = new Station("News", "https://radio.example/news", "Radio", Country: "PL", ChannelId: "station-uuid", Group: "news");
            var named = Local("News", tv, radio);
            Check("Same local name in TV and radio is not resolved through a global TV ID", named.Count == 2 && named.Contains(tv) && named.Contains(radio));
            Check("Local filtering preserves the caller's already scoped radio collection", Local("News", radio).SequenceEqual([radio]));
            var owned = new Station("Unique Local", "https://owned.example/live", "Live");
            Check("Owned playlist title with unknown country remains locally searchable", Local("Unique", owned).SequenceEqual([owned]) && owned.Country == "" && owned.Kind == "Live");

            var polskieRadio = new Station("Polskie Radio", "https://radio.example/polskie", "Radio", Country: "PL", ChannelId: "radio-uuid");
            Check("A real title containing country and media words is not stripped", ChannelSearch.LocalQuery("Polskie Radio", [polskieRadio]) == "Polskie Radio");
            var radio24 = polskieRadio with { Name = "Polskie Radio 24", Url = "https://radio.example/24" };
            Check("A matching title prefix remains a local query", ChannelSearch.LocalQuery("Polskie Radio", [radio24]) == "Polskie Radio" && Local("Polskie Radio", radio24).Count == 1);
            Check("Known alias text is preserved by local query interpretation", ChannelSearch.LocalQuery("Deutsche Welle English", [legacyDw]) == "Deutsche Welle English");

            var one = new Station("TVP1", "https://cdn.example/tvp1", "TV", Country: "PL", ChannelId: "TVP1.pl", Aliases: ["Jedynka"], Feed: "HD");
            var ten = one with { Name = "TVP10", Url = "https://cdn.example/tvp10", ChannelId = "TVP10.pl", Aliases = [] };
            string interpreted = ChannelSearch.LocalQuery("znajdź polskie kanały TVP1", [one, ten]);
            Check("Natural command resolves to the same local channel as its stripped name", ChannelSearch.Key(interpreted) == "tvp1" && Local(interpreted, one, ten).SequenceEqual([one]), interpreted);
            Check("Spaced numbered title excludes a different numbered channel", Local("TVP 1", one, ten).SequenceEqual([one]));
            Check("Quality-only suffix does not make an existing channel unfindable", Local("TVP1 HD", one with { Name = "TVP 1 (1080p)" }, ten).Count == 1);
            Check("Local alias and partial alphabetic name work without a directory", Local("Jedynka", one, ten).SequenceEqual([one]) && Local("TVP", one, ten).Count == 2);
            var numbered = new Station("Demo 1", "https://cdn.example/numbered", "TV");
            var delayed = numbered with { Name = "Demo +1", Url = "https://cdn.example/delayed" };
            Check("Numbered title does not absorb the timeshift channel", Local("Demo 1", numbered, delayed).SequenceEqual([numbered]));
            Check("Explicit plus-one query retains the timeshift channel only", Local("Demo +1", numbered, delayed).SequenceEqual([delayed]));
            Check("Clearing a query restores every record supplied by the active view", Local("", legacyDw, tv, radio, owned).Count == 4);
            foreach(var pair in new[]{("filmy","movies"),("wiadomości","news"),("muzyka","music"),("dzieci","kids"),("dokumenty","documentary"),("sportowe","sports")})
            {
                var category=new Station("Imported channel","https://owned.example/"+pair.Item2,"Live",Group:"PL | "+pair.Item2);
                Check("Polish whole-genre query finds imported provider category "+pair.Item2,Local(pair.Item1,category,one).SequenceEqual([category]));
                Check("English genre finds Polish provider category "+pair.Item2,Local(pair.Item2,category with{Group=pair.Item1}).Count==1);
            }
            Check("Genre expansion does not match category-like substrings or alter channel-name queries",Local("filmy",owned with{Group="FilmyBox"}).Count==0&&Local("Film 1",owned with{Group="movies"}).Count==0&&Local("TVP 1",one,ten with{Group="movies"}).SequenceEqual([one]));
            var namedNews=new Station("News 24","https://owned.example/exact","Live",ChannelId:"Exact.pl");var otherNews=owned with{Group="news",ChannelId="Other.pl"};
            Check("Discovery exact named channel identity remains precise",ChannelSearch.Filter("News 24",[namedNews,otherNews],[namedNews,otherNews]).SequenceEqual([namedNews]));
            var foreignFilm=new Station("Filmy","https://owned.example/india","TV",Country:"IN",ChannelId:"Filmy.in");var polishFilm=owned with{Country="PL",Group="movies",ChannelId="Cinema.pl"};
            Check("Foreign exact channel name cannot erase scoped Polish genre results",ChannelSearch.Filter("filmy",[polishFilm],[foreignFilm,polishFilm]).SequenceEqual([polishFilm]));
            Check("Whole genre query keeps both same-name channel and categorized channels",ChannelSearch.Filter("news",[namedNews,otherNews],[namedNews,otherNews]).Count==2);

            const string shared = "https://cdn.example/shared.m3u8";
            var english = legacyDw with { Name = "World English", Url = shared, ChannelId = "World.de", Aliases = ["English source alias"], Feed = "EnglishHD", UserAgent = "OriginalPlayer/1", Referrer = "https://world.example/" };
            var german = english with { Name = "World German", Aliases = ["German source alias"], Feed = "GermanHD", UserAgent = "", Referrer = "" };
            var languages = MainForm.MergeStations([german], [english]);
            Check("Same URL does not collapse explicitly different language programs", languages.Count == 2 && languages.Select(s => ChannelSearch.ProgramVariant(s.Feed)).ToHashSet().SetEquals(["english", "german"]));
            Check("Merging a German source cannot lend it the English title alias", Local("World English", languages.ToArray()).Count == 1 && Local("World English", languages.ToArray())[0].Feed == "EnglishHD" && !ChannelSearch.Names(languages.Single(s => s.Feed == "GermanHD")).Contains("English source alias"));
            var polish = english with { Country = "PL", Aliases = ["Polish source alias"], UserAgent = "", Referrer = "" };
            var countries = MainForm.MergeStations([polish], [english]);
            Check("Same URL and ID in different declared countries stay separate", countries.Count == 2 && countries.Select(s => s.Country).ToHashSet().SetEquals(["PL", "DE"]) && !ChannelSearch.Names(countries.Single(s => s.Country == "PL")).Contains("English source alias"));

            var sd = english with { Name = "World English (480p)", Feed = "EnglishSD", Quality = "480p", Aliases = ["Current English source"], UserAgent = "", Referrer = "" };
            var qualities = MainForm.MergeStations([sd], [english]);
            Check("Quality variants of one program at one URL still merge", qualities.Count == 1 && ChannelSearch.ProgramVariant(qualities[0].Feed) == "english");
            Check("Equivalent source merge preserves playback headers absent in fresh metadata", qualities.Single().UserAgent == "OriginalPlayer/1" && qualities.Single().Referrer == "https://world.example/");
            Check("Equivalent source merge preserves old and new searchable aliases", ChannelSearch.Names(qualities.Single()).Contains("English source alias") && ChannelSearch.Names(qualities.Single()).Contains("Current English source"));
            var freshHeaders = sd with { UserAgent = "UpdatedPlayer/2", Referrer = "https://world.example/player" };
            var updated = MainForm.MergeStations([freshHeaders], [english]).Single();
            Check("Explicit fresh playback headers replace older header values", updated.UserAgent == "UpdatedPlayer/2" && updated.Referrer == "https://world.example/player");
            Check("Repeated equivalent merge is stable rather than duplicating a record", MainForm.MergeStations([updated], MainForm.MergeStations([updated], [english])).Count == 1);
            if (network)
            {
                using var deadline=new CancellationTokenSource(TimeSpan.FromMinutes(2));
                var catalog=new SmartCatalog(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(report))!,"local-search-production-data"));
                var downloaded=catalog.Search("","TV",["PL","DE"],true,deadline.Token).GetAwaiter().GetResult();
                var metadata=catalog.Broadcasters(true,deadline.Token).GetAwaiter().GetResult();
                Check("Production PL/DE catalog and current channel metadata were downloaded",downloaded.Stations.Count>0&&metadata.Stations.Count>10000,new{downloaded=downloaded.Stations.Count,metadata=metadata.Stations.Count,downloaded.DataAt,downloaded.Detail});
                var dw=ChannelSearch.FilterLocal("DW",downloaded.Stations);
                var expected=downloaded.Stations.Where(s=>ChannelSearch.Matches("DW",s)).ToList();
                Check("Real downloaded DW entries remain locally searchable despite legacy IDs",dw.Count>0&&dw.SequenceEqual(expected),new{local=dw.Count,strictDiscovery=ChannelSearch.Filter("DW",downloaded.Stations,metadata.Stations).Count,channels=dw.Select(s=>new{s.Name,s.ChannelId,s.Feed,s.Country}).ToArray()});
                var tvp=ChannelSearch.FilterLocal("TVP 1",downloaded.Stations);
                Check("Real downloaded TVP 1 search does not confuse TVP 10",tvp.All(s=>ChannelSearch.Key(s.Name)!="tvp10"),new{matches=tvp.Count,names=tvp.Select(s=>s.Name).Distinct().ToArray(),availabilityRequired=false});
            }
        }
        catch (Exception ex) { Check("Unexpected failure", false, ex.ToString()); }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(report))!);
        File.WriteAllText(report, JsonSerializer.Serialize(new { success, checks }, new JsonSerializerOptions { WriteIndented = true }));
        return success ? 0 : 1;
    }
}
