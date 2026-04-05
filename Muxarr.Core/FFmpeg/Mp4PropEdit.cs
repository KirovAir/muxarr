using System.Text;
using Muxarr.Core.MkvToolNix;
using Muxarr.Core.Utilities;

namespace Muxarr.Core.FFmpeg;

/// <summary>
/// MP4/QuickTime counterpart to <see cref="MkvPropEdit"/>. Writes track
/// metadata and disposition flags via an ffmpeg stream-copy pass so the MP4
/// container and every codec survive byte-identical. Notably preserves tx3g
/// subtitles, which mkvmerge would translate to SRT on the way through.
/// </summary>
public static class Mp4PropEdit
{
    /// <summary>
    /// Runs an ffmpeg stream-copy pass to apply <paramref name="tracks"/> to
    /// <paramref name="input"/>, writing the result to <paramref name="output"/>.
    /// Null fields on a <see cref="TrackOutput"/> mean "keep original" and are
    /// not emitted as options, matching <see cref="MkvPropEdit.EditTrackProperties"/>.
    /// </summary>
    public static async Task<ProcessResult> EditTrackProperties(
        string input,
        string output,
        List<TrackOutput> tracks,
        long durationMs = 0,
        Action<string, int, bool>? onOutput = null)
    {
        if (string.IsNullOrEmpty(input))
        {
            throw new ArgumentException("Input path is required.", nameof(input));
        }
        if (string.IsNullOrEmpty(output))
        {
            throw new ArgumentException("Output path is required.", nameof(output));
        }
        if (string.Equals(input, output, StringComparison.Ordinal))
        {
            throw new ArgumentException("Output path must differ from input path.", nameof(output));
        }

        return await FFmpeg.ExecuteAsync(BuildArguments(input, output, tracks), durationMs, onOutput);
    }

    /// <summary>
    /// Probes the source with ffprobe and verifies that ffmpeg's stream
    /// indices line up with mkvmerge's track IDs. Returns false when the
    /// layout diverges (e.g. a data or timecode track mkvmerge skipped) so
    /// the caller can fall back to a full remux instead of editing the wrong
    /// stream by accident.
    /// </summary>
    public static async Task<bool> CanEditAsync(string file, List<TrackOutput> tracks)
    {
        var probe = await FFmpeg.GetStreamInfo(file);
        return probe.Result != null && VerifyStreamAlignment(probe.Result, tracks);
    }

    /// <summary>
    /// Builds the ffmpeg argument string for a stream-copy metadata edit.
    /// Exposed for unit testing; production callers use <see cref="EditTrackProperties"/>.
    /// </summary>
    public static string BuildArguments(string input, string output, List<TrackOutput> tracks)
    {
        var sb = new StringBuilder();

        // -y overwrites stale temp files from an aborted prior run, -nostdin
        // keeps ffmpeg from reading the background service's stdin, -nostats
        // suppresses the per-second stderr progress line since we drive the UI
        // via -progress pipe:1 instead.
        sb.Append("-hide_banner -nostdin -nostats -loglevel info -y");
        sb.Append(" -progress pipe:1");

        // Paths use plain quoting, not FFmpegHelper.EscapeValue. Windows argv
        // parsing only treats backslashes as escapes before a double quote, so
        // C:\Users\file.mp4 must appear verbatim; only user-supplied metadata
        // values go through EscapeValue.
        sb.Append($" -i \"{input}\"");

        // -map 0 -c copy keeps every input stream as-is (no transcoding, no
        // drops). -map_metadata 0 carries over global tags; +use_metadata_tags
        // allows arbitrary per-track keys in the moov atom.
        sb.Append(" -map 0 -c copy");
        sb.Append(" -map_metadata 0 -movflags +use_metadata_tags");

        foreach (var track in tracks)
        {
            var idx = track.TrackNumber;

            if (track.Name != null)
            {
                // Empty string clears the title, non-empty sets it.
                sb.Append($" -metadata:s:{idx} title={FFmpegHelper.EscapeValue(track.Name)}");
            }

            if (track.LanguageCode != null)
            {
                sb.Append($" -metadata:s:{idx} language={track.LanguageCode}");
            }

            var disposition = FFmpegHelper.BuildDispositionValue(track);
            if (disposition != null)
            {
                // NOTE: -disposition uses ffmpeg's general stream specifier
                // syntax, where "s:N" means "subtitle stream N (relative)",
                // not "stream index N". The bare absolute index is what we
                // actually want here. This is different from -metadata:s:N
                // which uses metadata-specifier syntax where s:N really does
                // mean "stream index N". Yes, this is as confusing as it sounds.
                sb.Append($" -disposition:{idx} {disposition}");
            }
        }

        // -f mp4 is required because the temp file is named .muxtmp so ffmpeg
        // can't infer the format from the extension.
        sb.Append($" -f mp4 \"{output}\"");

        return sb.ToString();
    }

    /// <summary>
    /// Verifies that ffmpeg's stream indices match mkvmerge's track IDs for
    /// the given <see cref="TrackOutput"/>s. Exposed for testing; production
    /// callers use <see cref="CanEditAsync"/>.
    /// </summary>
    public static bool VerifyStreamAlignment(FFprobeResult probe, List<TrackOutput> tracks)
    {
        foreach (var track in tracks)
        {
            var stream = probe.Streams.FirstOrDefault(s => s.Index == track.TrackNumber);
            if (stream == null || !IsCompatibleType(stream.CodecType, track.Type))
            {
                return false;
            }
        }

        return true;
    }

    // ffprobe uses singular codec_type names ("subtitle"); mkvmerge uses the plural.
    private static bool IsCompatibleType(string? ffprobeCodecType, string mkvmergeType)
    {
        if (string.IsNullOrEmpty(ffprobeCodecType))
        {
            return false;
        }

        return (ffprobeCodecType, mkvmergeType) switch
        {
            ("video", MkvMerge.VideoTrack) => true,
            ("audio", MkvMerge.AudioTrack) => true,
            ("subtitle", MkvMerge.SubtitlesTrack) => true,
            _ => false
        };
    }
}
