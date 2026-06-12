using Sweeprr.API.Models;

namespace Sweeprr.API.Services.Rules;

/// <summary>
/// Reads field values from a pre-populated <see cref="MediaContext"/>.
///
/// Null field + <see cref="MediaContext.HasTransientFailure"/> → <see cref="ResolvedValue.Transient"/>.
/// Null field without a transient flag → <see cref="ResolvedValue.Missing"/> (definitively absent).
/// </summary>
public sealed class ValueResolver : IValueResolver
{
    public ResolvedValue Resolve(RuleField field, MediaContext context)
    {
        return field switch
        {
            RuleField.LastWatched       => Lift(context.LastWatched,       context),
            RuleField.PlayCount         => Lift(context.PlayCount,         context),
            RuleField.WatchedByAnyUser  => Lift(context.WatchedByAnyUser,  context),
            RuleField.WatchedByAllUsers => Lift(context.WatchedByAllUsers, context),
            RuleField.SeenByUserCount   => Lift(context.SeenByUserCount,   context),
            RuleField.ReleaseDate       => Lift(context.ReleaseDate,       context),
            RuleField.DateAdded         => Lift(context.DateAdded,         context),
            RuleField.Rating            => Lift(context.Rating,            context),
            RuleField.Genre             => LiftList(context.Genres,        context),
            RuleField.ResolutionHeight  => Lift(context.ResolutionHeight,  context),
            RuleField.VideoCodec        => LiftString(context.VideoCodec,  context),
            RuleField.AudioChannels     => Lift(context.AudioChannels,     context),
            RuleField.Monitored         => Lift(context.Monitored,         context),
            RuleField.Tags              => LiftList(context.Tags,          context),
            RuleField.QualityProfile    => LiftString(context.QualityProfile, context),
            RuleField.FileSizeGb        => Lift(context.FileSizeGb,        context),
            RuleField.SeriesEnded          => Lift(context.SeriesEnded,          context),
            RuleField.IsFinale             => Lift(context.IsFinale,             context),
            RuleField.CutoffMet            => Lift(context.CutoffMet,            context),
            RuleField.HasComplementaryCopy => Lift(context.HasComplementaryCopy, context),
            RuleField.DiskFreeSpacePercent => Lift(context.DiskFreeSpacePercent, context),
            RuleField.DiskFreeSpaceGb      => Lift(context.DiskFreeSpaceGb,      context),
            _                              => new ResolvedValue.Missing()
        };
    }

    private static ResolvedValue Lift<T>(T? value, MediaContext context) where T : struct
    {
        if (value.HasValue)
            return new ResolvedValue.Success(value.Value);

        return context.HasTransientFailure
            ? new ResolvedValue.Transient(context.TransientFailureReason ?? "Transient data source failure")
            : new ResolvedValue.Missing();
    }

    private static ResolvedValue LiftString(string? value, MediaContext context)
    {
        if (!string.IsNullOrEmpty(value))
            return new ResolvedValue.Success(value);

        return context.HasTransientFailure
            ? new ResolvedValue.Transient(context.TransientFailureReason ?? "Transient data source failure")
            : new ResolvedValue.Missing();
    }

    private static ResolvedValue LiftList(IReadOnlyList<string>? value, MediaContext context)
    {
        if (value is not null)
            return new ResolvedValue.Success(value);

        return context.HasTransientFailure
            ? new ResolvedValue.Transient(context.TransientFailureReason ?? "Transient data source failure")
            : new ResolvedValue.Missing();
    }
}
