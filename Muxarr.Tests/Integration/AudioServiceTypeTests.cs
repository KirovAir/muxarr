using Microsoft.EntityFrameworkCore;
using Muxarr.Core.Language;
using Muxarr.Core.Models;
using Muxarr.Core.Utilities;
using Muxarr.Data.Entities;
using Muxarr.Data.Extensions;

namespace Muxarr.Tests.Integration;

// AC-3 coded for visually impaired listeners: ffmpeg wiped its header flags, so the original flag never stuck.
[TestClass]
public class AudioServiceTypeTests : IntegrationTestBase
{
    [TestMethod]
    public async Task VisuallyImpairedServiceType_OriginalFlagSticks()
    {
        var path = await MakeFile("vi", "Show S01E01.mkv");
        var profile = await SeedTvProfile();
        var file = await Fixture.ScanAndPersist(path, profile);
        await SetOriginalLanguage(file, "English");

        var audio = file.Snapshot.Tracks.Single(t => t.Type == MediaTrackType.Audio);
        Assert.IsTrue(audio.IsDefault, "the header says default; ffmpeg's reading of the bitstream must not wipe it");
        Assert.IsFalse(audio.IsOriginal, "fixture: no original flag yet");
        Assert.IsTrue(audio.IsVisualImpaired, "the bitstream's hint is kept");
        Assert.IsTrue(file.CheckHasNonStandardMetadata(profile), "fixture: the original flag is missing");

        var conversion = await Fixture.SeedConversion(file, file.BuildTargetFromProfile(profile));
        await Fixture.Converter.RunAsync(CancellationToken.None);

        var done = await Fixture.AssertStateAsync(conversion.Id, ConversionState.Completed);
        StringAssert.Contains(done.LogText(), "Metadata updated successfully.");
        Assert.IsFalse(done.LogText().Contains("Falling through"), done.LogText());

        var after = await Fixture.ScanAndPersist(path, profile);
        after.OriginalLanguage = "English";
        var kept = after.Snapshot.Tracks.Single(t => t.Type == MediaTrackType.Audio);
        Assert.IsTrue(kept.IsOriginal);
        Assert.IsTrue(kept.IsDefault);
        Assert.IsFalse(after.CheckHasNonStandardMetadata(profile), "nothing left to fix");
    }

    [TestMethod]
    [DataRow("vi", false, true, DisplayName = "Visually impaired")]
    [DataRow("hi", true, false, DisplayName = "Hearing impaired")]
    public async Task ServiceType_KeepsTheHeaderFlags(string serviceType, bool hearing, bool visual)
    {
        var path = await MakeFile(serviceType, $"{serviceType}.mkv", original: true);
        var profile = await SeedTvProfile();

        var file = await Fixture.ScanAndPersist(path, profile);

        var audio = file.Snapshot.Tracks.Single(t => t.Type == MediaTrackType.Audio);
        Assert.IsTrue(audio.IsDefault);
        Assert.IsTrue(audio.IsOriginal);
        Assert.AreEqual(hearing, audio.IsHearingImpaired);
        Assert.AreEqual(visual, audio.IsVisualImpaired);
    }

    // English AC-3 with the given service type, default in the header, original only when asked.
    private async Task<string> MakeFile(string serviceType, string name, bool original = false)
    {
        var raw = Path.Combine(TempDir, $"raw_{serviceType}.mkv");
        var made = await ProcessExecutor.ExecuteProcessAsync("ffmpeg",
            "-y -loglevel error -f lavfi -i testsrc2=s=320x240:r=24 -f lavfi -i sine=frequency=440:sample_rate=48000 -t 2 " +
            $"-map 0:v -map 1:a -c:v libx264 -preset ultrafast -c:a ac3 -ac 2 -audio_service_type {serviceType} \"{raw}\"",
            TimeSpan.FromSeconds(60));
        Assert.IsTrue(made.Success, made.Error);

        var path = Path.Combine(TempDir, name);
        var muxed = await ProcessExecutor.ExecuteProcessAsync("mkvmerge",
            $"-q -o \"{path}\" --language 1:eng --default-track-flag 1:1{(original ? " --original-flag 1:1" : "")} \"{raw}\"",
            TimeSpan.FromSeconds(60));
        Assert.IsTrue(muxed.Success, muxed.Error + muxed.Output);
        File.Delete(raw);
        return path;
    }

    private async Task<Profile> SeedTvProfile()
    {
        return await Fixture.WithDbContext(async ctx =>
        {
            var p = new Profile
            {
                Name = "TV",
                Directories = [TempDir],
                ClearFileTitle = true,
                ClearVideoTrackNames = true,
                AudioSettings = new TrackSettings
                {
                    Enabled = true,
                    AssumeUndeterminedIsOriginal = true,
                    AllowedLanguages = [IsoLanguage.OriginalLanguage, IsoLanguage.Find("English")],
                    RemoveCommentary = true
                },
                SubtitleSettings = new TrackSettings
                {
                    Enabled = true,
                    AllowedLanguages = [IsoLanguage.Find("English")],
                    RemoveCommentary = true
                }
            };
            ctx.Profiles.Add(p);
            await ctx.SaveChangesAsync();
            return p;
        });
    }

    private async Task SetOriginalLanguage(MediaFile file, string language)
    {
        await Fixture.WithDbContext(async ctx =>
        {
            await ctx.MediaFiles.Where(f => f.Id == file.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(f => f.OriginalLanguage, language));
            return 0;
        });
        file.OriginalLanguage = language;
    }
}
