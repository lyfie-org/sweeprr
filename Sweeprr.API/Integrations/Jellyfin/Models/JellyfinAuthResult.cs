using Sweeprr.API.Integrations.Jellyfin.Dto;

namespace Sweeprr.API.Integrations.Jellyfin.Models;

public sealed record JellyfinAuthResult(JellyfinUser User, string AccessToken)
{
    public static JellyfinAuthResult From(JellyfinAuthenticateResultDto dto) =>
        new(JellyfinUser.From(dto.User), dto.AccessToken);
}
