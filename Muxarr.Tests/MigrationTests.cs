using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Muxarr.Core.Config;
using Muxarr.Core.Models;
using Muxarr.Data;
using Muxarr.Data.Extensions;
using Muxarr.Data.Entities;

namespace Muxarr.Tests;

[TestClass]
public class MigrationTests : FixtureTestBase
{
    // Last migration before SnapshotNormalization. Tests seed against this schema.
    private const string PreSnapshotMigration = "20260409154440_RenamePostProcessingConfig";

    private AppDbContext CreateContext(string dbPath)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;
        return new AppDbContext(options);
    }

    private static async Task MigrateAsync(AppDbContext context, string? target)
    {
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(target);
    }

    // Bypasses EF's string.Format pass so '{' in JSON seed data is not treated as a placeholder.
    private static async Task ExecAsync(AppDbContext context, string sql)
    {
        var connection = context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync();
    }

    private static Task SeedProfileAsync(AppDbContext context)
    {
        return ExecAsync(context, """
                                  INSERT INTO Profile
                                      (Id, Name, Directories, ClearVideoTrackNames, AudioSettings, SubtitleSettings,
                                       SkipHardlinkedFiles, CreatedDate, UpdatedDate)
                                  VALUES
                                      (1, 'test', '[]', 0, '{}', '{}', 0, '2026-04-01 00:00:00', '2026-04-01 00:00:00')
                                  """);
    }

    private static Task SeedMediaFileAsync(AppDbContext context, int id, string path,
        string container = "Matroska", string resolution = "1920x1080", long durationMs = 3600000)
    {
        return ExecAsync(context, $"""
                                   INSERT INTO MediaFile
                                       (Id, ProfileId, Path, Size, ProbeOutput, TrackCount,
                                        HasRedundantTracks, HasNonStandardMetadata, HasScanWarning,
                                        ContainerType, Resolution, DurationMs, VideoBitDepth, HasFaststart,
                                        FileLastWriteTime, FileCreationTime, CreatedDate, UpdatedDate)
                                   VALUES
                                       ({id}, 1, '{path}', 12345, '', 2,
                                        0, 0, 0,
                                        '{container}', '{resolution}', {durationMs}, 10, 0,
                                        '2026-04-01 00:00:00', '2026-04-01 00:00:00',
                                        '2026-04-01 00:00:00', '2026-04-01 00:00:00')
                                   """);
    }

    [TestMethod]
    public async Task Initialize_CopiesLegacyDatabaseToConfigAndArchivesOriginal()
    {
        var configDbPath = TempPath("config/muxarr.db");
        var previousDbPath = TempPath("config/muxarr-previous.db");
        var legacyDbPath = TempPath("data/muxarr.db");

        Directory.CreateDirectory(Path.GetDirectoryName(configDbPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(legacyDbPath)!);

        await using (var legacyContext = CreateContext(legacyDbPath))
        {
            await legacyContext.Database.MigrateAsync();
            legacyContext.Configs.Set(new SetupConfig
            {
                CompletedAt = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc)
            });
            await legacyContext.SaveChangesAsync();
        }

        await using (var newContext = CreateContext(configDbPath))
        {
            await newContext.Initialize();
        }

        Assert.IsTrue(File.Exists(configDbPath));
        Assert.IsTrue(File.Exists(previousDbPath));
        Assert.IsFalse(File.Exists(legacyDbPath));

        await using (var verifyContext = CreateContext(configDbPath))
        {
            var setup = await verifyContext.Configs.GetAsync<SetupConfig>();
            Assert.IsNotNull(setup);
            Assert.AreEqual(new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc), setup.CompletedAt);
        }

        await using (var previousContext = CreateContext(previousDbPath))
        {
            var setup = await previousContext.Configs.GetAsync<SetupConfig>();
            Assert.IsNotNull(setup);
            Assert.AreEqual(new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc), setup.CompletedAt);
        }
    }

    [TestMethod]
    public async Task SnapshotNormalization_BackfillsCustomQueuedConversionPlan()
    {
        var dbPath = TempPath("custom-plan.db");

        await using (var context = CreateContext(dbPath))
        {
            await MigrateAsync(context, PreSnapshotMigration);
            await SeedProfileAsync(context);
            await SeedMediaFileAsync(context, 1, "/m/show.mkv");
            await ExecAsync(context, """
                                     INSERT INTO MediaConversion
                                         (Id, MediaFileId, Name, Log, Progress, SizeBefore, SizeAfter, SizeDifference,
                                          TracksBefore, TracksAfter, AllowedTracks, IsCustomConversion, State,
                                          CreatedDate, UpdatedDate)
                                     VALUES
                                         (1, 1, 'show.mkv', '', 0, 0, 0, 0,
                                          '[]', '[]',
                                          '[
                                             {"Id":0,"Type":"Video","IsCommentary":false,"IsHearingImpaired":false,"IsVisualImpaired":false,"IsDefault":true,"IsForced":false,"IsOriginal":false,"Codec":"H264","AudioChannels":0,"LanguageCode":"und","LanguageName":"Undetermined","TrackName":"Custom Video Name"},
                                             {"Id":1,"Type":"Audio","IsCommentary":false,"IsHearingImpaired":false,"IsVisualImpaired":false,"IsDefault":true,"IsForced":false,"IsOriginal":true,"Codec":"Aac","AudioChannels":2,"LanguageCode":"eng","LanguageName":"English","TrackName":"English Stereo"},
                                             {"Id":2,"Type":"Subtitles","IsCommentary":false,"IsHearingImpaired":true,"IsVisualImpaired":false,"IsDefault":false,"IsForced":false,"IsOriginal":false,"Codec":"SubRip","AudioChannels":0,"LanguageCode":"eng","LanguageName":"English","TrackName":"English SDH"}
                                          ]',
                                          1, 'New', '2026-04-01 00:00:00', '2026-04-01 00:00:00')
                                     """);
            await MigrateAsync(context, null);
        }

        await using (var context = CreateContext(dbPath))
        {
            var conversion = await context.MediaConversions.AsNoTracking().SingleAsync();
            Assert.IsNotNull(conversion.ConversionPlan);
            Assert.AreEqual(3, conversion.ConversionPlan.Tracks.Count);

            var video = conversion.ConversionPlan.Tracks.Single(t => t.Type == MediaTrackType.Video);
            Assert.AreEqual(0, video.Index);
            Assert.AreEqual("Custom Video Name", video.Name);
            Assert.AreEqual("und", video.LanguageCode);
            Assert.AreEqual(true, video.IsDefault);
            Assert.AreEqual(false, video.IsOriginal);
            Assert.IsTrue(video.NameLocked);

            var audio = conversion.ConversionPlan.Tracks.Single(t => t.Type == MediaTrackType.Audio);
            Assert.AreEqual(1, audio.Index);
            Assert.AreEqual("English Stereo", audio.Name);
            Assert.AreEqual("eng", audio.LanguageCode);
            Assert.AreEqual(true, audio.IsDefault);
            Assert.AreEqual(true, audio.IsOriginal);
            Assert.AreEqual(false, audio.IsForced);
            Assert.AreEqual(false, audio.IsCommentary);

            var sub = conversion.ConversionPlan.Tracks.Single(t => t.Type == MediaTrackType.Subtitles);
            Assert.AreEqual(2, sub.Index);
            Assert.AreEqual("English SDH", sub.Name);
            Assert.AreEqual(true, sub.IsHearingImpaired);
            Assert.AreEqual(false, sub.IsDefault);

            Assert.IsNull(conversion.ConversionPlan.HasChapters);
            Assert.IsNull(conversion.ConversionPlan.HasAttachments);
            Assert.IsNull(conversion.ConversionPlan.Faststart);
        }
    }

    [TestMethod]
    public async Task SnapshotNormalization_DoesNotBackfillCompletedCustomConversions()
    {
        var dbPath = TempPath("custom-completed.db");

        await using (var context = CreateContext(dbPath))
        {
            await MigrateAsync(context, PreSnapshotMigration);
            await SeedProfileAsync(context);
            await SeedMediaFileAsync(context, 1, "/m/done.mkv");
            await ExecAsync(context, """
                                     INSERT INTO MediaConversion
                                         (Id, MediaFileId, Name, Log, Progress, SizeBefore, SizeAfter, SizeDifference,
                                          TracksBefore, TracksAfter, AllowedTracks, IsCustomConversion, State,
                                          CreatedDate, UpdatedDate)
                                     VALUES
                                         (1, 1, 'done.mkv', '', 100, 0, 0, 0,
                                          '[]', '[]',
                                          '[{"Id":0,"Type":"Video","Codec":"H264","TrackName":"x"}]',
                                          1, 'Completed', '2026-04-01 00:00:00', '2026-04-01 00:00:00')
                                     """);
            await MigrateAsync(context, null);
        }

        await using (var context = CreateContext(dbPath))
        {
            var conversion = await context.MediaConversions.AsNoTracking().SingleAsync();
            Assert.IsNotNull(conversion.ConversionPlan);
            Assert.AreEqual(0, conversion.ConversionPlan.Tracks.Count);
        }
    }

    [TestMethod]
    public async Task SnapshotNormalization_LeavesNonCustomConversionPlansEmpty()
    {
        var dbPath = TempPath("noncustom.db");

        await using (var context = CreateContext(dbPath))
        {
            await MigrateAsync(context, PreSnapshotMigration);
            await SeedProfileAsync(context);
            await SeedMediaFileAsync(context, 1, "/m/auto.mkv");
            await ExecAsync(context, """
                                     INSERT INTO MediaConversion
                                         (Id, MediaFileId, Name, Log, Progress, SizeBefore, SizeAfter, SizeDifference,
                                          TracksBefore, TracksAfter, AllowedTracks, IsCustomConversion, State,
                                          CreatedDate, UpdatedDate)
                                     VALUES
                                         (1, 1, 'auto.mkv', '', 0, 0, 0, 0,
                                          '[]', '[]',
                                          '[{"Id":0,"Type":"Video","Codec":"H264"}]',
                                          0, 'New', '2026-04-01 00:00:00', '2026-04-01 00:00:00')
                                     """);
            await MigrateAsync(context, null);
        }

        await using (var context = CreateContext(dbPath))
        {
            var conversion = await context.MediaConversions.AsNoTracking().SingleAsync();
            Assert.IsNotNull(conversion.ConversionPlan);
            Assert.AreEqual(0, conversion.ConversionPlan.Tracks.Count);
        }
    }

    [TestMethod]
    public async Task SnapshotNormalization_BuildsMediaSnapshotFromMediaFile()
    {
        var dbPath = TempPath("media-snapshot.db");

        await using (var context = CreateContext(dbPath))
        {
            await MigrateAsync(context, PreSnapshotMigration);
            await SeedProfileAsync(context);
            await SeedMediaFileAsync(context, 1, "/m/movie.mkv",
                "Matroska", "3840x2160", 7200000);
            await ExecAsync(context, """
                                     INSERT INTO MediaTrack
                                         (Id, MediaFileId, TrackNumber, Type, IsCommentary, IsHearingImpaired,
                                          IsVisualImpaired, IsDefault, IsForced, IsOriginal,
                                          Codec, AudioChannels, LanguageCode, LanguageName, TrackName)
                                     VALUES
                                         (1, 1, 0, 'Video', 0, 0, 0, 1, 0, 0, 'H264', 0, 'und', 'Undetermined', NULL),
                                         (2, 1, 1, 'Audio', 0, 0, 0, 1, 0, 1, 'Aac', 6, 'eng', 'English', '5.1 Audio');
                                     """);
            await MigrateAsync(context, null);
        }

        await using (var context = CreateContext(dbPath))
        {
            var file = await context.MediaFiles.AsNoTracking().SingleAsync();
            Assert.IsNotNull(file.SnapshotId);

            var snapshot = await context.MediaSnapshots.AsNoTracking()
                .Include(s => s.Tracks)
                .SingleAsync(s => s.Id == file.SnapshotId);

            Assert.AreEqual("Matroska", snapshot.ContainerType);
            Assert.AreEqual("3840x2160", snapshot.Resolution);
            Assert.AreEqual(7200000, snapshot.DurationMs);
            Assert.AreEqual(10, snapshot.VideoBitDepth);
            Assert.AreEqual(2, snapshot.Tracks.Count);

            var video = snapshot.Tracks.Single(t => t.Type == MediaTrackType.Video);
            Assert.AreEqual(0, video.Index);
            Assert.AreEqual("H264", video.Codec);
            Assert.IsTrue(video.IsDefault);

            var audio = snapshot.Tracks.Single(t => t.Type == MediaTrackType.Audio);
            Assert.AreEqual("5.1 Audio", audio.Name);
            Assert.AreEqual(6, audio.AudioChannels);
            Assert.AreEqual("eng", audio.LanguageCode);
            Assert.IsTrue(audio.IsOriginal);
        }
    }

    [TestMethod]
    public async Task SnapshotNormalization_ReconstructsHistoricalSnapshotsFromJson()
    {
        var dbPath = TempPath("historical.db");

        await using (var context = CreateContext(dbPath))
        {
            await MigrateAsync(context, PreSnapshotMigration);
            await SeedProfileAsync(context);
            await SeedMediaFileAsync(context, 1, "/m/old.mkv");
            await ExecAsync(context, """
                                     INSERT INTO MediaConversion
                                         (Id, MediaFileId, Name, Log, Progress, SizeBefore, SizeAfter, SizeDifference,
                                          TracksBefore, TracksAfter, AllowedTracks, IsCustomConversion, State,
                                          CreatedDate, UpdatedDate)
                                     VALUES
                                         (1, 1, 'old.mkv', '', 100, 1000, 800, -200,
                                          '[{"Id":0,"Type":"Video","Codec":"H264","LanguageCode":"und","LanguageName":"Undetermined","TrackName":null},
                                            {"Id":1,"Type":"Audio","Codec":"Dts","LanguageCode":"eng","LanguageName":"English","AudioChannels":6,"TrackName":"Original"}]',
                                          '[{"Id":0,"Type":"Video","Codec":"H264","LanguageCode":"und","LanguageName":"Undetermined","TrackName":null},
                                            {"Id":1,"Type":"Audio","Codec":"Aac","LanguageCode":"eng","LanguageName":"English","AudioChannels":2,"TrackName":"Stereo"}]',
                                          '[]',
                                          0, 'Completed', '2026-04-01 00:00:00', '2026-04-01 01:00:00')
                                     """);
            await MigrateAsync(context, null);
        }

        await using (var context = CreateContext(dbPath))
        {
            var conversion = await context.MediaConversions.AsNoTracking()
                .Include(c => c.BeforeSnapshot!).ThenInclude(s => s.Tracks)
                .Include(c => c.AfterSnapshot!).ThenInclude(s => s.Tracks)
                .SingleAsync();

            Assert.IsNotNull(conversion.BeforeSnapshot);
            Assert.IsNotNull(conversion.AfterSnapshot);
            Assert.AreNotEqual(conversion.BeforeSnapshotId, conversion.AfterSnapshotId);

            Assert.AreEqual(2, conversion.BeforeSnapshot.Tracks.Count);
            Assert.AreEqual(2, conversion.BeforeSnapshot.TrackCount);
            var beforeAudio = conversion.BeforeSnapshot.Tracks.Single(t => t.Type == MediaTrackType.Audio);
            Assert.AreEqual("Dts", beforeAudio.Codec);
            Assert.AreEqual(6, beforeAudio.AudioChannels);

            Assert.AreEqual(2, conversion.AfterSnapshot.Tracks.Count);
            var afterAudio = conversion.AfterSnapshot.Tracks.Single(t => t.Type == MediaTrackType.Audio);
            Assert.AreEqual("Aac", afterAudio.Codec);
            Assert.AreEqual(2, afterAudio.AudioChannels);
        }
    }

    [TestMethod]
    public async Task SnapshotNormalization_TolleratesMalformedHistoricalJson()
    {
        var dbPath = TempPath("malformed.db");

        await using (var context = CreateContext(dbPath))
        {
            await MigrateAsync(context, PreSnapshotMigration);
            await SeedProfileAsync(context);
            await SeedMediaFileAsync(context, 1, "/m/broken.mkv");
            // Two broken rows plus one valid; migration must skip the bad ones and keep going.
            await ExecAsync(context, """
                                     INSERT INTO MediaConversion
                                         (Id, MediaFileId, Name, Log, Progress, SizeBefore, SizeAfter, SizeDifference,
                                          TracksBefore, TracksAfter, AllowedTracks, IsCustomConversion, State,
                                          CreatedDate, UpdatedDate)
                                     VALUES
                                         (1, 1, 'broken.mkv', '', 100, 0, 0, 0,
                                          'not json at all', '{"oops":"object not array"}', '[]',
                                          0, 'Failed', '2026-04-01 00:00:00', '2026-04-01 00:00:00'),
                                         (2, 1, 'ok.mkv', '', 100, 0, 0, 0,
                                          '[{"Id":0,"Type":"Video","Codec":"H264","LanguageCode":"und","LanguageName":"Undetermined"}]',
                                          '[]', '[]',
                                          0, 'Completed', '2026-04-01 00:00:00', '2026-04-01 00:00:00');
                                     """);
            await MigrateAsync(context, null);
        }

        await using (var context = CreateContext(dbPath))
        {
            var broken = await context.MediaConversions.AsNoTracking()
                .SingleAsync(c => c.Id == 1);
            Assert.IsNull(broken.BeforeSnapshotId);
            Assert.IsNull(broken.AfterSnapshotId);

            var ok = await context.MediaConversions.AsNoTracking()
                .Include(c => c.BeforeSnapshot!).ThenInclude(s => s.Tracks)
                .SingleAsync(c => c.Id == 2);
            Assert.IsNotNull(ok.BeforeSnapshot);
            Assert.AreEqual(1, ok.BeforeSnapshot.Tracks.Count);
        }
    }

    [TestMethod]
    public async Task SnapshotNormalization_DropsOldSchema()
    {
        var dbPath = TempPath("schema.db");

        await using var context = CreateContext(dbPath);
        await MigrateAsync(context, PreSnapshotMigration);
        await SeedProfileAsync(context);
        await SeedMediaFileAsync(context, 1, "/m/a.mkv");
        await MigrateAsync(context, null);

        var tableCount = (long)(await ScalarAsync(context,
            "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='MediaTrack'") ?? 0L);
        Assert.AreEqual(0L, tableCount);

        await AssertColumnAbsent(context, "MediaFile", "ContainerType");
        await AssertColumnAbsent(context, "MediaFile", "Resolution");
        await AssertColumnAbsent(context, "MediaFile", "DurationMs");
        await AssertColumnAbsent(context, "MediaFile", "VideoBitDepth");
        await AssertColumnAbsent(context, "MediaFile", "TrackCount");
        await AssertColumnAbsent(context, "MediaFile", "HasFaststart");

        await AssertColumnAbsent(context, "MediaConversion", "AllowedTracks");
        await AssertColumnAbsent(context, "MediaConversion", "TracksBefore");
        await AssertColumnAbsent(context, "MediaConversion", "TracksAfter");

        await AssertColumnPresent(context, "MediaSnapshot", "ContainerType");
        await AssertColumnPresent(context, "TrackSnapshot", "SnapshotId");
        await AssertColumnPresent(context, "MediaConversion", "ConversionPlan");
        await AssertColumnPresent(context, "MediaFile", "SnapshotId");
        await AssertColumnPresent(context, "MediaFile", "IsHardlinked");

        await AssertColumnAbsent(context, "MediaSnapshot", "_TempFileId");
        await AssertColumnAbsent(context, "MediaSnapshot", "_TempConvBeforeId");
        await AssertColumnAbsent(context, "MediaSnapshot", "_TempConvAfterId");
    }

    private static async Task<object?> ScalarAsync(AppDbContext context, string sql)
    {
        var connection = context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        return await cmd.ExecuteScalarAsync();
    }

    private static async Task AssertColumnAbsent(AppDbContext context, string table, string column)
    {
        var count = (long)(await ScalarAsync(context,
            $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name='{column}'") ?? 0L);
        Assert.AreEqual(0L, count, $"{table}.{column} still present");
    }

    private static async Task AssertColumnPresent(AppDbContext context, string table, string column)
    {
        var count = (long)(await ScalarAsync(context,
            $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name='{column}'") ?? 0L);
        Assert.AreEqual(1L, count, $"{table}.{column} missing");
    }
}
