using System;
using System.Collections.Generic;
using Sweeprr.API.Models;

namespace Sweeprr.API.Dtos.Sweep;

public sealed record SweepItemResponse(
    int Id,
    int RuleGroupId,
    string RuleGroupName,
    string MediaServerItemId,
    string Title,
    MediaType MediaType,
    long? SizeBytes,
    string? MatchedRuleSummary,
    SweepItemStatus Status,
    int? ArrInstanceId,
    string? TmdbId,
    string? TvdbId,
    string? ImdbId,
    DateTime FlaggedAt,
    DateTime? SweptAt,
    string? SkippedReason,
    IReadOnlyList<string>? Genres,
    int? ResolutionHeight,
    string? VideoCodec,
    int? AudioChannels);
