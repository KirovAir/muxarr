using Muxarr.Core.Models;
using Muxarr.Core.Extensions;
using Muxarr.Data.Entities;

namespace Muxarr.Data.Extensions;

/// <summary>
/// Scores tracks by quality for deduplication (MaxTracks per language).
/// Higher score = higher quality. Use AudioQualityStrategy.SmallestSize to invert.
/// </summary>
public static class TrackQualityScorer
{
    private static readonly HashSet<AudioCodec> LosslessAudioCodecs =
        [AudioCodec.TrueHd, AudioCodec.DtsHdMa, AudioCodec.Flac, AudioCodec.Pcm];

    private static readonly HashSet<SubtitleCodec> TextSubtitleCodecs =
        [SubtitleCodec.Srt, SubtitleCodec.Ass, SubtitleCodec.WebVtt, SubtitleCodec.TimedText, SubtitleCodec.MovText];

    private static readonly string[] SpatialKeywords =
        ["Atmos", "DTS:X", "IMAX", "Spatial"];

    /// <summary>
    /// Returns a quality score for the track. Higher = better quality.
    /// For SmallestSize strategy, the score is negated so lower quality sorts first.
    /// </summary>
    public static int ScoreTrack(IMediaTrack track, AudioQualityStrategy strategy = AudioQualityStrategy.BestQuality)
    {
        var raw = track.Type == MediaTrackType.Audio
            ? ScoreAudioTrack(track)
            : ScoreSubtitleTrack(track);

        return strategy == AudioQualityStrategy.SmallestSize ? -raw : raw;
    }

    /// <summary>
    /// Audio: Lossless+Spatial > Lossless > Lossy+Spatial > Lossy, then channels, then Regular > Dub/AD > Commentary.
    /// </summary>
    private static int ScoreAudioTrack(IMediaTrack track)
    {
        var codec = Enum.TryParse<AudioCodec>(track.Codec, out var parsed)
            ? parsed
            : AudioCodecExtensions.ParseAudioCodec(track.Codec);

        var isLossless = LosslessAudioCodecs.Contains(codec);
        var isSpatial = IsSpatialAudio(track);
        var flagScore = track.IsCommentary ? 0 : track.IsVisualImpaired ? 1 : track.IsDub ? 1 : 2;

        return (isLossless ? 1000 : 0) + (isSpatial ? 500 : 0) + track.AudioChannels * 10 + flagScore;
    }

    /// <summary>
    /// Subtitles: Text > Bitmap (codec), then Regular > SDH > Forced > Commentary (flags).
    /// </summary>
    private static int ScoreSubtitleTrack(IMediaTrack track)
    {
        var codec = Enum.TryParse<SubtitleCodec>(track.Codec, out var parsed)
            ? parsed
            : SubtitleCodecExtensions.ParseSubtitleCodec(track.Codec);

        var codecScore = TextSubtitleCodecs.Contains(codec) ? 1000 : 0;

        // Flag tiebreaker: regular subs are the most universally useful.
        // SDH adds noise (sound descriptions), forced is partial (foreign dialogue only),
        // commentary is niche. Lower score = less preferred when deduplicating.
        var flagScore = track.IsCommentary ? 5
            : track.IsDub ? 8
            : track.IsForced ? 10
            : track.IsHearingImpaired ? 50
            : 100;

        return codecScore + flagScore;
    }

    private static bool IsSpatialAudio(IMediaTrack track)
    {
        if (string.IsNullOrEmpty(track.TrackName))
        {
            return false;
        }

        foreach (var keyword in SpatialKeywords)
        {
            if (track.TrackName.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
