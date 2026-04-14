using Muxarr.Core.Language;
using Muxarr.Core.Models;
using Muxarr.Core.MkvToolNix;
using Muxarr.Data.Entities;
using Muxarr.Data.Extensions;

namespace Muxarr.Tests;

/// <summary>
/// Test-only bridge between ConversionPlan (nullable delta) and MediaSnapshot
/// (fully-populated display shape). Production code reads ConversionPlan +
/// MediaFile directly; these helpers exist so existing test assertions keep
/// working without rewriting 30+ call sites.
/// </summary>
internal static class DisplayMergeExtensions
{
    public static MediaSnapshot BuildTargetSnapshot(this MediaFile file, Profile? profile)
    {
        var target = file.BuildTargetFromProfile(profile);
        return target.MergeForDisplay(file);
    }

    public static MediaSnapshot MergeForDisplay(this ConversionPlan target, MediaFile file)
    {
        var src = file.Snapshot.Tracks.ToDictionary(t => t.Index);
        return new MediaSnapshot
        {
            Tracks = target.Tracks.Select(t => t.ToDisplay(src.GetValueOrDefault(t.Index))).ToList(),
            HasChapters = target.HasChapters ?? file.Snapshot.HasChapters,
            HasAttachments = target.HasAttachments ?? file.Snapshot.HasAttachments
        };
    }

    public static TrackSnapshot ToDisplay(this TrackPlan t, IMediaTrack? src)
    {
        var snap = src?.ToSnapshot() ?? new TrackSnapshot { Index = t.Index, Type = t.Type };

        if (t.Name != null)
        {
            snap.Name = string.IsNullOrEmpty(t.Name) ? null : t.Name;
        }

        if (t.LanguageCode != null)
        {
            snap.LanguageCode = t.LanguageCode;
            snap.LanguageName = IsoLanguage.Find(t.LanguageCode).Name;
        }

        if (t.IsDefault != null)
        {
            snap.IsDefault = t.IsDefault.Value;
        }

        if (t.IsForced != null)
        {
            snap.IsForced = t.IsForced.Value;
        }

        if (t.IsHearingImpaired != null)
        {
            snap.IsHearingImpaired = t.IsHearingImpaired.Value;
        }

        if (t.IsVisualImpaired != null)
        {
            snap.IsVisualImpaired = t.IsVisualImpaired.Value;
        }

        if (t.IsCommentary != null)
        {
            snap.IsCommentary = t.IsCommentary.Value;
        }

        if (t.IsOriginal != null)
        {
            snap.IsOriginal = t.IsOriginal.Value;
        }

        snap.IsDub = t.IsDub ?? TrackNameFlags.ContainsDub(snap.Name);

        return snap;
    }
}
