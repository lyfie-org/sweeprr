namespace Sweeprr.API.Dtos.Rules;

using System.Collections.Generic;

public sealed record GenresResponse(IReadOnlyList<string> Genres);
