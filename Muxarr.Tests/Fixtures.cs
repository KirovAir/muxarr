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
