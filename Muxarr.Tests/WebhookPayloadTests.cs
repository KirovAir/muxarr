using System.Text.Json;
using Muxarr.Core.Api.Models;

namespace Muxarr.Tests;

[TestClass]
public class WebhookPayloadTests
{
    [TestMethod]
    public void Download_SonarrV4_ReadsEpisodeFilesArray()
    {
        const string json = """
            {
              "eventType": "Download",
              "series": { "id": 1, "title": "Show", "originalLanguage": { "id": 1, "name": "English" } },
              "episodes": [ { "seasonNumber": 2, "episodeNumber": 3 } ],
              "episodeFiles": [ { "path": "/tv/Show/S02E03.mkv", "relativePath": "S02E03.mkv" } ]
            }
            """;

        var items = JsonSerializer.Deserialize<WebhookPayload>(json)!.GetFileItems();

        Assert.AreEqual(1, items.Count);
        Assert.AreEqual("/tv/Show/S02E03.mkv", items[0].FilePath);
        Assert.AreEqual("Show S02E03", items[0].Title);
        Assert.AreEqual("English", items[0].OriginalLanguage);
    }

    [TestMethod]
    public void Download_SonarrV3_StillReadsSingleEpisodeFile()
    {
        const string json = """
            {
              "eventType": "Download",
              "series": { "id": 1, "title": "Show" },
              "episodes": [ { "seasonNumber": 1, "episodeNumber": 1 } ],
              "episodeFile": { "path": "/tv/Show/S01E01.mkv" }
            }
            """;

        var items = JsonSerializer.Deserialize<WebhookPayload>(json)!.GetFileItems();

        Assert.AreEqual(1, items.Count);
        Assert.AreEqual("Show S01E01", items[0].Title);
    }

    [TestMethod]
    public void Download_SeasonPack_FallsBackToSeriesTitle()
    {
        const string json = """
            {
              "eventType": "Download",
              "series": { "id": 1, "title": "Show" },
              "episodes": [ { "seasonNumber": 1, "episodeNumber": 1 }, { "seasonNumber": 1, "episodeNumber": 2 }, { "seasonNumber": 1, "episodeNumber": 3 } ],
              "episodeFiles": [ { "path": "/tv/Show/S01E01.mkv" }, { "path": "/tv/Show/S01E02-E03.mkv" } ]
            }
            """;

        var items = JsonSerializer.Deserialize<WebhookPayload>(json)!.GetFileItems();

        CollectionAssert.AreEqual(new[] { "/tv/Show/S01E01.mkv", "/tv/Show/S01E02-E03.mkv" }, items.Select(i => i.FilePath).ToList());
        Assert.IsTrue(items.All(i => i.Title == "Show"));
    }
}
