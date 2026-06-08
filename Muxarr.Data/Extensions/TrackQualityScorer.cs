using Muxarr.Core.Models;
using Muxarr.Core.Extensions;
using Muxarr.Data.Entities;

namespace Muxarr.Data.Extensions;

/// <summary>
/// Scores tracks by quality for deduplication (MaxTracks per language).
/// Higher score = kept. Each strategy decides which quality axis wins when
/// channel count, fidelity, and subtitle format disagree.
///
/// In every "best quality" strategy the content tier (main feature audio/subs
/// &gt; dub/AD &gt; commentary) is the top discriminator, so a niche track is
/// never kept over the real feature track just because it has more channels.
/// </summary>
public static class TrackQualityScorer
{
    private static readonly HashSet<AudioCodec> LosslessAudioCodecs =
        [AudioCodec.TrueHd, AudioCodec.DtsHdMa, AudioCodec.Flac, AudioCodec.Pcm];

    private static readonly HashSet<SubtitleCodec> TextSubtitleCodecs =
        [SubtitleCodec.Srt, SubtitleCodec.Ass, SubtitleCodec.WebVtt, SubtitleCodec.TimedText, SubtitleCodec.MovText];

    private static readonly string[] SpatialKeywords =
        ["Atmos", "DTS:X", "IMAX", "Spatial"];

    // Content tiers keep real feature content above niche tracks regardless of
    // codec or channel count. Promoted to the top discriminator so a 7.1
    // commentary never outranks a 2.0 main track.
    private const int MainContent = 2;
    private const int SecondaryContent = 1; // dub / audio description
    private const int CommentaryContent = 0;

    /// <summary>
    /// Scores an audio track for deduplication. Higher = kept. Content tier
    /// (main &gt; dub/AD &gt; commentary) always wins first; the strategy then
    /// decides whether channels or fidelity dominate.
    /// </summary>
    public static int ScoreAudio(IMediaTrack track, AudioQualityStrategy strategy = AudioQualityStrategy.BestQuality)
    {
        var codec = Enum.TryParse<AudioCodec>(track.Codec, out var parsed)
            ? parsed
            : AudioCodecExtensions.ParseAudioCodec(track.Codec);

        var isLossless = LosslessAudioCodecs.Contains(codec) ? 1 : 0;
        var isSpatial = IsSpatialAudio(track) ? 1 : 0;
        var channels = Math.Clamp(track.AudioChannels, 0, 15);
        var content = AudioContentTier(track);

        return strategy switch
        {
            // Surround beats stereo; spatial then lossless break ties within the
            // same channel count. Fixes a 2.0 lossless downmix outranking 5.1/7.1.
            AudioQualityStrategy.MostChannels =>
                content * 1_000_000 + channels * 10_000 + isSpatial * 100 + isLossless * 10,

            // Prefer lossy and the fewest channels, but still keep a usable main
            // track over commentary/dubs.
            AudioQualityStrategy.SmallestSize =>
                content * 1_000_000 + (15 - channels) * 10_000 + (1 - isLossless) * 100,

            // Highest fidelity (default): lossless + spatial dominate, channels
            // only break ties. Preserves the original ranking.
            _ =>
                content * 1_000_000 + isLossless * 100_000 + isSpatial * 10_000 + channels * 10
        };
    }

    /// <summary>
    /// Scores a subtitle track for deduplication. Higher = kept. The strategy
    /// decides whether text format, image format, or accessibility (SDH) wins.
    /// </summary>
    public static int ScoreSubtitle(IMediaTrack track, SubtitleQualityStrategy strategy = SubtitleQualityStrategy.TextFirst)
    {
        var codec = Enum.TryParse<SubtitleCodec>(track.Codec, out var parsed)
            ? parsed
            : SubtitleCodecExtensions.ParseSubtitleCodec(track.Codec);

        var isText = TextSubtitleCodecs.Contains(codec) ? 1 : 0;
        var typeTier = SubtitleTypeTier(track);
        var isSdh = track.IsHearingImpaired ? 1 : 0;

        return strategy switch
        {
            // Bitmap (PGS/VobSub) over text, then type (regular > SDH > forced).
            SubtitleQualityStrategy.ImageFirst =>
                (1 - isText) * 1_000 + typeTier * 10,

            // SDH/HI above everything, then text > image. Keeps the accessibility
            // track that would otherwise lose to a regular one.
            SubtitleQualityStrategy.Accessibility =>
                isSdh * 100_000 + isText * 1_000 + typeTier * 10,

            // Text over bitmap, then type. Preserves the original ranking.
            _ =>
                isText * 1_000 + typeTier * 10
        };
    }

    private static int AudioContentTier(IMediaTrack track)
    {
        if (track.IsCommentary)
        {
            return CommentaryContent;
        }

        if (track.IsDub || track.IsVisualImpaired)
        {
            return SecondaryContent;
        }

        return MainContent;
    }

    /// <summary>
    /// Subtitle type tiebreaker. Regular dialogue is the most universally useful;
    /// SDH adds sound descriptions, forced is partial (foreign dialogue only),
    /// dubs/commentary are niche. Higher = more preferred.
    /// </summary>
    private static int SubtitleTypeTier(IMediaTrack track)
    {
        if (track.IsCommentary)
        {
            return 0;
        }

        if (track.IsDub)
        {
            return 1;
        }

        if (track.IsForced)
        {
            return 2;
        }

        if (track.IsHearingImpaired)
        {
            return 3;
        }

        return 4; // regular
    }

    private static bool IsSpatialAudio(IMediaTrack track)
    {
        if (string.IsNullOrEmpty(track.Name))
        {
            return false;
        }

        foreach (var keyword in SpatialKeywords)
        {
            if (track.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
