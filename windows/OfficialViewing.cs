namespace SwirStreamAI;

public static class OfficialViewing
{
    // These are information/browser routes, never direct VLC streams or playback evidence.
    public static List<Station> Missing(IEnumerable<Station> directory, IEnumerable<Station> streams)
    {
        var present = streams.Select(SourceRanker.ChannelKey).ToHashSet();
        return directory.Where(s => !present.Contains(SourceRanker.ChannelKey(s)) && PublicWeb.Allowed(s.Homepage)).Select(s =>
        {
            string url = s.Homepage, note = "Strona nadawcy. Nie znaleziono bezpośredniego adresu dla VLC; dostępność sprawdzisz na stronie.";
            if (s.Country == "PL" && new[] { "TVN.pl", "TVN7.pl", "TVN24.pl", "TVN24BiS.pl", "TVNFabula.pl", "TVNStyle.pl", "TVNTurbo.pl" }.Contains(s.ChannelId))
            { url = "https://player.pl/live"; note = "Player: oglądanie w przeglądarce. Wymagane konto; część kanałów wymaga pakietu. Warunki i dostępność podaje Player."; }
            if (s.Country == "PL" && new[] { "HBO.pl", "HBO2.pl", "HBO3.pl" }.Contains(s.ChannelId))
            { url = "https://www.play.pl/uslugi/serwisy-wideo/hbo-max"; note = "Informacja operatora o HBO / HBO 2 / HBO 3. Odbiór zależy od wykupionego pakietu. Ten odnośnik nie jest transmisją."; }
            return s with { Url = url + (new Uri(url).Fragment.Length == 0 ? "#swir-channel=" + Uri.EscapeDataString(s.ChannelId) : ""), Source = "Oficjalna strona • WWW", BrowserOnly = true, AccessNote = note };
        }).ToList();
    }
}
