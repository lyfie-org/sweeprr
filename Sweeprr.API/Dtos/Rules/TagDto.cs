namespace Sweeprr.API.Dtos.Rules;

/// <summary>Response shape for a single *arr tag surfaced by the Rule Builder UI.</summary>
public sealed record TagDto(int Id, string Label);

public sealed record TagsResponse(IReadOnlyList<TagDto> Tags);
