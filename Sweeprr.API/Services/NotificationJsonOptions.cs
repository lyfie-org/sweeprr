using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sweeprr.API.Services;

/// <summary>Shared JSON options for outbound notification payloads — camelCase to match the PRD's webhook examples.</summary>
internal static class NotificationJsonOptions
{
    public static readonly JsonSerializerOptions Default = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
