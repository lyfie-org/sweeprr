using Sweeprr.API.Integrations.Jellyfin.Models;
using Sweeprr.API.Integrations.Matching;
using Sweeprr.API.Integrations.Radarr.Models;
using Sweeprr.API.Integrations.Sonarr.Models;

namespace Sweeprr.Tests.Integrations;

/// <summary>
/// Unit tests for <see cref="MediaMatchingService"/> and <see cref="MediaIdentity"/>.
///
/// All tests use in-memory data — no HTTP, no database.
/// The suite verifies the OR-fallback matching logic, conflict detection,
/// season alignment, and the conservative "ambiguous → never act" guarantee.
/// </summary>
public class MediaMatchingServiceTests
{
    private readonly MediaMatchingService _svc = new();

    // ═══════════════════════════════════════════════════════════════════════════
    // MediaIdentity helpers
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void MediaIdentity_HasAny_Returns_False_When_All_Null()
    {
        var id = new MediaIdentity(null, null, null, null);
        Assert.False(id.HasAny);
    }

    [Theory]
    [InlineData("tt1234567", null, null)]
    [InlineData(null, 12345, null)]
    [InlineData(null, null, 67890)]
    [InlineData("tt0000001", 1, 2)]
    public void MediaIdentity_HasAny_Returns_True_When_Any_Id_Present(
        string? imdb, int? tmdb, int? tvdb)
    {
        var id = new MediaIdentity(imdb, tmdb, tvdb, null);
        Assert.True(id.HasAny);
    }

    [Fact]
    public void MediaIdentity_From_ProviderIds_Maps_All_Fields()
    {
        var pids = new ProviderIds("tt1160419", 438631, 12345);
        var id   = MediaIdentity.From(pids, seasonNumber: 2);

        Assert.Equal("tt1160419", id.ImdbId);
        Assert.Equal(438631,      id.TmdbId);
        Assert.Equal(12345,       id.TvdbId);
        Assert.Equal(2,           id.SeasonNumber);
    }

    [Fact]
    public void MediaIdentity_From_ProviderIds_SeasonNumber_Defaults_Null()
    {
        var pids = new ProviderIds("tt1160419", 438631, null);
        var id   = MediaIdentity.From(pids);
        Assert.Null(id.SeasonNumber);
    }

    [Fact]
    public void MediaIdentity_Empty_Has_No_Ids()
    {
        Assert.False(MediaIdentity.Empty.HasAny);
        Assert.Null(MediaIdentity.Empty.SeasonNumber);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // BuildRadarrIndex
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void BuildRadarrIndex_Indexes_By_TmdbId()
    {
        var movie = MakeMovie(id: 1, tmdb: 438631, imdb: null);
        var index = _svc.BuildRadarrIndex([movie]);

        var result = _svc.MatchMovie(new MediaIdentity(null, 438631, null, null), index);

        var matched = Assert.IsType<MatchResult<RadarrMovie>.Matched>(result);
        Assert.Equal(1, matched.Value.Id);
    }

    [Fact]
    public void BuildRadarrIndex_Indexes_By_ImdbId()
    {
        var movie = MakeMovie(id: 2, tmdb: 99999, imdb: "tt1160419");
        var index = _svc.BuildRadarrIndex([movie]);

        var result = _svc.MatchMovie(new MediaIdentity("tt1160419", null, null, null), index);

        var matched = Assert.IsType<MatchResult<RadarrMovie>.Matched>(result);
        Assert.Equal(2, matched.Value.Id);
    }

    [Fact]
    public void BuildRadarrIndex_Skips_Zero_TmdbId()
    {
        var movie = MakeMovie(id: 3, tmdb: 0, imdb: null);
        var index = _svc.BuildRadarrIndex([movie]);

        var result = _svc.MatchMovie(new MediaIdentity(null, 0, null, null), index);

        Assert.IsType<MatchResult<RadarrMovie>.Unmatched>(result);
    }

    [Fact]
    public void BuildRadarrIndex_Skips_Null_ImdbId()
    {
        var movie = MakeMovie(id: 4, tmdb: 111, imdb: null);
        var index = _svc.BuildRadarrIndex([movie]);

        // Querying with null IMDB should never produce a false positive
        var result = _svc.MatchMovie(new MediaIdentity(null, null, null, null), index);
        Assert.IsType<MatchResult<RadarrMovie>.Unmatched>(result);
    }

    [Fact]
    public void BuildRadarrIndex_Marks_TmdbId_Conflicted_When_Two_Movies_Share_It()
    {
        var a = MakeMovie(id: 10, tmdb: 500, imdb: null);
        var b = MakeMovie(id: 11, tmdb: 500, imdb: null); // same TmdbId
        var index = _svc.BuildRadarrIndex([a, b]);

        var result = _svc.MatchMovie(new MediaIdentity(null, 500, null, null), index);
        Assert.IsType<MatchResult<RadarrMovie>.Ambiguous>(result);
    }

    [Fact]
    public void BuildRadarrIndex_Marks_ImdbId_Conflicted_When_Two_Movies_Share_It()
    {
        var a = MakeMovie(id: 20, tmdb: 1, imdb: "tt9999999");
        var b = MakeMovie(id: 21, tmdb: 2, imdb: "tt9999999"); // same IMDB
        var index = _svc.BuildRadarrIndex([a, b]);

        var result = _svc.MatchMovie(new MediaIdentity("tt9999999", null, null, null), index);
        Assert.IsType<MatchResult<RadarrMovie>.Ambiguous>(result);
    }

    [Fact]
    public void BuildRadarrIndex_Handles_Empty_List()
    {
        var index = _svc.BuildRadarrIndex([]);
        var result = _svc.MatchMovie(new MediaIdentity("tt1234567", 100, null, null), index);
        Assert.IsType<MatchResult<RadarrMovie>.Unmatched>(result);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // MatchMovie
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void MatchMovie_Returns_Unmatched_When_Identity_Has_No_Ids()
    {
        var index = _svc.BuildRadarrIndex([MakeMovie(1, 100, "tt0000001")]);
        var result = _svc.MatchMovie(MediaIdentity.Empty, index);
        Assert.IsType<MatchResult<RadarrMovie>.Unmatched>(result);
    }

    [Fact]
    public void MatchMovie_Returns_Matched_By_TmdbId_Only()
    {
        var movie = MakeMovie(id: 5, tmdb: 27205, imdb: null);
        var index = _svc.BuildRadarrIndex([movie]);

        var result = _svc.MatchMovie(new MediaIdentity(null, 27205, null, null), index);

        var matched = Assert.IsType<MatchResult<RadarrMovie>.Matched>(result);
        Assert.Equal(5, matched.Value.Id);
    }

    [Fact]
    public void MatchMovie_Returns_Matched_By_ImdbId_When_TmdbId_Not_In_Index()
    {
        var movie = MakeMovie(id: 6, tmdb: 12345, imdb: "tt0120737");
        var index = _svc.BuildRadarrIndex([movie]);

        // Identity has a different TMDB (no match) but matching IMDB
        var result = _svc.MatchMovie(new MediaIdentity("tt0120737", 99999, null, null), index);

        var matched = Assert.IsType<MatchResult<RadarrMovie>.Matched>(result);
        Assert.Equal(6, matched.Value.Id);
    }

    [Fact]
    public void MatchMovie_Returns_Matched_When_Both_TmdbId_And_ImdbId_Point_To_Same_Movie()
    {
        var movie = MakeMovie(id: 7, tmdb: 438631, imdb: "tt1160419");
        var index = _svc.BuildRadarrIndex([movie]);

        // Both IDs in the identity resolve to the same Radarr movie → single unique candidate
        var result = _svc.MatchMovie(new MediaIdentity("tt1160419", 438631, null, null), index);

        var matched = Assert.IsType<MatchResult<RadarrMovie>.Matched>(result);
        Assert.Equal(7, matched.Value.Id);
    }

    [Fact]
    public void MatchMovie_Returns_Ambiguous_When_TmdbId_And_ImdbId_Point_To_Different_Movies()
    {
        var dune      = MakeMovie(id: 8,  tmdb: 438631, imdb: null);
        var inception = MakeMovie(id: 9,  tmdb: 0,      imdb: "tt0120737");
        var index = _svc.BuildRadarrIndex([dune, inception]);

        // TmdbId hits dune; ImdbId hits inception → two distinct candidates
        var result = _svc.MatchMovie(new MediaIdentity("tt0120737", 438631, null, null), index);
        Assert.IsType<MatchResult<RadarrMovie>.Ambiguous>(result);
    }

    [Fact]
    public void MatchMovie_Returns_Ambiguous_When_TmdbId_Is_Conflicted_In_Index()
    {
        var a = MakeMovie(id: 30, tmdb: 777, imdb: null);
        var b = MakeMovie(id: 31, tmdb: 777, imdb: null);
        var index = _svc.BuildRadarrIndex([a, b]);

        var result = _svc.MatchMovie(new MediaIdentity(null, 777, null, null), index);
        Assert.IsType<MatchResult<RadarrMovie>.Ambiguous>(result);
    }

    [Fact]
    public void MatchMovie_Returns_Ambiguous_When_ImdbId_Is_Conflicted_In_Index()
    {
        var a = MakeMovie(id: 40, tmdb: 1, imdb: "tt0000001");
        var b = MakeMovie(id: 41, tmdb: 2, imdb: "tt0000001");
        var index = _svc.BuildRadarrIndex([a, b]);

        var result = _svc.MatchMovie(new MediaIdentity("tt0000001", null, null, null), index);
        Assert.IsType<MatchResult<RadarrMovie>.Ambiguous>(result);
    }

    [Fact]
    public void MatchMovie_Returns_Unmatched_When_No_Movie_Has_Matching_Id()
    {
        var movie = MakeMovie(id: 50, tmdb: 100, imdb: "tt0000100");
        var index = _svc.BuildRadarrIndex([movie]);

        var result = _svc.MatchMovie(new MediaIdentity("tt9999999", 999, null, null), index);
        Assert.IsType<MatchResult<RadarrMovie>.Unmatched>(result);
    }

    [Fact]
    public void MatchMovie_ImdbId_Lookup_Is_Case_Insensitive()
    {
        var movie = MakeMovie(id: 60, tmdb: 0, imdb: "tt1234567");
        var index = _svc.BuildRadarrIndex([movie]);

        // Identity provides uppercase variant — should still match
        var result = _svc.MatchMovie(new MediaIdentity("TT1234567", null, null, null), index);
        var matched = Assert.IsType<MatchResult<RadarrMovie>.Matched>(result);
        Assert.Equal(60, matched.Value.Id);
    }

    [Fact]
    public void MatchMovie_ImdbId_With_Whitespace_In_Index_Still_Matches()
    {
        // Radarr data occasionally has leading/trailing spaces in IMDB IDs
        var movie = MakeMovie(id: 61, tmdb: 0, imdb: "  tt1234567  ");
        var index = _svc.BuildRadarrIndex([movie]);

        var result = _svc.MatchMovie(new MediaIdentity("tt1234567", null, null, null), index);
        Assert.IsType<MatchResult<RadarrMovie>.Matched>(result);
    }

    [Fact]
    public void MatchMovie_Third_Movie_Added_After_Conflict_Is_Also_Ignored()
    {
        // Verifies that once a key is conflicted, additional items don't "un-conflict" it
        var a = MakeMovie(id: 70, tmdb: 888, imdb: null);
        var b = MakeMovie(id: 71, tmdb: 888, imdb: null);
        var c = MakeMovie(id: 72, tmdb: 888, imdb: null); // third item with same TMDB
        var index = _svc.BuildRadarrIndex([a, b, c]);

        var result = _svc.MatchMovie(new MediaIdentity(null, 888, null, null), index);
        Assert.IsType<MatchResult<RadarrMovie>.Ambiguous>(result);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // BuildSonarrIndex
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void BuildSonarrIndex_Indexes_By_TvdbId()
    {
        var series = MakeSeries(id: 1, tvdb: 78804, imdb: null);
        var index  = _svc.BuildSonarrIndex([series]);

        var result = _svc.MatchSeries(new MediaIdentity(null, null, 78804, null), index);

        var matched = Assert.IsType<MatchResult<SonarrSeriesMatch>.Matched>(result);
        Assert.Equal(1, matched.Value.Series.Id);
    }

    [Fact]
    public void BuildSonarrIndex_Indexes_By_ImdbId()
    {
        var series = MakeSeries(id: 2, tvdb: 0, imdb: "tt0898266");
        var index  = _svc.BuildSonarrIndex([series]);

        var result = _svc.MatchSeries(new MediaIdentity("tt0898266", null, null, null), index);

        var matched = Assert.IsType<MatchResult<SonarrSeriesMatch>.Matched>(result);
        Assert.Equal(2, matched.Value.Series.Id);
    }

    [Fact]
    public void BuildSonarrIndex_Skips_Zero_TvdbId()
    {
        var series = MakeSeries(id: 3, tvdb: 0, imdb: null);
        var index  = _svc.BuildSonarrIndex([series]);

        var result = _svc.MatchSeries(new MediaIdentity(null, null, 0, null), index);
        Assert.IsType<MatchResult<SonarrSeriesMatch>.Unmatched>(result);
    }

    [Fact]
    public void BuildSonarrIndex_Marks_TvdbId_Conflicted_When_Two_Series_Share_It()
    {
        var a = MakeSeries(id: 10, tvdb: 999, imdb: null);
        var b = MakeSeries(id: 11, tvdb: 999, imdb: null);
        var index = _svc.BuildSonarrIndex([a, b]);

        var result = _svc.MatchSeries(new MediaIdentity(null, null, 999, null), index);
        Assert.IsType<MatchResult<SonarrSeriesMatch>.Ambiguous>(result);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // MatchSeries
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void MatchSeries_Returns_Unmatched_When_Identity_Has_No_Ids()
    {
        var index = _svc.BuildSonarrIndex([MakeSeries(1, 100, "tt0000001")]);
        var result = _svc.MatchSeries(MediaIdentity.Empty, index);
        Assert.IsType<MatchResult<SonarrSeriesMatch>.Unmatched>(result);
    }

    [Fact]
    public void MatchSeries_Returns_Matched_By_TvdbId_Without_Season()
    {
        var series = MakeSeries(id: 5, tvdb: 78804, imdb: null, seasons: [1, 2, 3]);
        var index  = _svc.BuildSonarrIndex([series]);

        var result = _svc.MatchSeries(new MediaIdentity(null, null, 78804, null), index);

        var matched = Assert.IsType<MatchResult<SonarrSeriesMatch>.Matched>(result);
        Assert.Equal(5, matched.Value.Series.Id);
        Assert.Null(matched.Value.Season); // no season requested
    }

    [Fact]
    public void MatchSeries_Resolves_Season_When_SeasonNumber_Specified()
    {
        var series = MakeSeries(id: 6, tvdb: 78804, imdb: null, seasons: [1, 2, 3]);
        var index  = _svc.BuildSonarrIndex([series]);

        var result = _svc.MatchSeries(new MediaIdentity(null, null, 78804, SeasonNumber: 2), index);

        var matched = Assert.IsType<MatchResult<SonarrSeriesMatch>.Matched>(result);
        Assert.Equal(6, matched.Value.Series.Id);
        Assert.NotNull(matched.Value.Season);
        Assert.Equal(2, matched.Value.Season!.SeasonNumber);
    }

    [Fact]
    public void MatchSeries_Returns_Null_Season_When_Season_Not_Present_In_Sonarr()
    {
        var series = MakeSeries(id: 7, tvdb: 78804, imdb: null, seasons: [1, 2]); // no S3
        var index  = _svc.BuildSonarrIndex([series]);

        var result = _svc.MatchSeries(new MediaIdentity(null, null, 78804, SeasonNumber: 3), index);

        // Series matched, but season 3 doesn't exist in Sonarr
        var matched = Assert.IsType<MatchResult<SonarrSeriesMatch>.Matched>(result);
        Assert.Equal(7, matched.Value.Series.Id);
        Assert.Null(matched.Value.Season);
    }

    [Fact]
    public void MatchSeries_Returns_Matched_By_ImdbId_Fallback()
    {
        var series = MakeSeries(id: 8, tvdb: 12345, imdb: "tt0898266");
        var index  = _svc.BuildSonarrIndex([series]);

        // Identity has a non-matching TvdbId but correct ImdbId
        var result = _svc.MatchSeries(new MediaIdentity("tt0898266", null, 99999, null), index);

        var matched = Assert.IsType<MatchResult<SonarrSeriesMatch>.Matched>(result);
        Assert.Equal(8, matched.Value.Series.Id);
    }

    [Fact]
    public void MatchSeries_Returns_Ambiguous_On_TvdbId_Conflict()
    {
        var a = MakeSeries(id: 20, tvdb: 111, imdb: null);
        var b = MakeSeries(id: 21, tvdb: 111, imdb: null);
        var index = _svc.BuildSonarrIndex([a, b]);

        var result = _svc.MatchSeries(new MediaIdentity(null, null, 111, null), index);
        Assert.IsType<MatchResult<SonarrSeriesMatch>.Ambiguous>(result);
    }

    [Fact]
    public void MatchSeries_Returns_Ambiguous_When_TvdbId_And_ImdbId_Point_To_Different_Series()
    {
        var breaking = MakeSeries(id: 30, tvdb: 81189, imdb: null);
        var sopranos = MakeSeries(id: 31, tvdb: 0,     imdb: "tt0141842");
        var index = _svc.BuildSonarrIndex([breaking, sopranos]);

        // TvdbId hits breaking bad; ImdbId hits sopranos → two distinct candidates
        var result = _svc.MatchSeries(new MediaIdentity("tt0141842", null, 81189, null), index);
        Assert.IsType<MatchResult<SonarrSeriesMatch>.Ambiguous>(result);
    }

    [Fact]
    public void MatchSeries_Both_TvdbId_And_ImdbId_Point_To_Same_Series_Returns_Matched()
    {
        var series = MakeSeries(id: 40, tvdb: 81189, imdb: "tt0903747");
        var index  = _svc.BuildSonarrIndex([series]);

        var result = _svc.MatchSeries(new MediaIdentity("tt0903747", null, 81189, null), index);

        var matched = Assert.IsType<MatchResult<SonarrSeriesMatch>.Matched>(result);
        Assert.Equal(40, matched.Value.Series.Id);
    }

    [Fact]
    public void MatchSeries_Returns_Correct_Season_Among_Multiple_Seasons()
    {
        var series = MakeSeries(id: 50, tvdb: 999, imdb: null, seasons: [0, 1, 2, 3, 4, 5]);
        var index  = _svc.BuildSonarrIndex([series]);

        // Request season 4 specifically
        var result = _svc.MatchSeries(new MediaIdentity(null, null, 999, SeasonNumber: 4), index);

        var matched = Assert.IsType<MatchResult<SonarrSeriesMatch>.Matched>(result);
        Assert.Equal(4, matched.Value.Season!.SeasonNumber);
    }

    [Fact]
    public void MatchSeries_Resolves_Season_Zero_Specials_Correctly()
    {
        var series = MakeSeries(id: 51, tvdb: 998, imdb: null, seasons: [0, 1, 2]);
        var index  = _svc.BuildSonarrIndex([series]);

        var result = _svc.MatchSeries(new MediaIdentity(null, null, 998, SeasonNumber: 0), index);

        var matched = Assert.IsType<MatchResult<SonarrSeriesMatch>.Matched>(result);
        Assert.NotNull(matched.Value.Season);
        Assert.Equal(0, matched.Value.Season!.SeasonNumber);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Cross-type guard — Radarr index never matches Sonarr queries and vice versa
    // ═══════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Radarr_Index_Does_Not_Match_On_TvdbId_Because_Radarr_Has_No_TvdbId()
    {
        // Radarr movies are indexed by TMDB + IMDB only; a TvdbId query yields Unmatched
        var movie = MakeMovie(id: 1, tmdb: 438631, imdb: null);
        var index = _svc.BuildRadarrIndex([movie]);

        // Query with only TvdbId — Radarr index has no TVDB entries
        var result = _svc.MatchMovie(new MediaIdentity(null, null, 78804, null), index);
        Assert.IsType<MatchResult<RadarrMovie>.Unmatched>(result);
    }

    [Fact]
    public void Sonarr_Index_Does_Not_Match_On_TmdbId_Because_Sonarr_Has_No_TmdbId()
    {
        var series = MakeSeries(id: 1, tvdb: 78804, imdb: null);
        var index  = _svc.BuildSonarrIndex([series]);

        // Query with only TmdbId — Sonarr index has no TMDB entries
        var result = _svc.MatchSeries(new MediaIdentity(null, 438631, null, null), index);
        Assert.IsType<MatchResult<SonarrSeriesMatch>.Unmatched>(result);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════════════

    private static RadarrMovie MakeMovie(int id, int tmdb, string? imdb) =>
        new RadarrMovie(
            Id: id,
            Title: $"Movie {id}",
            Year: 2020,
            TmdbId: tmdb,
            ImdbId: imdb,
            Monitored: true,
            HasFile: true,
            QualityProfileId: 1,
            Tags: [],
            Path: $"/movies/{id}",
            SizeOnDisk: 1_000_000_000L,
            Added: DateTimeOffset.UtcNow,
            Status: "released",
            MovieFile: null);

    private static SonarrSeries MakeSeries(
        int      id,
        int      tvdb,
        string?  imdb,
        int[]?   seasons = null)
    {
        var seasonList = (seasons ?? [1])
            .Select(n => new SonarrSeason(
                SeasonNumber:       n,
                Monitored:          true,
                EpisodeFileCount:   10,
                EpisodeCount:       10,
                TotalEpisodeCount:  10,
                SizeOnDisk:         500_000_000L))
            .ToList()
            .AsReadOnly();

        return new SonarrSeries(
            Id: id,
            Title: $"Series {id}",
            Year: 2010,
            TvdbId: tvdb,
            ImdbId: imdb,
            Monitored: true,
            QualityProfileId: 1,
            Tags: [],
            Path: $"/tv/{id}",
            Added: DateTimeOffset.UtcNow,
            Ended: false,
            Seasons: seasonList);
    }
}
