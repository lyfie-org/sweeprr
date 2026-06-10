using System.ComponentModel.DataAnnotations;

namespace Sweeprr.API.Dtos.Media;

public sealed class QueueManualRequest
{
    [Required, MinLength(1, ErrorMessage = "At least one item ID is required.")]
    public IReadOnlyList<string> Ids { get; init; } = [];
}
