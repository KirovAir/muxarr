using Muxarr.Core.Models;
using Muxarr.Data.Entities;

namespace Muxarr.Data.Extensions;

// Reduces a fully-populated desired TargetSnapshot to a delta by nulling out
// fields that already match the source. Converters apply non-null fields only.
// Name semantics:
//   null                = inherit source
//   ""                  = explicit clear (emitted if source not already empty)
//   non-empty           = emitted if differs
public static class TargetDiff
{
    public static TargetSnapshot Delta(MediaSnapshot source, TargetSnapshot desired)
    {
        var sourceByNumber = source.Tracks.ToDictionary(t => t.TrackNumber);
        var result = new TargetSnapshot
        {
            HasChapters = DiffBool(source.HasChapters, desired.HasChapters),
            HasAttachments = DiffBool(source.HasAttachments, desired.HasAttachments),
            Faststart = desired.Faststart,
            Tracks = new List<TargetTrack>(desired.Tracks.Count)
        };

        foreach (var track in desired.Tracks)
        {
            sourceByNumber.TryGetValue(track.TrackNumber, out var original);
            result.Tracks.Add(Delta(original, track));
        }

        return result;
    }

    public static TargetTrack Delta(TrackSnapshot? source, TargetTrack desired)
    {
        return new TargetTrack
        {
            TrackNumber = desired.TrackNumber,
            Type = desired.Type,
            NameLocked = desired.NameLocked,

            Name = DiffString(source?.TrackName, desired.Name),
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

    public static bool HasChanges(TargetTrack delta)
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

    public static bool HasChanges(TargetSnapshot delta)
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
