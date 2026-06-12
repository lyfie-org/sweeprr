namespace Sweeprr.API.Integrations.Sonarr.Dto;

internal sealed class SonarrEpisodeDto
{
    public int     Id            { get; set; }
    public int     SeriesId      { get; set; }
    public int     SeasonNumber  { get; set; }
    public int     EpisodeNumber { get; set; }
    public int?    EpisodeFileId { get; set; }
    public int?    TvdbId        { get; set; }
    public bool    HasFile       { get; set; }
    public bool    Monitored     { get; set; }
    public string? AirDate       { get; set; }
    public string? Title         { get; set; }
    public string? FinaleType    { get; set; }  // "series" | "season" | null; Sonarr v4+ only
}
