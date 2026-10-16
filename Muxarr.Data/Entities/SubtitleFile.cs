using System.Text;
using System.Text.Json.Serialization;
using Muxarr.Core.Extensions;
using Muxarr.Core.Language;
using Muxarr.Core.Models;
using Muxarr.Core.Utilities;
using Muxarr.Data.Extensions;

namespace Muxarr.Data.Entities;

// A subtitle file beside the video. Only the suffix is stored, so a rename keeps it valid.
public class SubtitleFile : IMediaTrack
{
    // Above any container track index.
    public const int FirstIndex = 1000;

    public string Suffix { get; set; } = string.Empty;
    public string DiskName { get; set; } = string.Empty;
    public string LanguageCode { get; set; } = string.Empty;
    public string LanguageName { get; set; } = string.Empty;
    public bool IsForced { get; set; }
    public bool IsHearingImpaired { get; set; }
    public int Number { get; set; }
    public string Codec { get; set; } = string.Empty;
    public long Size { get; set; }
    public DateTime LastWriteTime { get; set; }

    // Why the file cannot go into a video, read once per file version: not UTF-8, or no cues.
    public bool Inspected { get; set; }
    public string? Problem { get; set; }

    [JsonIgnore]
    public int Index { get; set; }

    [JsonIgnore]
    public MediaTrackType Type => MediaTrackType.Subtitles;

    [JsonIgnore]
    public int AudioChannels => 0;

    [JsonIgnore]
    public long DurationMs => 0;

    [JsonIgnore]
    public bool IsDefault => false;

    [JsonIgnore]
    public bool IsCommentary => false;

    [JsonIgnore]
    public bool IsVisualImpaired => false;

    [JsonIgnore]
    public bool IsOriginal => false;

    [JsonIgnore]
    public bool IsDub => false;

    // Image files cannot go in; ffprobe reports a muxed WebVTT as unknown, which would mux it in again every time.
    [JsonIgnore]
    public bool CanMuxIn => (Codec is nameof(SubtitleCodec.Srt) or nameof(SubtitleCodec.Ass)) && Problem == null;

    [JsonIgnore]
    public string Identity => $"{IsoLanguage.Find(LanguageName).Base.Name}|{IsForced}|{IsHearingImpaired}|{SubtitleSidecars.ExtensionFor(GetCodec())}";

    string? IMediaTrack.Name => null;

    // The name as found when the video still has that base name, so case survives on Linux.
    public string FileName(string mediaPath)
    {
        var baseName = Path.GetFileNameWithoutExtension(mediaPath);
        return DiskName.StartsWith(baseName + ".", StringComparison.Ordinal) ? DiskName : baseName + Suffix;
    }

    public string FullPath(string mediaPath)
    {
        return Path.Combine(Path.GetDirectoryName(mediaPath) ?? string.Empty, FileName(mediaPath));
    }

    public SubtitleFileDeletion ToDeletion(string mediaPath)
    {
        return new SubtitleFileDeletion { FileName = FileName(mediaPath), Suffix = Suffix, Size = Size, LastWriteTime = LastWriteTime };
    }

    public static SubtitleFile From(SubtitleSidecar sidecar, string mediaPath)
    {
        var info = new FileInfo(sidecar.Path);
        return new SubtitleFile
        {
            Suffix = sidecar.FileName[Path.GetFileNameWithoutExtension(mediaPath).Length..],
            DiskName = sidecar.FileName,
            LanguageCode = sidecar.Language.WriteCode ?? sidecar.Language.TwoLetterCode,
            LanguageName = sidecar.Language.Name,
            IsForced = sidecar.Forced,
            IsHearingImpaired = sidecar.HearingImpaired,
            Number = sidecar.Number,
            Codec = SubtitleSidecars.CodecFor(sidecar.Extension).ToString(),
            Size = info.Exists ? info.Length : 0,
            LastWriteTime = info.Exists ? info.LastWriteTimeUtc : default
        };
    }

    // Entries already known with the same size and time keep their verdict; the rest are read.
    public static List<SubtitleFile> Find(string mediaPath, IReadOnlyList<SubtitleFile>? known = null)
    {
        var files = SubtitleSidecars.Find(mediaPath).Select(s => From(s, mediaPath)).ToList();
        for (var i = 0; i < files.Count; i++)
        {
            var file = files[i];
            file.Index = FirstIndex + i;
            var previous = known?.FirstOrDefault(k => k.Inspected && k.Suffix == file.Suffix && k.Size == file.Size && k.LastWriteTime == file.LastWriteTime);
            file.Problem = previous != null ? previous.Problem : Inspect(file.FullPath(mediaPath), file.Codec);
            file.Inspected = true;
        }

        return files;
    }

    private static string? Inspect(string path, string codec)
    {
        if (codec == nameof(SubtitleCodec.Pgs))
        {
            return null;
        }

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (IOException)
        {
            return "unreadable";
        }

        string text;
        try
        {
            text = new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return "not UTF-8";
        }

        return SubtitleExtractor.HasCues(text, codec == nameof(SubtitleCodec.Ass) ? "ass" : "srt") ? null : "no cues";
    }

    public bool Matches(IMediaTrack track)
    {
        return track.Type == MediaTrackType.Subtitles
               && !track.IsCommentary
               && Matches(track.LanguageName, track.IsForced, track.IsHearingImpaired,
                   SubtitleSidecars.ExtensionFor(track.GetSubtitleCodec()));
    }

    public bool Matches(string language, bool forced, bool hearingImpaired, string? extension)
    {
        return forced == IsForced
               && hearingImpaired == IsHearingImpaired
               && IsoLanguage.Find(language).Base.Equals(IsoLanguage.Find(LanguageName).Base)
               && extension == SubtitleSidecars.ExtensionFor(GetCodec());
    }

    private SubtitleCodec GetCodec()
    {
        return Enum.TryParse<SubtitleCodec>(Codec, out var codec) ? codec : SubtitleCodec.Srt;
    }
}
