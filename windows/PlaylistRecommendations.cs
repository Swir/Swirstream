namespace SwirStreamAI;

public record PlaylistRecommendation(FoundPlaylist Playlist,int MatchingChannels,int ScopedChannels,int VerifiedChannels,double Preference);
public static class PlaylistRecommendations
{
    // Ranking only: never change a playlist, its dates or decoder verification.
    // Each programme contributes once, regardless of redundant stream addresses.
    public static List<PlaylistRecommendation> Order(IEnumerable<FoundPlaylist> playlists,string query,string[] countries,SearchPrioritySnapshot preferences,IReadOnlySet<string> verified)
    {
        return playlists.Select(p=>
        {
            var scoped=p.Stations.Where(s=>countries.Length==0||countries.Contains(s.Country)).ToList();
            var matches=scoped.Where(s=>ChannelSearch.Matches(query,s)).GroupBy(SourceRanker.ChannelKey).ToList();
            return new PlaylistRecommendation(p,matches.Count,scoped.Select(SourceRanker.ChannelKey).Distinct().Count(),matches.Count(g=>g.Any(s=>verified.Contains(s.Url))),matches.Count==0?0:matches.Average(g=>g.Max(preferences.Score)));
        }).OrderByDescending(p=>p.MatchingChannels>0).ThenByDescending(p=>p.ScopedChannels>0).ThenBy(p=>p.Playlist.IsStale).ThenByDescending(p=>p.MatchingChannels).ThenByDescending(p=>p.VerifiedChannels).ThenByDescending(p=>p.Preference).ThenByDescending(p=>p.Playlist.ModifiedAt??p.Playlist.FetchedAt).ThenBy(p=>p.Playlist.Id,StringComparer.Ordinal).ToList();
    }
}
