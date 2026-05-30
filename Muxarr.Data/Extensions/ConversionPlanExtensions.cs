using Muxarr.Core.Extensions;
using Muxarr.Core.Models;
using Muxarr.Data.Entities;

namespace Muxarr.Data.Extensions;

// Reduces a fully-populated desired ConversionPlan to a delta by nulling out
// fields that already match the source. Converters apply non-null fields only.
// Name semantics:
//   null                = inherit source
//   ""                  = explicit clear (emitted if source not already empty)
//   non-empty           = emitted if differs
public static class ConversionPlanExtensions
{
    public static ConversionPlan Delta(MediaSnapshot source, ConversionPlan desired)
    {
        var sourceByNumber = source.Tracks.ToDictionary(t => t.Index);
        var result = new ConversionPlan
        {
            HasChapters = DiffBool(source.HasChapters, desired.HasChapters),
            HasAttachments = DiffBool(source.HasAttachments, desired.HasAttachments),
            Faststart = desired.Faststart,
            Tracks = new List<TrackPlan>(desired.Tracks.Count)
        };

        foreach (var track in desired.Tracks)
        {
            sourceByNumber.TryGetValue(track.Index, out var original);
            result.Tracks.Add(Delta(original, track));
        }

        return result;
    }

    private static TrackPlan Delta(TrackSnapshot? source, TrackPlan desired)
    {
        return new TrackPlan
        {
            Index = desired.Index,
            Type = desired.Type,
            NameLocked = desired.NameLocked,
            SourcePath = desired.SourcePath,

            Name = DiffString(source?.Name, desired.Name),
            LanguageCode = DiffString(source?.LanguageCode, desired.LanguageCode),
            IsDefault = DiffBool(source?.IsDefault, desired.IsDefault),
            IsForced = DiffBool(source?.IsForced, desired.IsForced),
            IsHearingImpaired = DiffBool(source?.IsHearingImpaired, desired.IsHearingImpaired),
            IsVisualImpaired = DiffBool(source?.IsVisualImpaired, desired.IsVisualImpaired),
            IsCommentary = DiffBool(source?.IsCommentary, desired.IsCommentary),
            IsOriginal = DiffBool(source?.IsOriginal, desired.IsOriginal),
            IsDub = DiffBool(source?.IsDub, desired.IsDub)
        };
    }

    public static TrackPlan ToTargetTrack(this TrackSnapshot t, bool nameLocked)
    {
        var plan = EntityCompare.CopyTo<TrackPlan>(t);
        plan.NameLocked = nameLocked;
        return plan;
    }

    public static bool HasChanges(TrackPlan delta)
    {
        return delta.Name != null
               || delta.LanguageCode != null
               || delta.IsDefault != null
               || delta.IsForced != null
               || delta.IsHearingImpaired != null
               || delta.IsVisualImpaired != null
               || delta.IsCommentary != null
               || delta.IsOriginal != null
               || delta.IsDub != null;
    }

    public static bool HasChanges(ConversionPlan delta)
    {
        return delta.HasChapters != null
               || delta.HasAttachments != null
               || delta.Faststart != null
               || delta.Tracks.Any(HasChanges);
    }

    private static string? DiffString(string? source, string? desired)
    {
        if (desired == null)
        {
            return null;
        }

        return string.Equals(source ?? "", desired, StringComparison.Ordinal) ? null : desired;
    }

    private static bool? DiffBool(bool? source, bool? desired)
    {
        if (desired == null)
        {
            return null;
        }

        if (source == null)
        {
            return desired;
        }

        return source.Value == desired.Value ? null : desired;
    }
}
