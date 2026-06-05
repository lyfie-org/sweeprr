using Sweeprr.API.Models;

namespace Sweeprr.API.Services.Rules;

/// <summary>
/// Extracts the value for a given <see cref="RuleField"/> from a populated <see cref="MediaContext"/>.
/// </summary>
public interface IValueResolver
{
    ResolvedValue Resolve(RuleField field, MediaContext context);
}
