using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Muxarr.Core.Language;
using Muxarr.Data.Entities;
using Muxarr.Data.Extensions;
using Muxarr.Web.Services;

namespace Muxarr.Tests.Integration;

/// <summary>
/// Throws inside the converter at each step of the finalize sequence and records
/// what is on disk afterwards, and again after the startup recovery sweep. This
/// exercises the handled-failure paths; a hard kill would skip the rollback and
/// is not covered here. Every row must leave the video playable under its own
/// name with no temp or backup file left.
/// </summary>
[TestClass]
public class FinalizeFaultInjectionTests : IntegrationTestBase
{
    [TestCleanup]
    public void ClearHook()
    {
        MediaConverterService.FaultHook = null;
    }

    [TestMethod]
    [DataRow("validated")]
    [DataRow("backup-renamed")]
    [DataRow("swapped")]
    [DataRow("backup-deleted")]
    [DataRow("rescanned")]
    [DataRow("sidecars-placed")]
    [DataRow("arr-updated")]
    [DataRow("sources-deleted")]
    [DataRow("post-processed")]
    public async Task Crash_At_Step_Leaves_A_Playable_Video(string step)
    {
        var path = CopyFixture("test_complex.mkv");
        var sourceHash = Hash(path);
        var profile = await SeedEnglishOnlyProfile();
        var file = await Fixture.ScanAndPersist(path, profile);
        Assert.IsTrue(file.NeedsWork, "fixture must need work for a remux to run");
        await Fixture.Converter.AddMediaToQueue(file);

        var hit = false;
        MediaConverterService.FaultHook = s =>
        {
            if (s != step)
            {
                return Task.CompletedTask;
            }

            hit = true;
            throw new IOException($"injected failure at {step}");
        };
        await Fixture.Converter.RunAsync(CancellationToken.None);
        MediaConverterService.FaultHook = null;
        Assert.IsTrue(hit, $"the {step} hook never ran; the seam has moved");

        var afterCrash = Listing();
        var state = await Fixture.WithDbContext(async ctx =>
            (await ctx.MediaConversions.OrderByDescending(c => c.Id).FirstAsync()).State);

        // A new converter instance runs the startup sweep (leftover temp files, .muxbak).
        var recovery = await ConverterIntegrationFixture.CreateAsync(TempDir);
        try
        {
            await recovery.Converter.RunAsync(CancellationToken.None);
        }
        finally
        {
            recovery.Dispose();
        }

        var afterRecovery = Listing();
        var videoExists = File.Exists(path);
        var isSource = videoExists && Hash(path) == sourceHash;
        var isConverted = videoExists && !isSource && await IsEnglishOnly(path);

        var row = $"STEP {step}: conversion={state}\n" +
                  $"  after crash:    {afterCrash}\n" +
                  $"  after recovery: {afterRecovery}\n" +
                  $"  video under its name: {(isSource ? "ORIGINAL" : isConverted ? "CONVERTED" : videoExists ? "UNKNOWN" : "MISSING")}\n";
        Console.WriteLine(row);
        if (Environment.GetEnvironmentVariable("MUXARR_FAULT_MATRIX") is { Length: > 0 } matrix)
        {
            File.AppendAllText(matrix, row);
        }

        Assert.AreEqual(ConversionState.Failed, state, $"{step}: an injected failure must fail the conversion");
        Assert.IsTrue(videoExists, $"{step}: no video left under the original name\n{afterRecovery}");
        if (isConverted)
        {
            // The profile extracts the English subtitles; each must be in place or still in its temp copy.
            foreach (var name in new[] { "test_complex.en.srt", "test_complex.en.sdh.srt", "test_complex.en.forced.srt" })
            {
                Assert.IsTrue(File.Exists(Path.Combine(TempDir, name)) || File.Exists(Path.Combine(TempDir, name + ".muxsub")),
                    $"{step}: {name} is in neither place\n{afterRecovery}");
            }
        }
        else
        {
            Assert.IsFalse(afterRecovery.Contains(".muxsub"), $"{step}: temp subtitle left behind without a swap\n{afterRecovery}");
        }
        Assert.IsFalse(afterRecovery.Contains(".muxbak"), $"{step}: backup left behind after recovery\n{afterRecovery}");
        Assert.IsTrue(isSource || isConverted, $"{step}: video under the name is neither the source nor the converted file\n{afterRecovery}");
        Assert.IsFalse(afterRecovery.Contains(".muxtmp"), $"{step}: temp output left behind\n{afterRecovery}");
    }

    private async Task<Profile> SeedEnglishOnlyProfile()
    {
        var profile = await Fixture.SeedProfile();
        return await Fixture.WithDbContext(async ctx =>
        {
            var p = await ctx.Profiles.FirstAsync(x => x.Id == profile.Id);
            p.SubtitleSettings = new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [new LanguagePreference { Language = IsoLanguage.Find("English") }],
                Location = SubtitleLocation.NextToFile
            };
            await ctx.SaveChangesAsync();
            return p;
        });
    }

    private static async Task<bool> IsEnglishOnly(string path)
    {
        var probed = new MediaFile { Path = path };
        await probed.SetFileDataFromFFprobe();
        var subs = probed.Snapshot.Tracks.Where(t => t.Type == Core.Models.MediaTrackType.Subtitles).ToList();
        return subs.All(t => t.LanguageCode == "eng" && t.Codec == nameof(Core.Extensions.SubtitleCodec.Pgs));
    }

    private string Listing()
    {
        return string.Join(", ", Directory.EnumerateFiles(TempDir)
            .Where(f => !f.EndsWith(".db") && !f.Contains("muxarr-it.db") && !f.EndsWith(".db-wal") && !f.EndsWith(".db-shm"))
            .OrderBy(f => f)
            .Select(f => $"{Path.GetFileName(f)}[{Hash(f)[..8]}]"));
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
