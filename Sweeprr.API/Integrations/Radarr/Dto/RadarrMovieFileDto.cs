namespace Sweeprr.API.Integrations.Radarr.Dto;

internal sealed class RadarrMovieFileDto
{
    public int     Id           { get; set; }
    public int     MovieId      { get; set; }
    public string  RelativePath { get; set; } = string.Empty;
    public string? Path         { get; set; }
    public long?   Size         { get; set; }
    public string? DateAdded    { get; set; }
    public string? ReleaseGroup { get; set; }
}
