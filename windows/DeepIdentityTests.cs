using System.Text.Json;

namespace SwirStreamAI;

public static class DeepIdentityTests
{
    public static int Run(string report)
    {
        bool success = true; var checks = new List<object>();
        void Check(string name, bool ok, object? detail = null) { checks.Add(new { name, ok, detail }); success &= ok; }
        try
        {
            var english = new Station("World (English)", "https://cdn.example/en.m3u8", "TV", Country: "DE", ChannelId: "World.de", Feed: "EnglishHD");
            var german = english with { Name = "World [German]", Url = "https://cdn.example/de.m3u8", Feed = "GermanHD" };
            var englishSd = english with { Name = "World English (480p)", Url = "https://cdn.example/en-sd.m3u8", Feed = "EnglishSD" };
            Check("Parenthesized and bracketed language names stay distinct", ChannelSearch.Key(english.Name) != ChannelSearch.Key(german.Name));
            Check("Regional labels stay distinct", ChannelSearch.Key("News (Europe)") != ChannelSearch.Key("News (Asia)"));
            Check("Pure quality and availability labels do not split one program", ChannelSearch.Key("News (1080p) [Geo-blocked] [Not 24/7]") == ChannelSearch.Key("News HD"));
            Check("Plus one is different from a channel numbered one", ChannelSearch.Key("Demo +1") != ChannelSearch.Key("Demo 1"));
            Check("Numbered query does not match a timeshift name", !ChannelSearch.Matches("Demo 1", new("Demo +1", "https://cdn.example/timeshift", "TV")));
            Check("Punctuation alone does not match all channels", !ChannelSearch.Matches("!!!", english));
            Check("Non-Latin names still retain identity", ChannelSearch.Key("中央电视台") != "" && ChannelSearch.Matches("中央电视台", english with { Name = "中央电视台" }));
            Check("Legacy CamelCase IDs expose language words", ChannelSearch.Names(english with { Name = "World", ChannelId = "DWEnglish.de", Feed = "" }).Contains("DW English"));
            Check("Feed names are searchable with the channel name", ChannelSearch.Matches("World English", english with { Name = "World", Feed = "EnglishHD" }));
            Check("Channel country suffix is removed before a feed suffix", ChannelSearch.IdentifierName("France24.fr@EnglishHD") == "France24 English HD");
            Check("Radio UUID is not promoted into channel aliases", !ChannelSearch.Names(english with { Kind = "Radio", Name = "Radio", Feed = "", ChannelId = "secret-station-uuid" }).Contains("secret-station-uuid"));

            List<Station> directory = [english, german];
            var specific = ChannelSearch.Filter("World English", [english, german, englishSd], directory);
            Check("Exact English query keeps both qualities and excludes German feed", specific.Count == 2 && specific.All(s => ChannelSearch.ProgramVariant(s.Feed) == "english"));
            Check("Canonical channel search still offers separate language variants", ChannelSearch.Filter("World", [english, german, englishSd], directory).Count == 3);
            Check("Alternative-language alias cannot defeat explicit feed identity", ChannelSearch.Filter("World English", [german with { Aliases = ["World English"] }], directory).Count == 0);
            Check("Missing feed cannot authorize a differently named language program", !ChannelSearch.SameProgram(english with { Feed = "" }, german with { Feed = "" }));
            Check("Quality-only feeds do not erase names declaring different languages", !ChannelSearch.SameProgram(english with { Feed = "HD" }, german with { Feed = "SD" }));
            Check("English HD and SD remain valid alternatives", ChannelSearch.SameProgram(english, englishSd));
            Check("Identical program names with quality-only feeds remain alternatives", ChannelSearch.SameProgram(english with { Feed = "HD" }, englishSd with { Feed = "SD" }));
            Check("Country disagreement is not hidden by equal channel ID", ChannelSearch.Filter("World English", [english with { Country = "PL" }], directory).Count == 0);

            var old = new Station("Polsat Sport", "https://old.example/", "TV", Country: "PL", ChannelId: "PolsatSport.pl", Aliases: ["Sport dawniej"]);
            var intermediate = old with { Name = "Sport Interim", ChannelId = "SportInterim.pl", Aliases = [] };
            var current = old with { Name = "Polsat Sport 1", ChannelId = "PolsatSport1.pl", Aliases = [] };
            var successors = ChannelSearch.AddReplacementAliases([old, intermediate, current], new Dictionary<string, string> { [old.ChannelId] = intermediate.ChannelId, [intermediate.ChannelId] = current.ChannelId });
            var resolved = successors.Single(s => s.ChannelId == current.ChannelId);
            Check("Declared successor inherits historical name and alias through a chain", ChannelSearch.Names(resolved).Contains("Polsat Sport") && ChannelSearch.Names(resolved).Contains("Sport dawniej") && ChannelSearch.Names(resolved).Contains("Sport Interim"));
            Check("Successor aliases do not overwrite historical channel IDs", successors[0].ChannelId == old.ChannelId && successors[1].ChannelId == intermediate.ChannelId);
            Check("Old name can discover the current channel stream", ChannelSearch.Filter("Polsat Sport", [resolved with { Url = "https://cdn.example/current.m3u8" }], successors).Count == 1);
            var cyclic = ChannelSearch.AddReplacementAliases([old, current], new Dictionary<string, string> { [old.ChannelId] = current.ChannelId, [current.ChannelId] = old.ChannelId });
            Check("Cyclic replacement data creates no aliases", cyclic[1].Aliases!.Length == 0);
            var crossCountry = ChannelSearch.AddReplacementAliases([old, current with { Country = "DE" }], new Dictionary<string, string> { [old.ChannelId] = current.ChannelId });
            Check("Replacement chain cannot cross country identity", crossCountry[1].Aliases!.Length == 0);
            var missing = ChannelSearch.AddReplacementAliases([old, current], new Dictionary<string, string> { [old.ChannelId] = "Missing.pl" });
            Check("Unknown replacement endpoint creates no guessed mapping", missing[1].Aliases!.Length == 0);

            string payload = "[{\"channel\":\"World.de\",\"feed\":\"EnglishHD\",\"title\":\"World English\",\"url\":\"https://cdn.example:1935/shared.m3u8\",\"user_agent\":\"Player/2\",\"referrer\":\"https://world.example/\",\"labels\":[\"Not 24/7\"],\"quality\":\"1080p\"},{\"channel\":\"World.de\",\"feed\":\"GermanHD\",\"title\":\"World German\",\"url\":\"https://cdn.example:1935/shared.m3u8\"}]";
            var parsed = ChannelSearch.ParseStreamIndex(payload);
            Check("Same manifest URL retains explicitly distinct feeds", parsed.Count == 2 && parsed.Select(s => s.Feed).Distinct().Count() == 2);
            Check("Published playback headers and labels survive stream parsing", parsed[0].UserAgent == "Player/2" && parsed[0].Referrer == "https://world.example/" && parsed[0].Group == "Not 24/7" && parsed[0].Quality == "1080p");
            Check("Unrecognized declared ID cannot borrow another channel's title", ChannelSearch.JoinStreamIndex([parsed[0] with { ChannelId = "Unknown.de" }], [english], ["DE"], "World English").Count == 0);
            var baseChannel=new Station("France 24","https://france24.example/",Country:"FR",ChannelId:"France24.fr");
            var languageRows=ChannelSearch.JoinStreamIndex([baseChannel with {Url="https://cdn.example/english.m3u8",Feed="English"},baseChannel with {Url="https://cdn.example/french.m3u8",Feed="French"}],[baseChannel],["FR"],"France 24 English");
            Check("Generic publisher title with English feed remains discoverable",languageRows.Count==1&&languageRows[0].Feed=="English"&&languageRows[0].Country=="FR");
            Check("Generic title does not lend English identity to French feed",languageRows.All(s=>s.Url!="https://cdn.example/french.m3u8"));
        }
        catch (Exception ex) { Check("Unexpected failure", false, ex.ToString()); }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(report))!);
        File.WriteAllText(report, JsonSerializer.Serialize(new { success, checks }, new JsonSerializerOptions { WriteIndented = true }));
        return success ? 0 : 1;
    }
}
