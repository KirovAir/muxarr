using Muxarr.Core.Extensions;
using Muxarr.Data.Entities;

namespace Muxarr.Tests.Integration;

/// <summary>Real scanner against the committed and derived fixtures.</summary>
[TestClass]
public class MediaScannerIntegrationTests : IntegrationTestBase
{
    [TestMethod]
    public async Task Scan_ComplexMkv_ParsesNineTracksWithExpectedFlags()
    {
        var path = CopyFixture("test_complex.mkv");
        var profile = await Fixture.SeedProfile();

        var file = await Fixture.ScanAndPersist(path, profile);

        Assert.AreEqual("Matroska", file.ContainerType);
        Assert.AreEqual(ContainerFamily.Matroska, file.ContainerType.ToContainerFamily());
        Assert.IsTrue(file.DurationMs > 0, "duration should be populated");
        Assert.IsTrue(file.Size > 0, "size should be populated");
        Assert.AreEqual(9, file.Tracks.Count);
        Assert.AreEqual(9, file.TrackCount);

        var tracks = file.Tracks.OrderBy(t => t.TrackNumber).ToList();
        Assert.AreEqual(MediaTrackType.Video, tracks[0].Type);
        Assert.AreEqual(3, tracks.Count(t => t.Type == MediaTrackType.Audio));
        Assert.AreEqual(5, tracks.Count(t => t.Type == MediaTrackType.Subtitles));

        var defaultAudio = tracks.First(t => t.Type == MediaTrackType.Audio && t.IsDefault);
        Assert.AreEqual("English", defaultAudio.LanguageName);
        Assert.IsTrue(tracks.Any(t => t.Type == MediaTrackType.Audio && t.IsCommentary));
        Assert.IsTrue(tracks.Any(t => t.Type == MediaTrackType.Subtitles && t.IsForced));
        Assert.IsTrue(tracks.Any(t => t.Type == MediaTrackType.Subtitles && t.IsHearingImpaired));
    }

    [TestMethod]
    public async Task Scan_DerivedMp4_PersistsAsMp4Container()
    {
        var path = CopyFixture("test.mp4");
        var profile = await Fixture.SeedProfile();

        var file = await Fixture.ScanAndPersist(path, profile);

        Assert.AreEqual(ContainerFamily.Mp4, file.ContainerType.ToContainerFamily(),
            $"expected Mp4 family, container was: {file.ContainerType}");
        Assert.IsTrue(file.Tracks.Any(t => t.Type == MediaTrackType.Video));
        Assert.IsTrue(file.Tracks.Any(t => t.Type == MediaTrackType.Audio));
    }
}
