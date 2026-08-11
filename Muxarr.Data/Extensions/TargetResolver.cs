using Muxarr.Core.Extensions;
using Muxarr.Core.Language;
using Muxarr.Core.MkvToolNix;
using Muxarr.Core.Models;
using Muxarr.Data.Entities;

namespace Muxarr.Data.Extensions;

// Container-specific resolution of a desired target. Run by the builders so
// the ConversionPlan they hand off is already valid for the output container.
// Matroska has no FlagDub, so when IsDub is set on an unlocked target track
// we rewrite its title to encode the dub state and null IsDub out.
public static class TargetResolver
{
    public static void ResolveForContainer(ConversionPlan target, MediaSnapshot source)
    {
        var family = source.ContainerType.ToContainerFamily();
        var sourceByNumber = source.Tracks.ToDictionary(t => t.Index);

        if (family == ContainerFamily.Mp4)
        {
            target.Faststart ??= source.HasFaststart;
        }
        else
        {
            target.Faststart = null;
        }

        if (family != ContainerFamily.Matroska)
        {
            // mkvmerge stops after the video itself; ffmpeg needs a -t cut only mp4 can supply.
            target.TrimToVideoLength = target.TrimToVideoLength && family == ContainerFamily.Mp4;

            foreach (var track in target.Tracks)
            {
                // The mov muxer drops +original on stream-copy, so asking for it
                // would re-flag the file as non-standard on every scan.
                track.IsOriginal = null;

                // MP4's track language is a packed ISO 639-2 code; a BCP 47
                // region tag would be mangled to und on write. Ask for the base
                // and move the variant into the track name, the same way the
                // title carries the dub flag on Matroska. Variants with a real
                // ISO 639 code (cmn, yue) pack fine as-is.
                if (track.LanguageCode == null)
                {
                    continue;
                }

                var lang = IsoLanguage.Find(track.LanguageCode);
                if (lang.IetfTag == null || lang.Base.ThreeLetterCode is not { } baseCode)
                {
                    continue;
                }

                if (!track.NameLocked)
                {
                    Rename(track, sourceByNumber, name => LanguageVariants.EncodeInName(name, lang));
                }

                track.LanguageCode = baseCode;
            }

            return;
        }

        foreach (var track in target.Tracks)
        {
            if (track.IsDub == null)
            {
                continue;
            }

            if (!track.NameLocked)
            {
                Rename(track, sourceByNumber, name => TrackNameFlags.EncodeDubInName(name, track.IsDub.Value));
            }

            track.IsDub = null;
        }
    }

    // Rewrites a target's name off whichever name applies, the plan's own or the
    // source's. "" rather than null when the encoder empties it: that is an
    // explicit clear, where null would read as "no opinion".
    private static void Rename(TrackPlan track, Dictionary<int, TrackSnapshot> sourceByNumber,
        Func<string?, string?> encode)
    {
        sourceByNumber.TryGetValue(track.Index, out var original);
        var effectiveName = track.Name ?? original?.Name;
        var encoded = encode(effectiveName);
        if (!string.Equals(encoded ?? "", effectiveName ?? "", StringComparison.Ordinal))
        {
            track.Name = encoded ?? "";
        }
    }
}
