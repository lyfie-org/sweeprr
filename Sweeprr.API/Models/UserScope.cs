namespace Sweeprr.API.Models;

public sealed record UserScope(
    UserScopeMode Mode,
    IReadOnlyList<string> UserIds)
{
    public static readonly UserScope Default = new(UserScopeMode.All, []);

    public bool IsUserQualifying(string userId)
    {
        return Mode switch
        {
            UserScopeMode.All => true,
            UserScopeMode.Whitelist => UserIds.Contains(userId, StringComparer.OrdinalIgnoreCase),
            UserScopeMode.Exclude => !UserIds.Contains(userId, StringComparer.OrdinalIgnoreCase),
            _ => true
        };
    }
}
