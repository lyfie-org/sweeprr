namespace Sweeprr.API.Integrations.Radarr.Dto;

internal sealed class RadarrMovieDto
{
    public int                Id               { get; set; }
    public string             Title            { get; set; } = string.Empty;
    public int                Year             { get; set; }
    public int                TmdbId           { get; set; }
    public string?            ImdbId           { get; set; }
    public bool               Monitored        { get; set; }
    public bool               HasFile          { get; set; }
    public int                QualityProfileId { get; set; }
    public List<int>          Tags             { get; set; } = [];
    public string?            Path             { get; set; }
    public long               SizeOnDisk       { get; set; }
    public string?            Added            { get; set; }
    public string?            Status           { get; set; }
    public RadarrMovieFileDto? MovieFile       { get; set; }
}
