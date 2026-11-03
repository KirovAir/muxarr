using Muxarr.Core.Models;
using Muxarr.Core.Extensions;
using Muxarr.Data.Entities;
using Muxarr.Data.Extensions;
using Muxarr.Web.Services;

namespace Muxarr.Tests.Integration;

/// <summary>
/// The swap that puts the converted copy in place of the original.
/// </summary>
[TestClass]
public class SwapSafetyTests : IntegrationTestBase
{
    [TestMethod]
    public async Task SourceReplacedDuringConversion_TheReplacementSurvives()
    {
        var path = CopyFixture("test_complex.mkv");
        var replacement = CopyFixture("test.mkv", "replacement-source.mkv");
        var replacementHash = FileAssertions.Sha256(replacement);

        var conversion = await QueueDroppingASubtitle(path);

        // Sonarr imports an upgrade to the same path once the copy has been checked.
        var replaced = false;
        void OnChanged(ConverterProgressEvent e)
        {
            if (!replaced && e.Conversion.Id == conversion.Id && e.Conversion.GetLogSnapshot().Any(l => l.Message.Contains("Validation of new file is ok!")))
            {
                replaced = true;
                File.Copy(replacement, path, true);
            }
        }

        Fixture.Converter.ConverterStateChanged += OnChanged;
        try
        {
            await Fixture.Converter.RunAsync(CancellationToken.None);
        }
        finally
        {
            Fixture.Converter.ConverterStateChanged -= OnChanged;
        }

        Assert.IsTrue(replaced, "the hook never fired");
        var result = await Fixture.AssertStateAsync(conversion.Id, ConversionState.Failed);
        StringAssert.Contains(result.LogText(), "replaced while it was being converted");
        FileAssertions.AssertSha256Equals(path, replacementHash, "the imported file must be left as it is");
        FileAssertions.AssertNoStrayArtifacts(TempDir, Path.GetFileName(path));
    }

    [TestMethod]
    public async Task ConvertedFile_KeepsTheOriginalsPermissions()
    {
        if (OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Unix permissions only");
        }

        var path = CopyFixture("test_complex.mkv");
        const UnixFileMode shareMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead |
                                       UnixFileMode.GroupWrite | UnixFileMode.OtherRead | UnixFileMode.OtherWrite;
        File.SetUnixFileMode(path, shareMode);
        var conversion = await QueueDroppingASubtitle(path);

        await Fixture.Converter.RunAsync(CancellationToken.None);

        await Fixture.AssertStateAsync(conversion.Id, ConversionState.Completed);
        Assert.AreEqual(shareMode, File.GetUnixFileMode(path));
    }

    // An in-place mkvpropedit edit that does not stick falls
    // through to a remux. The edit had already changed the file, so the check before
    // the swap took Muxarr's own edit for an upgrade from Sonarr and failed, on every retry.
    [TestMethod]
    public async Task MetadataEditThatFallsThroughToARemux_StillSwaps()
    {
        var path = CopyFixture("test_complex.mkv");
        var profile = await Fixture.SeedProfile();
        var scanned = await Fixture.ScanAndPersist(path, profile);
        var sub = scanned.Snapshot.Tracks.First(t => t.Type == MediaTrackType.Subtitles);

        // A name that says SDH: the hearing-impaired flag reads straight back off it,
        // so turning the flag off never verifies in place.
        var named = await Core.Utilities.ProcessExecutor.ExecuteProcessAsync("mkvpropedit",
            $"\"{path}\" --edit track:{sub.Index + 1} --set \"name=English SDH\" --set flag-hearing-impaired=1",
            TimeSpan.FromSeconds(30));
        Assert.IsTrue(named.Success, named.Error);
        var file = await Fixture.ScanAndPersist(path, profile);
        Assert.IsTrue(file.Snapshot.Tracks.Single(t => t.Index == sub.Index).IsHearingImpaired, "fixture: SDH by name");

        var tracks = file.Snapshot.Tracks.ToSnapshots();
        tracks.Single(t => t.Index == sub.Index).IsHearingImpaired = false;
        var target = file.BuildTargetFromCustom(tracks);
        Assert.AreEqual(ConversionPlanner.ConversionStrategy.MetadataEdit,
            ConversionPlanner.Plan(file.Snapshot, target).Strategy, "fixture: starts as an in-place edit");
        var conversion = await Fixture.SeedConversion(file, target, true);

        await Fixture.Converter.RunAsync(CancellationToken.None);

        var done = await Fixture.ReloadConversion(conversion.Id);
        StringAssert.Contains(done.LogText(), "Falling through to remux");
        Assert.IsFalse(done.LogText().Contains("replaced while it was being converted"), done.LogText());
        Assert.AreEqual(ConversionState.Completed, done.State, done.LogText());
        FileAssertions.AssertNoStrayArtifacts(TempDir, Path.GetFileName(path));
    }

    private async Task<MediaConversion> QueueDroppingASubtitle(string path)
    {
        var profile = await Fixture.SeedProfile();
        var file = await Fixture.ScanAndPersist(path, profile);
        var droppedSub = file.Snapshot.Tracks.First(t => t.Type == MediaTrackType.Subtitles);
        var target = file.BuildTargetFromCustom(file.Snapshot.Tracks.Where(t => t.Index != droppedSub.Index).ToSnapshots());
        return await Fixture.SeedConversion(file, target, true);
    }
}
