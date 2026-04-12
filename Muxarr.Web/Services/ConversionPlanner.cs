using Muxarr.Core.Extensions;
using Muxarr.Core.Models;
using Muxarr.Data.Entities;
using Muxarr.Data.Extensions;

namespace Muxarr.Web.Services;

public static class ConversionPlanner
{
    public enum ConversionStrategy { Skip, MetadataEdit, Remux }

    public static ConversionStrategy DetermineStrategy(
        MediaFile file, MediaSnapshot before, MediaSnapshot target)
    {
        var hasTrackRemoval = target.Tracks.Count < file.TrackCount;
        var hasOrderChanges = !target.Tracks.Select(t => t.TrackNumber)
            .SequenceEqual(before.Tracks.Select(t => t.TrackNumber));

        if (hasTrackRemoval || hasOrderChanges)
        {
            return ConversionStrategy.Remux;
        }

        var trackOutputs = BuildTrackOutputs(before, target);
        var hasMetadataChanges = trackOutputs.Any(o => HasChanges(o));

        if (!hasMetadataChanges)
        {
            return ConversionStrategy.Skip;
        }

        return file.ContainerType.ToContainerFamily() == ContainerFamily.Matroska
            ? ConversionStrategy.MetadataEdit
            : ConversionStrategy.Remux;
    }

    /// <summary>
    /// Builds tool instructions by diffing target tracks against current tracks.
    /// When diffOnly is true (metadata edits), only changed properties are set.
    /// When false (remux), all non-video track properties are set explicitly
    /// so the output file has correct metadata regardless of tool defaults.
    /// </summary>
    public static List<TrackOutput> BuildTrackOutputs(MediaSnapshot before, MediaSnapshot target, bool diffOnly = true)
    {
        var beforeByNumber = before.Tracks.ToDictionary(t => t.TrackNumber);
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
                if (original == null || track.IsCommentary != original.IsCommentary)
                {
                    output.IsCommentary = track.IsCommentary;
                }
                if (original == null || track.IsDub != original.IsDub)
                {
                    output.IsDub = track.IsDub;
                }
            }
            else
            {
                // Remux: set all properties explicitly.
                output.Name = track.TrackName;
                output.LanguageCode = track.ResolveLanguageCode();
                output.IsDefault = track.IsDefault;
                output.IsForced = track.IsForced;
                output.IsHearingImpaired = track.IsHearingImpaired;
                output.IsCommentary = track.IsCommentary;
                output.IsDub = track.IsDub;
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
            || output.IsHearingImpaired != null || output.IsCommentary != null
            || output.IsDub != null;
    }

}
