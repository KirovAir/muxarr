using Microsoft.EntityFrameworkCore;
using Muxarr.Core.Language;
using Muxarr.Core.Models;
using Muxarr.Core.Utilities;
using Muxarr.Data.Entities;
using Muxarr.Data.Extensions;

namespace Muxarr.Tests.Integration;

/// <summary>
/// Surrogates (2009) end to end: a real Matroska file whose own AC-3 and DTS are both
/// tagged "und", next to a Dutch subtitle, through the scanner and the converter. The DTS
/// used to be removed; both must survive, and the log must say what was removed and why.
/// </summary>
[TestClass]
public class UnlabeledAudioConversionTests : IntegrationTestBase
{
    [TestMethod]
    [DataRow(true, DisplayName = "Assume undetermined is original: on")]
    [DataRow(false, DisplayName = "Assume undetermined is original: off")]
    public async Task TwoUnlabeledAudioTracks_BothSurvive(bool assumeUndetermined)
    {
        var path = await MakeSurrogates();
        var profile = await Fixture.WithDbContext(async ctx =>
        {
            var p = new Profile
            {
                Name = "movies",
                Directories = [TempDir],
                AudioSettings = new TrackSettings
                {
                    Enabled = true,
                    AssumeUndeterminedIsOriginal = assumeUndetermined,
                    AllowedLanguages = [IsoLanguage.OriginalLanguage, IsoLanguage.Find("English")],
                    RemoveCommentary = true
                },
                SubtitleSettings = new TrackSettings
                {
                    Enabled = true,
                    AllowedLanguages = [IsoLanguage.Find("English")]
                }
            };
            ctx.Profiles.Add(p);
            await ctx.SaveChangesAsync();
            return p;
        });
        var file = await Fixture.ScanAndPersist(path, profile);
        Assert.AreEqual(2, file.Snapshot.Tracks.Count(t => t.Type == MediaTrackType.Audio
                                                           && t.LanguageName == IsoLanguage.UndeterminedName),
            "fixture: two unlabeled audio tracks");
        await Fixture.WithDbContext(async ctx =>
        {
            await ctx.MediaFiles.Where(f => f.Id == file.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(f => f.OriginalLanguage, "English"));
            return 0;
        });
        file.OriginalLanguage = "English";

        var conversion = await Fixture.SeedConversion(file, file.BuildTargetFromProfile(profile));
        await Fixture.Converter.RunAsync(CancellationToken.None);

        var done = await Fixture.AssertStateAsync(conversion.Id, ConversionState.Completed);
        var probed = await FileAssertions.ProbeAsync(path);
        var audio = probed.Snapshot.Tracks.Where(t => t.Type == MediaTrackType.Audio).ToList();
        Assert.AreEqual(2, audio.Count, $"both audio tracks must survive. Log:\n{done.LogText()}");
        CollectionAssert.AreEquivalent(new[] { "Ac3", "Dts" }, audio.Select(t => t.Codec).ToArray());
        Assert.AreEqual(0, probed.Snapshot.Tracks.Count(t => t.Type == MediaTrackType.Subtitles), "Dutch is not wanted");
        if (assumeUndetermined)
        {
            Assert.IsTrue(audio.All(t => t.LanguageName == "English"), "taken as the original language and tagged so");
        }

        StringAssert.Contains(done.LogText(), "Removing subtitle #3 Dutch");
        StringAssert.Contains(done.LogText(), "Dutch is not one of the profile's languages.");
        Assert.IsFalse(done.LogText().Contains("Removing audio"), done.LogText());
        FileAssertions.AssertNoStrayArtifacts(TempDir, Path.GetFileName(path));
    }

    [TestMethod]
    [DataRow(true, DisplayName = "Assume undetermined is original: on")]
    [DataRow(false, DisplayName = "Assume undetermined is original: off")]
    public async Task UnlabeledMainTrackBesideEnglishCommentaries_MainTrackSurvives(bool assumeUndetermined)
    {
        // Lords of Dogtown (2005): the film is an unlabeled DTS track, and the two tagged
        // English tracks are commentaries. It came out with only the commentaries.
        var path = Path.Combine(TempDir, "Lords of Dogtown (2005).mkv");
        var result = await ProcessExecutor.ExecuteProcessAsync("ffmpeg",
            "-y -loglevel error -f lavfi -i testsrc2=s=320x240:r=24 -f lavfi -i sine=frequency=440:sample_rate=48000 " +
            "-f lavfi -i sine=frequency=550:sample_rate=48000 -f lavfi -i sine=frequency=660:sample_rate=48000 -t 3 " +
            "-map 0:v -map 1:a -map 2:a -map 3:a -c:v libx264 -preset ultrafast -c:a:0 dca -strict -2 -c:a:1 ac3 -c:a:2 ac3 -ac 2 " +
            "-metadata:s:a:0 language=und -metadata:s:a:1 language=eng -metadata:s:a:2 language=eng " +
            "-metadata:s:a:1 \"title=Commentary with Director Catherine Hardwicke\" " +
            "-metadata:s:a:2 \"title=Commentary with Tony Alva and Stacey Peralta\" " +
            $"-disposition:a:0 default -disposition:a:1 0 -disposition:a:2 0 \"{path}\"",
            TimeSpan.FromSeconds(60));
        Assert.IsTrue(result.Success, result.Error);

        var profile = await Fixture.WithDbContext(async ctx =>
        {
            var p = new Profile
            {
                Name = "movies",
                Directories = [TempDir],
                AudioSettings = new TrackSettings
                {
                    Enabled = true,
                    AssumeUndeterminedIsOriginal = assumeUndetermined,
                    AllowedLanguages = [IsoLanguage.OriginalLanguage, IsoLanguage.Find("English")],
                    RemoveCommentary = true
                },
                SubtitleSettings = new TrackSettings()
            };
            ctx.Profiles.Add(p);
            await ctx.SaveChangesAsync();
            return p;
        });
        var file = await Fixture.ScanAndPersist(path, profile);
        Assert.AreEqual(2, file.Snapshot.Tracks.Count(t => t.IsCommentary), "fixture: two commentaries");
        await Fixture.WithDbContext(async ctx =>
        {
            await ctx.MediaFiles.Where(f => f.Id == file.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(f => f.OriginalLanguage, "English"));
            return 0;
        });
        file.OriginalLanguage = "English";

        var conversion = await Fixture.SeedConversion(file, file.BuildTargetFromProfile(profile));
        await Fixture.Converter.RunAsync(CancellationToken.None);

        var done = await Fixture.AssertStateAsync(conversion.Id, ConversionState.Completed);
        var audio = (await FileAssertions.ProbeAsync(path)).Snapshot.Tracks.Where(t => t.Type == MediaTrackType.Audio).ToList();
        Assert.AreEqual(1, audio.Count, $"only the film should be left. Log:\n{done.LogText()}");
        Assert.AreEqual("Dts", audio[0].Codec);
        Assert.IsFalse(audio[0].IsCommentary);
        StringAssert.Contains(done.LogText(), ": commentary.");
    }

    private async Task<string> MakeSurrogates()
    {
        var srt = Path.Combine(TempDir, "dutch.srt");
        await File.WriteAllTextAsync(srt, "1\n00:00:00,500 --> 00:00:01,500\nHallo\n\n");
        var path = Path.Combine(TempDir, "Surrogates (2009).mkv");
        var result = await ProcessExecutor.ExecuteProcessAsync("ffmpeg",
            "-y -loglevel error -f lavfi -i testsrc2=s=320x240:r=24 -f lavfi -i sine=frequency=440:sample_rate=48000 " +
            $"-f lavfi -i sine=frequency=660:sample_rate=48000 -i \"{srt}\" -t 3 " +
            "-map 0:v -map 1:a -map 2:a -map 3:s -c:v libx264 -preset ultrafast -c:a:0 ac3 -c:a:1 dca -strict -2 -ac 2 -c:s srt " +
            "-metadata:s:a:0 language=und -metadata:s:a:1 language=und -metadata:s:s:0 language=dut " +
            $"-disposition:a:0 default -disposition:a:1 0 \"{path}\"",
            TimeSpan.FromSeconds(60));
        Assert.IsTrue(result.Success, result.Error);
        return path;
    }
}
