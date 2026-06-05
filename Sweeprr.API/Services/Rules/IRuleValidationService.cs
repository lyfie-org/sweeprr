using Sweeprr.API.Dtos.Rules;
using Sweeprr.API.Models;

namespace Sweeprr.API.Services.Rules;

public sealed record RuleValidationError(string Path, string Message);

public sealed record RuleValidationResult(
    bool IsValid,
    IReadOnlyList<RuleValidationError> Errors)
{
    public static RuleValidationResult Ok()
        => new(true, []);

    public static RuleValidationResult Fail(IReadOnlyList<RuleValidationError> errors)
        => new(false, errors);
}

public interface IRuleValidationService
{
    RuleValidationResult Validate(MediaType groupMediaType, IReadOnlyList<RuleConditionDto> conditions);
}
