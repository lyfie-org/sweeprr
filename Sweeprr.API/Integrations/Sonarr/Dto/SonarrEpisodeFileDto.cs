namespace Sweeprr.API.Integrations.Sonarr.Dto;

internal sealed class SonarrEpisodeFileDto
{
    public int     Id           { get; set; }
    public int     SeriesId     { get; set; }
    public int     SeasonNumber { get; set; }
    public string  RelativePath { get; set; } = string.Empty;
    public string? Path         { get; set; }
    public long?   Size         { get; set; }
    public string? DateAdded              { get; set; }
    public string? ReleaseGroup           { get; set; }
    public bool?   QualityCutoffNotMet   { get; set; }
}
