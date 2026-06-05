using Sweeprr.API.Integrations.Jellyfin.Dto;

namespace Sweeprr.API.Integrations.Jellyfin.Models;

public sealed record JellyfinSystemInfo(
    string ServerId,
    string ServerName,
    string Version)
{
    public static JellyfinSystemInfo From(JellyfinSystemInfoDto dto)
        => new(dto.Id, dto.ServerName, dto.Version);
}
