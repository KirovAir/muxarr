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
    /// Audio: Lossless+Spatial > Lossless > Lossy+Spatial > Lossy, then by channel count.
    /// </summary>
    private static int ScoreAudioTrack(IMediaTrack track)
    {
        var codec = Enum.TryParse<AudioCodec>(track.Codec, out var parsed)
            ? parsed
            : AudioCodecExtensions.ParseAudioCodec(track.Codec);

        var isLossless = LosslessAudioCodecs.Contains(codec);
        var isSpatial = IsSpatialAudio(track);
        return (isLossless ? 1000 : 0) + (isSpatial ? 500 : 0) + (track.AudioChannels * 10);
    }

    /// <summary>
    /// Subtitles: Text-based (SRT, ASS) preferred over bitmap (PGS, VobSub).
    /// </summary>
    private static int ScoreSubtitleTrack(IMediaTrack track)
    {
        var codec = Enum.TryParse<SubtitleCodec>(track.Codec, out var parsed)
            ? parsed
            : SubtitleCodecExtensions.ParseSubtitleCodec(track.Codec);

        return TextSubtitleCodecs.Contains(codec) ? 1000 : 0;
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
