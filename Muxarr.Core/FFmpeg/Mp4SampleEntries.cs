using System.Text;

namespace Muxarr.Core.FFmpeg;

/// <summary>
/// The names an MP4 files its video tracks under (the sample entry in each track's stsd box).
/// Dolby Vision has names of its own, and ffmpeg's mp4 muxer cannot write some of them: a
/// dvhe track comes out as hev1. The box is otherwise the same, its Dolby Vision settings
/// (dvcC/dvvC) still inside, but a player that goes by the name sees plain HEVC, and a
/// profile 5 track has no plain HEVC to fall back to. War Machine (2026) stopped playing.
/// </summary>
public static class Mp4SampleEntries
{
    // The name ffmpeg writes in place of each Dolby Vision one. The pairs share a layout:
    // dvhe/hev1 and dvav/avc3 may carry parameter sets in the stream, dvh1/hvc1 and dva1/avc1 may not.
    private static readonly Dictionary<string, string> PlainNames = new(StringComparer.Ordinal)
    {
        ["dvhe"] = "hev1",
        ["dvh1"] = "hvc1",
        ["dvav"] = "avc3",
        ["dva1"] = "avc1",
        ["dav1"] = "av01"
    };

    // Dolby Vision configuration boxes: profiles up to 7, 8 to 10, and 11 and up.
    private static readonly string[] DolbyVisionBoxes = ["dvcC", "dvvC", "dvwC"];

    // A VisualSampleEntry's fixed fields after its 8-byte header; its child boxes follow.
    private const int VisualSampleEntryFields = 78;

    public static bool IsDolbyVision(string? name)
    {
        return name != null && PlainNames.ContainsKey(name);
    }

    public sealed record Rename(int Track, string From, string To);

    /// <summary>
    /// Files each given track under its Dolby Vision name again, in place. The key is the
    /// track's position in the file (its trak box order), the value the name it should have.
    /// Only a video track that is filed under the matching plain name and still holds its
    /// Dolby Vision settings is touched, and only those four bytes change.
    /// </summary>
    public static List<Rename> RestoreDolbyVisionNames(string path, IReadOnlyDictionary<int, string> wanted)
    {
        var renames = new List<Rename>();
        if (wanted.Count == 0)
        {
            return renames;
        }

        using var file = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
        if (FindTopLevelBox(file, "moov") is not { } moov || moov.Size > int.MaxValue)
        {
            return renames;
        }

        var buffer = new byte[moov.Size];
        file.Position = moov.Offset;
        file.ReadExactly(buffer);

        var track = -1;
        foreach (var trak in Children(buffer, moov.HeaderSize, buffer.Length).Where(b => b.Type == "trak"))
        {
            track++;
            if (!wanted.TryGetValue(track, out var name) || !PlainNames.TryGetValue(name, out var plain))
            {
                continue;
            }

            if (VideoSampleEntry(buffer, trak) is not { } entry || entry.Type != plain)
            {
                continue;
            }

            var settings = Children(buffer, entry.Offset + entry.HeaderSize + VisualSampleEntryFields,
                entry.Offset + entry.Size);
            if (!settings.Any(b => DolbyVisionBoxes.Contains(b.Type)))
            {
                continue;
            }

            file.Position = moov.Offset + entry.Offset + 4;
            file.Write(Encoding.ASCII.GetBytes(name));
            renames.Add(new Rename(track, plain, name));
        }

        if (renames.Count > 0)
        {
            file.Flush(true);
        }

        return renames;
    }

    private readonly record struct Box(string Type, long Offset, int HeaderSize, long Size);

    // The first sample entry of a video track: trak/mdia/(hdlr 'vide')/minf/stbl/stsd.
    private static Box? VideoSampleEntry(byte[] buffer, Box trak)
    {
        var mdia = Child(buffer, trak, "mdia");
        if (mdia == null)
        {
            return null;
        }

        var hdlr = Child(buffer, mdia.Value, "hdlr");
        // FullBox version/flags (4), pre_defined (4), then the handler type.
        var handler = hdlr is { } h && h.Size >= h.HeaderSize + 12
            ? Encoding.ASCII.GetString(buffer, (int)(h.Offset + h.HeaderSize + 8), 4)
            : null;
        if (handler != "vide")
        {
            return null;
        }

        var stsd = Child(buffer, mdia.Value, "minf") is { } minf && Child(buffer, minf, "stbl") is { } stbl
            ? Child(buffer, stbl, "stsd")
            : null;
        if (stsd is not { } s)
        {
            return null;
        }

        // FullBox version/flags (4) and the entry count (4) come before the entries.
        return Children(buffer, s.Offset + s.HeaderSize + 8, s.Offset + s.Size).Cast<Box?>().FirstOrDefault();
    }

    private static Box? Child(byte[] buffer, Box parent, string type)
    {
        return Children(buffer, parent.Offset + parent.HeaderSize, parent.Offset + parent.Size)
            .Where(b => b.Type == type)
            .Cast<Box?>()
            .FirstOrDefault();
    }

    private static IEnumerable<Box> Children(byte[] buffer, long start, long end)
    {
        var position = start;
        while (position + 8 <= end)
        {
            long size = ReadUInt32(buffer, position);
            var header = 8;
            if (size == 1)
            {
                if (position + 16 > end)
                {
                    yield break;
                }

                size = (long)ReadUInt64(buffer, position + 8);
                header = 16;
            }
            else if (size == 0)
            {
                size = end - position;
            }

            if (size < header || position + size > end)
            {
                yield break;
            }

            yield return new Box(Encoding.ASCII.GetString(buffer, (int)position + 4, 4), position, header, size);
            position += size;
        }
    }

    // Top-level boxes are walked by seeking, so the media data is never read.
    private static Box? FindTopLevelBox(FileStream file, string type)
    {
        var header = new byte[16];
        long position = 0;
        while (position + 8 <= file.Length)
        {
            file.Position = position;
            file.ReadExactly(header, 0, 8);
            long size = ReadUInt32(header, 0);
            var headerSize = 8;
            if (size == 1)
            {
                if (position + 16 > file.Length)
                {
                    return null;
                }

                file.ReadExactly(header, 8, 8);
                size = (long)ReadUInt64(header, 8);
                headerSize = 16;
            }
            else if (size == 0)
            {
                size = file.Length - position;
            }

            if (size < headerSize || position + size > file.Length)
            {
                return null;
            }

            var name = Encoding.ASCII.GetString(header, 4, 4);
            if (name == type)
            {
                return new Box(name, position, headerSize, size);
            }

            position += size;
        }

        return null;
    }

    private static uint ReadUInt32(byte[] buffer, long offset)
    {
        return (uint)(buffer[offset] << 24 | buffer[offset + 1] << 16 | buffer[offset + 2] << 8 | buffer[offset + 3]);
    }

    private static ulong ReadUInt64(byte[] buffer, long offset)
    {
        return (ulong)ReadUInt32(buffer, offset) << 32 | ReadUInt32(buffer, offset + 4);
    }
}
