using Sweeprr.API.Integrations.Jellyfin.Dto;

namespace Sweeprr.API.Integrations.Jellyfin.Models;

public sealed record JellyfinUser(string Id, string Name)
{
    public static JellyfinUser From(JellyfinUserDto dto) => new(dto.Id, dto.Name);
}
