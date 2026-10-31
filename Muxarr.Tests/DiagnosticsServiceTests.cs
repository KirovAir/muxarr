using Microsoft.EntityFrameworkCore;
using Muxarr.Core.Config;
using Muxarr.Core.Language;
using Muxarr.Data;
using Muxarr.Data.Entities;
using Muxarr.Data.Extensions;
using Muxarr.Web.Services;

namespace Muxarr.Tests;

/// <summary>
/// The exported diagnostics file has what is needed to find a problem, and no secrets.
/// </summary>
[TestClass]
public class DiagnosticsServiceTests
{
    private const string WebhookKey = "a8125cfff97846849b0bdabbfa53379b";
    private const string RadarrKey = "radarr0123456789abcdef0123456789";
    private const string PasswordHash = "AQAAAAIAAYagAAAAEIE2OVgXvQAwu4OI4qAaN";
    private const string DiscordHook = "https://discord.com/api/webhooks/123/TopSecretWebhookToken";
    private const string PlexToken = "AbCdEfGhIjKlMnOpQrSt";

    private string _dbPath = null!;
    private DbContextOptions<AppDbContext> _options = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"muxarr_diag_{Guid.NewGuid():N}.db");
        _options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        await using var context = new AppDbContext(_options);
        await context.Database.EnsureCreatedAsync();

        context.Configs.Set(new WebhookConfig { ApiKey = WebhookKey, MuxarrUrl = "http://tower:8183" });
        context.Configs.Set(new AuthConfig { Username = "brett", PasswordHash = PasswordHash }, AuthConfig.Key);
        context.Configs.Set(new ProcessingConfig
        {
            PostProcessingEnabled = true,
            PostProcessingCommand = $"curl -s 'http://plex:32400/library/sections/1/refresh?X-Plex-Token={PlexToken}'"
        });
        context.Configs.Set(new List<NotificationConfig>
        {
            new() { Name = "Discord", Provider = "Discord", Settings = new() { ["WebhookUrl"] = DiscordHook } }
        });
        context.Integrations.Add(new Muxarr.Data.Entities.Integration { Name = "Radarr", Type = IntegrationType.Radarr, Url = "http://radarr:7878", ApiKey = RadarrKey });
        var profile = new Profile
        {
            Name = "Movies & TV",
            Directories = ["/data/media/bluray"],
            AudioSettings = new TrackSettings
            {
                Enabled = true,
                AllowedLanguages = [IsoLanguage.OriginalLanguage, IsoLanguage.Find("English")]
            }
        };
        context.Profiles.Add(profile);
        var file = new MediaFile { Path = "/data/media/bluray/Unknown Film (2021)/Unknown Film (2021).mkv", Profile = profile };
        context.MediaFiles.Add(file);
        context.MediaConversions.Add(new MediaConversion
        {
            Name = "Unknown Film", MediaFile = file, State = ConversionState.Failed,
            LogEntries = [new ConversionLogEntry { Timestamp = DateTime.UtcNow, Message = "Something bad happened while processing the file.", IsError = true }]
        });
        context.LogEntries.Add(new LogEntry
        {
            Timestamp = DateTime.UtcNow.AddMinutes(-5), Level = "ERR", Source = "MediaConverterService",
            Message = "Conversion state for Big Remux is in progress on startup.", Exception = "System.Exception: boom\n   at Somewhere()"
        });
        context.LogEntries.Add(new LogEntry
        {
            Timestamp = DateTime.UtcNow.AddDays(-30), Level = "INF", Source = "Old", Message = "a month ago"
        });
        await context.SaveChangesAsync();
    }

    [TestCleanup]
    public void Cleanup()
    {
        using var context = new AppDbContext(_options);
        context.Database.EnsureDeleted();
    }

    [TestMethod]
    public async Task Report_HasWhatIsNeeded()
    {
        var report = await new DiagnosticsService(new Factory(_options)).BuildReportAsync();

        StringAssert.Contains(report, "Profile \"Movies & TV\"");
        StringAssert.Contains(report, "keep Original Language, English");
        StringAssert.Contains(report, "Radarr \"Radarr\": http://radarr:7878, API key set");
        StringAssert.Contains(report, "Without an original language from Sonarr/Radarr: 1");
        StringAssert.Contains(report, "/data/media/bluray/Unknown Film (2021)/Unknown Film (2021).mkv");
        StringAssert.Contains(report, "Something bad happened while processing the file.");
        StringAssert.Contains(report, "Conversion state for Big Remux is in progress on startup.");
        StringAssert.Contains(report, "System.Exception: boom");
        StringAssert.Contains(report, "curl -s 'http://plex:32400/library/sections/1/refresh?X-Plex-Token=[redacted]'");
        Assert.IsFalse(report.Contains("a month ago"), "only the last days of the log");
    }

    [TestMethod]
    public async Task Report_LeavesOutSecrets()
    {
        var report = await new DiagnosticsService(new Factory(_options)).BuildReportAsync();

        foreach (var secret in new[] { WebhookKey, RadarrKey, PasswordHash, DiscordHook, "TopSecretWebhookToken", PlexToken })
        {
            Assert.IsFalse(report.Contains(secret), $"the report must not contain {secret}");
        }
    }

    [TestMethod]
    public async Task Report_TellsAReplacedFileFromOneTheConversionDidNotFinish()
    {
        await using (var context = new AppDbContext(_options))
        {
            var profile = await context.Profiles.SingleAsync();
            profile.SubtitleSettings = new TrackSettings { Enabled = true, AllowedLanguages = [IsoLanguage.Find("English")] };
            foreach (var (title, sizeAfter) in new[] { ("Upgraded Film", 1000L), ("Stuck Film", 2000L) })
            {
                var snapshot = new MediaSnapshot
                {
                    Tracks =
                    [
                        new TrackSnapshot { Index = 0, Type = Muxarr.Core.Models.MediaTrackType.Video, Codec = "hevc" },
                        new TrackSnapshot { Index = 1, Type = Muxarr.Core.Models.MediaTrackType.Audio, LanguageName = "English", LanguageCode = "eng", Codec = "eac3" },
                        new TrackSnapshot { Index = 2, Type = Muxarr.Core.Models.MediaTrackType.Subtitles, LanguageName = "English", LanguageCode = "eng", Codec = "srt" },
                        new TrackSnapshot { Index = 3, Type = Muxarr.Core.Models.MediaTrackType.Subtitles, LanguageName = "Czech", LanguageCode = "cze", Codec = "srt" }
                    ]
                };
                var file = new MediaFile
                {
                    Path = $"/data/media/bluray/{title}/{title}.mkv", Title = title, OriginalLanguage = "English",
                    Profile = profile, Snapshot = snapshot, HasRedundantTracks = true, Size = 2000,
                    FileLastWriteTime = DateTime.UtcNow.AddDays(-2)
                };
                context.MediaFiles.Add(file);
                context.MediaConversions.Add(new MediaConversion
                {
                    Name = title, MediaFile = file, State = ConversionState.Completed, SizeAfter = sizeAfter,
                    UpdatedDate = DateTime.UtcNow.AddDays(-1)
                });
            }

            await context.SaveChangesAsync();
        }

        var report = await new DiagnosticsService(new Factory(_options)).BuildReportAsync();

        StringAssert.Contains(report, "Converted, then replaced: 1. Converted and unchanged, yet still needing work: 1.");
        StringAssert.Contains(report, "Would remove now:     s#3 Czech");
        var stuck = report[report.IndexOf("Stuck Film.mkv", StringComparison.Ordinal)..];
        StringAssert.Contains(stuck[..stuck.IndexOf("Original language", StringComparison.Ordinal)], "unchanged, so the conversion did not reach its target");
    }

    [TestMethod]
    public async Task Report_ListsConversionsThatRemovedAnUnlabeledAudioTrack()
    {
        static TrackSnapshot A(int index, string language, string codec) => new()
        {
            Index = index, Type = Muxarr.Core.Models.MediaTrackType.Audio, LanguageName = language,
            LanguageCode = IsoLanguage.Find(language).ThreeLetterCode ?? "und", Codec = codec, AudioChannels = 6
        };

        await using (var context = new AppDbContext(_options))
        {
            var profile = await context.Profiles.SingleAsync();
            void Converted(string title, TrackSnapshot[] before, TrackSnapshot[] after)
            {
                var file = new MediaFile
                {
                    Path = $"/data/media/bluray/{title}/{title}.mkv", Title = title, Profile = profile,
                    Snapshot = new MediaSnapshot { Tracks = after.Select(t => A(t.Index, t.LanguageName, t.Codec)).ToList() }
                };
                context.MediaFiles.Add(file);
                context.MediaConversions.Add(new MediaConversion
                {
                    Name = title, MediaFile = file, State = ConversionState.Completed,
                    BeforeSnapshot = new MediaSnapshot { Tracks = before.ToList() },
                    AfterSnapshot = new MediaSnapshot { Tracks = after.ToList() }
                });
            }

            // Its own AC-3 and DTS, both unlabeled: the DTS went.
            Converted("Surrogates", [A(1, "Undetermined", "Ac3"), A(2, "Undetermined", "Dts")], [A(1, "Undetermined", "Ac3")]);
            // Retagged on the way through, nothing lost.
            Converted("Retagged", [A(1, "Undetermined", "Ac3"), A(2, "German", "Ac3")], [A(1, "English", "Ac3")]);
            // Only a tagged foreign track went.
            Converted("Dub Removed", [A(1, "Undetermined", "Eac3"), A(2, "French", "Ac3")], [A(1, "Undetermined", "Eac3")]);
            await context.SaveChangesAsync();
        }

        var report = await new DiagnosticsService(new Factory(_options)).BuildReportAsync();
        var section = report[report.IndexOf("== Past conversions that removed an unlabeled audio track ==", StringComparison.Ordinal)..];
        section = section[..section.IndexOf("\n== ", 5, StringComparison.Ordinal)];

        StringAssert.Contains(section, "Surrogates");
        StringAssert.Contains(section, "Removed: audio #2 Undetermined DTS 5.1");
        Assert.IsFalse(section.Contains("Retagged"), section);
        Assert.IsFalse(section.Contains("Dub Removed"), section);
        StringAssert.Contains(section, "1 of 3 completed conversions.");
    }

    [TestMethod]
    public async Task Report_ListsRemovedMainAudioAndUnlabeledSubtitles_ByFolder()
    {
        static TrackSnapshot A(int index, string language, string codec, bool isDefault = false, bool original = false) => new()
        {
            Index = index, Type = Muxarr.Core.Models.MediaTrackType.Audio, LanguageName = language,
            LanguageCode = IsoLanguage.Find(language).ThreeLetterCode ?? "und", Codec = codec, AudioChannels = 6,
            IsDefault = isDefault, IsOriginal = original
        };
        static TrackSnapshot C(int index) => new()
        {
            Index = index, Type = Muxarr.Core.Models.MediaTrackType.Audio, LanguageName = "English",
            LanguageCode = "eng", Codec = "Ac3", AudioChannels = 2, IsCommentary = true
        };
        static TrackSnapshot S(int index, string language) => new()
        {
            Index = index, Type = Muxarr.Core.Models.MediaTrackType.Subtitles, LanguageName = language,
            LanguageCode = IsoLanguage.Find(language).ThreeLetterCode ?? "und", Codec = "Srt"
        };

        await using (var context = new AppDbContext(_options))
        {
            var profile = await context.Profiles.SingleAsync();
            void Converted(string relative, string? original, TrackSnapshot[] before, TrackSnapshot[] after)
            {
                var file = new MediaFile
                {
                    Path = $"/data/media/bluray/{relative}", Profile = profile, OriginalLanguage = original,
                    Snapshot = new MediaSnapshot { Tracks = [] }
                };
                context.MediaFiles.Add(file);
                context.MediaConversions.Add(new MediaConversion
                {
                    Name = Path.GetFileNameWithoutExtension(relative), MediaFile = file, State = ConversionState.Completed,
                    BeforeSnapshot = new MediaSnapshot { Tracks = before.ToList() },
                    AfterSnapshot = new MediaSnapshot { Tracks = after.ToList() }
                });
            }

            // Radarr says English, so the film's own French went.
            Converted("Amelie (2001)/Amelie (2001).mkv", "English",
                [A(1, "French", "Dts", isDefault: true), A(2, "English", "Ac3")], [A(1, "English", "Ac3")]);
            Converted("Flagged (2010)/Flagged (2010).mkv", "English",
                [A(1, "English", "Ac3", isDefault: true), A(2, "German", "Ac3", original: true)], [A(1, "English", "Ac3", isDefault: true)]);
            // A plain foreign dub: nothing to report.
            Converted("Normal (2015)/Normal (2015).mkv", "English",
                [A(1, "English", "Ac3", isDefault: true), A(2, "French", "Ac3")], [A(1, "English", "Ac3", isDefault: true)]);
            Converted("Show/Season 1/Show S01E01.mkv", "English",
                [A(1, "English", "Ac3", isDefault: true), S(2, "Undetermined"), S(3, "English")], [A(1, "English", "Ac3", isDefault: true), S(2, "English")]);
            Converted("Show/Season 1/Show S01E02.mkv", "English",
                [A(1, "English", "Ac3", isDefault: true), S(2, "Undetermined"), S(3, "English")], [A(1, "English", "Ac3", isDefault: true), S(2, "English")]);
            // The film went, two commentaries stayed.
            Converted("Dogtown (2005)/Dogtown (2005).mkv", "English",
                [A(1, "Undetermined", "Dts", isDefault: true), C(2), C(3)], [C(1), C(2)]);
            // Its only subtitle was retagged, not removed.
            Converted("Retagged (2020)/Retagged (2020).mkv", "English",
                [A(1, "English", "Ac3", isDefault: true), S(2, "Undetermined")], [A(1, "English", "Ac3", isDefault: true), S(2, "English")]);
            await context.SaveChangesAsync();
        }

        var report = await new DiagnosticsService(new Factory(_options)).BuildReportAsync();
        string SectionOf(string title)
        {
            var s = report[report.IndexOf($"== {title} ==", StringComparison.Ordinal)..];
            return s[..s.IndexOf("\n== ", 5, StringComparison.Ordinal)];
        }

        var main = SectionOf("Past conversions that removed the file's default or original-flagged audio");
        StringAssert.Contains(main, "Amelie (2001): 1 file; Sonarr/Radarr original English; removed French (default); kept English");
        StringAssert.Contains(main, "Flagged (2010): 1 file; Sonarr/Radarr original English; removed German (marked original); kept English");
        Assert.IsTrue(main.IndexOf("marked the removed track as its original", StringComparison.Ordinal)
                      < main.IndexOf("Flagged (2010)", StringComparison.Ordinal), main);
        Assert.IsFalse(main.Contains("Normal (2015)"), main);

        var extras = SectionOf("Past conversions that left only commentary or audio description");
        StringAssert.Contains(extras, "Dogtown");
        StringAssert.Contains(extras, "1 of 7 completed conversions.");

        var subs = SectionOf("Past conversions that removed an unlabeled subtitle");
        StringAssert.Contains(subs, "Show: 2 files; removed SRT x2; kept English");
        Assert.IsFalse(subs.Contains("Retagged"), subs);
    }

    [TestMethod]
    public void FileName_IsDatedText()
    {
        StringAssert.Matches(DiagnosticsService.FileName(DateTime.UtcNow),
            new System.Text.RegularExpressions.Regex(@"^muxarr-diagnostics-\d{4}-\d{2}-\d{2}-\d{4}\.txt$"));
    }

    private sealed class Factory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext()
        {
            return new AppDbContext(options);
        }
    }
}
