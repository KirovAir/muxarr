using Muxarr.Core.Utilities;

namespace Muxarr.Tests;

/// <summary>
/// Resolves committed fixtures under Muxarr.Tests/Fixtures/ and derived
/// ones (MP4 variants, asymmetric MKV) generated once per run by
/// <see cref="EnsurePoolAsync"/>, called from the integration assembly init.
/// </summary>
public static class Fixtures
{
    public static readonly string SourceDir = Path.Combine(AppContext.BaseDirectory, "Fixtures");
    public static readonly string PoolDir = Path.Combine(Path.GetTempPath(), "muxarr-test-pool");

    public static string Resolve(string name)
    {
        var fromSource = Path.Combine(SourceDir, name);
        if (File.Exists(fromSource))
        {
            return fromSource;
        }

        var fromPool = Path.Combine(PoolDir, name);
        if (File.Exists(fromPool))
        {
            return fromPool;
        }

        throw new FileNotFoundException(
            $"Fixture '{name}' not found in {SourceDir} or {PoolDir}. " +
            "Derived fixtures require Fixtures.EnsurePoolAsync to have been called - " +
            "usually from the integration assembly initializer.");
    }

    public static async Task EnsurePoolAsync()
    {
        Directory.CreateDirectory(PoolDir);

        await GenerateMp4FromMkvAsync("test.mkv", "test.mp4");
        await GenerateMp4FromMkvAsync("test_complex.mkv", "test_complex.mp4");
        await GenerateAsymmetricMkvAsync("asymmetric.mkv");
        await GenerateRichMp4Async("test_rich.mp4");
    }

    /// <summary>
    /// Full-shape MP4 derived from test_complex.mkv: 3 audio tracks + 5
    /// subtitles (mov_text) with explicit dispositions (commentary, dub,
    /// forced, hearing_impaired) and per-track metadata that round-trips
    /// through ffprobe. Used by the complex-conversion integration tests
    /// that need a non-Matroska source rich enough to stress the ffmpeg
    /// remux + disposition path.
    /// </summary>
    private static async Task GenerateRichMp4Async(string targetName)
    {
        var source = Path.Combine(SourceDir, "test_complex.mkv");
        var target = Path.Combine(PoolDir, targetName);

        if (!File.Exists(source))
        {
            Assert.Inconclusive($"Source fixture 'test_complex.mkv' missing at {source}.");
        }

        if (File.Exists(target) && File.GetLastWriteTimeUtc(target) >= File.GetLastWriteTimeUtc(source))
        {
            return;
        }

        if (File.Exists(target))
        {
            File.Delete(target);
        }

        // Input stream order in test_complex.mkv:
        //   v:0, a:0 English 5.1, a:1 Commentary, a:2 French Dub,
        //   s:0 English, s:1 Forced, s:2 SDH, s:3 French, s:4 Spanish
        //
        // -disposition uses absolute output stream index; -metadata uses
        // stream-specifier syntax ("s:N"). stream-index mapping after
        // ffmpeg's muxer: same order, indices 0..8.
        var args =
            $"-y -loglevel error -i \"{source}\" " +
            "-map 0:v -map 0:a -map 0:s " +
            "-c:v copy -c:a aac -c:s mov_text " +
            "-metadata:s:0 title=\"Video 4K HDR\" " +
            "-metadata:s:1 title=\"English 5.1\" -metadata:s:1 language=eng " +
            "-metadata:s:2 title=\"Commentary\" -metadata:s:2 language=eng -disposition:2 +comment " +
            "-metadata:s:3 title=\"French Dub\" -metadata:s:3 language=fre -disposition:3 +dub " +
            "-metadata:s:4 title=\"English\" -metadata:s:4 language=eng " +
            "-metadata:s:5 title=\"English Forced\" -metadata:s:5 language=eng -disposition:5 +forced " +
            "-metadata:s:6 title=\"English SDH\" -metadata:s:6 language=eng -disposition:6 +hearing_impaired " +
            "-metadata:s:7 title=\"French\" -metadata:s:7 language=fre " +
            "-metadata:s:8 title=\"Spanish\" -metadata:s:8 language=spa " +
            "-movflags +use_metadata_tags " +
            $"-f mp4 \"{target}\"";
        var result = await ProcessExecutor.ExecuteProcessAsync("ffmpeg", args, TimeSpan.FromSeconds(60));
        if (!result.Success || !File.Exists(target))
        {
            Assert.Inconclusive($"Failed to generate {targetName}: {result.Error?.Trim()}");
        }
    }

    private static async Task GenerateMp4FromMkvAsync(string sourceName, string targetName)
    {
        var source = Path.Combine(SourceDir, sourceName);
        var target = Path.Combine(PoolDir, targetName);

        if (!File.Exists(source))
        {
            Assert.Inconclusive($"Source fixture '{sourceName}' missing at {source}.");
        }

        // Cache: skip regeneration if the pool file is newer than the source.
        if (File.Exists(target) && File.GetLastWriteTimeUtc(target) >= File.GetLastWriteTimeUtc(source))
        {
            return;
        }

        if (File.Exists(target))
        {
            File.Delete(target);
        }

        // Stream-copy video, transcode audio to AAC, drop subs (MP4 can't
        // carry every subtitle codec the source MKVs use).
        var args = $"-y -loglevel error -i \"{source}\" -map 0:v -map 0:a -c:v copy -c:a aac \"{target}\"";
        var result = await ProcessExecutor.ExecuteProcessAsync("ffmpeg", args, TimeSpan.FromSeconds(60));
        if (!result.Success || !File.Exists(target))
        {
            Assert.Inconclusive($"Failed to generate {targetName} from {sourceName}: {result.Error?.Trim()}");
        }
    }

    /// <summary>
    /// 3s video + 10s audio MKV. Container duration is the max (10s) so a
    /// remux that drops the audio lands at 3s, which is what the
    /// OutputValidator truncation-rejection test needs.
    /// </summary>
    private static async Task GenerateAsymmetricMkvAsync(string targetName)
    {
        var target = Path.Combine(PoolDir, targetName);
        if (File.Exists(target))
        {
            return;
        }

        // mpeg4 + ac3 picked because they are in every reasonable ffmpeg build.
        var args =
            "-y -loglevel error " +
            "-f lavfi -i \"testsrc=duration=3:size=160x120:rate=10\" " +
            "-f lavfi -i \"sine=duration=10:frequency=440\" " +
            "-c:v mpeg4 -c:a ac3 " +
            $"\"{target}\"";
        var result = await ProcessExecutor.ExecuteProcessAsync("ffmpeg", args, TimeSpan.FromSeconds(60));
        if (!result.Success || !File.Exists(target))
        {
            Assert.Inconclusive($"Failed to generate {targetName}: {result.Error?.Trim()}");
        }
    }
}
