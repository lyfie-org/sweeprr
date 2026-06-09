using System.Text.Json.Serialization;

namespace Sweeprr.API.Integrations.Bazarr.Dto;

public sealed record BazarrSystemStatusDto(
    [property: JsonPropertyName("bazarr_version")] string? BazarrVersion,
    string? DataDir);
