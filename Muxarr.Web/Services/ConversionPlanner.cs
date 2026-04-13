using Muxarr.Core.Extensions;
using Muxarr.Core.Models;
using Muxarr.Data.Entities;
using Muxarr.Data.Extensions;

namespace Muxarr.Web.Services;

public static class ConversionPlanner
{
    public enum ConversionStrategy { Skip, MetadataEdit, Remux }

    /// <summary>Strategy + cached outputs, produced in one pass to avoid drift between calls.</summary>
    public sealed record ConversionPlan(
        ConversionStrategy Strategy,
        List<TrackOutput> DiffOutputs,
        List<TrackOutput> RemuxOutputs);

    public static ConversionPlan Plan(MediaFile file, MediaSnapshot before, MediaSnapshot target)
    {
        var family = file.ContainerType.ToContainerFamily();
        var hasTrackRemoval = target.Tracks.Count < file.TrackCount;
        var hasOrderChanges = !target.Tracks.Select(t => t.TrackNumber)
            .SequenceEqual(before.Tracks.Select(t => t.TrackNumber));

        var diffOutputs = BuildTrackOutputs(before, target, family);
        var hasMetadataChanges = diffOutputs.Any(HasChanges);

        ConversionStrategy strategy;
        if (hasTrackRemoval || hasOrderChanges)
        {
            strategy = ConversionStrategy.Remux;
        }
        else if (!hasMetadataChanges)
        {
            strategy = ConversionStrategy.Skip;
        }
        else
        {
            strategy = family == ContainerFamily.Matroska
                ? ConversionStrategy.MetadataEdit
                : ConversionStrategy.Remux;
        }

        var remuxOutputs = strategy == ConversionStrategy.Remux
            ? BuildTrackOutputs(before, target, family, diffOnly: false)
            : [];

        return new ConversionPlan(strategy, diffOutputs, remuxOutputs);
    }

    public static ConversionStrategy DetermineStrategy(
        MediaFile file, MediaSnapshot before, MediaSnapshot target)
    {
        return Plan(file, before, target).Strategy;
    }

    /// <summary>
    /// Builds tool instructions by diffing target tracks against current tracks.
    /// When diffOnly is true (metadata edits), only changed properties are set.
    /// When false (remux), all non-video track properties are set explicitly.
    /// IsDub is dropped for Matroska since the spec has no FlagDub element -
    /// callers that want to persist a dub state on Matroska must encode it in
    /// the track name themselves (see TrackNameFlags.EncodeDubInName).
    /// </summary>
    public static List<TrackOutput> BuildTrackOutputs(MediaSnapshot before, MediaSnapshot target,
        ContainerFamily family, bool diffOnly = true)
    {
        var beforeByNumber = before.Tracks.ToDictionary(t => t.TrackNumber);
        var supportsDubFlag = family != ContainerFamily.Matroska;
        var trackOutputs = new List<TrackOutput>();

        foreach (var track in target.Tracks)
        {
            beforeByNumber.TryGetValue(track.TrackNumber, out var original);

            var output = new TrackOutput
            {
                TrackNumber = track.TrackNumber,
                Type = track.Type.ToMkvMergeType()
            };

            if (track.Type == MediaTrackType.Video)
            {
                if (!string.Equals(track.TrackName ?? "", original?.TrackName ?? "", StringComparison.Ordinal))
                {
                    output.Name = track.TrackName ?? "";
                }
            }
            else if (diffOnly)
            {
                if (!string.Equals(track.TrackName ?? "", original?.TrackName ?? "", StringComparison.Ordinal))
                {
                    output.Name = track.TrackName;
                }

                var resolvedLanguage = track.ResolveLanguageCode();
                if (resolvedLanguage != null &&
                    !string.Equals(resolvedLanguage, original?.LanguageCode, StringComparison.Ordinal))
                {
                    output.LanguageCode = resolvedLanguage;
                }

                if (original == null || track.IsDefault != original.IsDefault)
                {
                    output.IsDefault = track.IsDefault;
                }
                if (original == null || track.IsForced != original.IsForced)
                {
                    output.IsForced = track.IsForced;
                }
                if (original == null || track.IsHearingImpaired != original.IsHearingImpaired)
                {
                    output.IsHearingImpaired = track.IsHearingImpaired;
                }
                if (original == null || track.IsVisualImpaired != original.IsVisualImpaired)
                {
                    output.IsVisualImpaired = track.IsVisualImpaired;
                }
                if (original == null || track.IsCommentary != original.IsCommentary)
                {
                    output.IsCommentary = track.IsCommentary;
                }
                if (original == null || track.IsOriginal != original.IsOriginal)
                {
                    output.IsOriginal = track.IsOriginal;
                }
                if (supportsDubFlag && (original == null || track.IsDub != original.IsDub))
                {
                    output.IsDub = track.IsDub;
                }
            }
            else
            {
                output.Name = track.TrackName;
                output.LanguageCode = track.ResolveLanguageCode();
                output.IsDefault = track.IsDefault;
                output.IsForced = track.IsForced;
                output.IsHearingImpaired = track.IsHearingImpaired;
                output.IsVisualImpaired = track.IsVisualImpaired;
                output.IsCommentary = track.IsCommentary;
                output.IsOriginal = track.IsOriginal;
                if (supportsDubFlag)
                {
                    output.IsDub = track.IsDub;
                }
            }

            trackOutputs.Add(output);
        }

        return trackOutputs;
    }

    /// <summary>
    /// Returns true when a TrackOutput has any property set (non-null = a change).
    /// </summary>
    public static bool HasChanges(TrackOutput output)
    {
        return output.Name != null || output.LanguageCode != null
            || output.IsDefault != null || output.IsForced != null
            || output.IsHearingImpaired != null || output.IsVisualImpaired != null
            || output.IsCommentary != null || output.IsOriginal != null
            || output.IsDub != null;
    }

}
