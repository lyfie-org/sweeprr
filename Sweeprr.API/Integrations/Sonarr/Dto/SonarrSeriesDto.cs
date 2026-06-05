namespace Sweeprr.API.Integrations.Sonarr.Dto;

internal sealed class SonarrSeriesDto
{
    public int                  Id               { get; set; }
    public string               Title            { get; set; } = string.Empty;
    public int                  Year             { get; set; }
    public int                  TvdbId           { get; set; }
    public string?              ImdbId           { get; set; }
    public bool                 Monitored        { get; set; }
    public int                  QualityProfileId { get; set; }
    public List<int>            Tags             { get; set; } = [];
    public string?              Path             { get; set; }
    public string?              Added            { get; set; }
    public List<SonarrSeasonDto> Seasons         { get; set; } = [];
}
