using System.Diagnostics;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Muxarr.Core.Models;
using Muxarr.Data;
using Muxarr.Data.Entities;

namespace Muxarr.Tests;

// Excluded from the regular run; invoke explicitly with
//   dotnet test --filter "TestCategory=Stress"
[TestClass]
public class StressMigrationTest : FixtureTestBase
{
    private const string PreSnapshotMigration = "20260409154440_RenamePostProcessingConfig";

    private const int FileCount = 50_000;
    private const int TracksPerFile = 5;
    private const int CompletedConversions = 4_000;
    private const int FailedConversions = 500;
    private const int QueuedNonCustom = 250;
    private const int QueuedCustom = 200;
    private const int ProcessingMixed = 50;

    [TestMethod]
    [TestCategory("Stress")]
    public async Task SnapshotNormalization_HandlesLargeLibrary()
    {
        var dbPath = TempPath("stress.db");
        Console.WriteLine($"DB: {dbPath}");

        var totalConversions = CompletedConversions + FailedConversions
                                                    + QueuedNonCustom + QueuedCustom + ProcessingMixed;

        // Phase 1: seed at the pre-migration schema.
        var seedSw = Stopwatch.StartNew();
        await using (var context = CreateContext(dbPath))
        {
            await Migrate(context, PreSnapshotMigration);
            await SeedAsync(context);
        }

        seedSw.Stop();
        Console.WriteLine($"Seed: {seedSw.Elapsed.TotalSeconds:F1}s "
                          + $"({FileCount:N0} files, {FileCount * TracksPerFile:N0} tracks, {totalConversions:N0} conversions)");

        var dbBytesBefore = new FileInfo(dbPath).Length;
        Console.WriteLine($"DB size before migration: {dbBytesBefore / 1024.0 / 1024.0:F1} MB");

        // Phase 2: apply SnapshotNormalization + everything after it.
        var migrateSw = Stopwatch.StartNew();
        await using (var context = CreateContext(dbPath))
        {
            await Migrate(context, null);
        }

        migrateSw.Stop();
        Console.WriteLine($"Migration: {migrateSw.Elapsed.TotalSeconds:F1}s");

        var dbBytesAfter = new FileInfo(dbPath).Length;
        Console.WriteLine($"DB size after migration: {dbBytesAfter / 1024.0 / 1024.0:F1} MB");

        // Phase 3: verify the result.
        await using (var context = CreateContext(dbPath))
        {
            var fileCount = await context.MediaFiles.CountAsync();
            var snapshotCount = await context.MediaSnapshots.CountAsync();
            var trackSnapshotCount = await context.TrackSnapshots.CountAsync();
            var conversionCount = await context.MediaConversions.CountAsync();

            Console.WriteLine($"After: {fileCount:N0} files, {snapshotCount:N0} snapshots, "
                              + $"{trackSnapshotCount:N0} track snapshots, {conversionCount:N0} conversions");

            Assert.AreEqual(FileCount, fileCount);
            Assert.AreEqual(totalConversions, conversionCount);

            // Each MediaFile gets one snapshot. Each non-empty before/after gets one more.
            // Failed conversions in this seed have a before but no after.
            // Processing conversions have both.
            var expectedConvSnapshots =
                CompletedConversions * 2 +
                FailedConversions * 1 +
                ProcessingMixed * 2;
            Assert.AreEqual(FileCount + expectedConvSnapshots, snapshotCount,
                "snapshot count off (file snapshots + before/after snapshots)");

            // Every file should now be linked to a snapshot.
            var unlinked = await context.MediaFiles.CountAsync(f => f.SnapshotId == null);
            Assert.AreEqual(0, unlinked);

            // Spot-check a custom queued conversion got its plan backfilled.
            var customSample = await context.MediaConversions
                .AsNoTracking()
                .Where(c => c.IsCustomConversion && c.State == ConversionState.New)
                .OrderBy(c => c.Id)
                .FirstAsync();
            Assert.IsTrue(customSample.ConversionPlan.Tracks.Count > 0,
                "queued custom conversion still has empty plan");
            Assert.IsTrue(customSample.ConversionPlan.Tracks.All(t => t.NameLocked));

            // Non-custom queued conversions must NOT have been backfilled
            // (their plans rebuild from profile at runtime).
            var nonCustomSample = await context.MediaConversions
                .AsNoTracking()
                .Where(c => !c.IsCustomConversion && c.State == ConversionState.New)
                .OrderBy(c => c.Id)
                .FirstAsync();
            Assert.AreEqual(0, nonCustomSample.ConversionPlan.Tracks.Count);

            // Historical snapshots are linked.
            var withBefore = await context.MediaConversions.CountAsync(c => c.BeforeSnapshotId != null);
            var withAfter = await context.MediaConversions.CountAsync(c => c.AfterSnapshotId != null);
            Console.WriteLine($"Conversions with before snapshot: {withBefore:N0}");
            Console.WriteLine($"Conversions with after snapshot:  {withAfter:N0}");
            Assert.AreEqual(CompletedConversions + FailedConversions + ProcessingMixed, withBefore);
            Assert.AreEqual(CompletedConversions + ProcessingMixed, withAfter);
        }

        // Write a summary to /tmp/muxarr-stress for posterity.
        var summary = $"""
                       Stress migration run
                       DB path: {dbPath}
                       Files:               {FileCount:N0}
                       Tracks per file:     {TracksPerFile}
                       Total conversions:   {totalConversions:N0}
                         completed:         {CompletedConversions:N0}
                         failed:            {FailedConversions:N0}
                         queued non-custom: {QueuedNonCustom:N0}
                         queued custom:     {QueuedCustom:N0}
                         processing mixed:  {ProcessingMixed:N0}

                       DB size before:      {dbBytesBefore / 1024.0 / 1024.0:F1} MB
                       DB size after:       {dbBytesAfter / 1024.0 / 1024.0:F1} MB
                       Seed time:           {seedSw.Elapsed.TotalSeconds:F1}s
                       Migration time:      {migrateSw.Elapsed.TotalSeconds:F1}s

                       All assertions passed.
                       """;
        try
        {
            Directory.CreateDirectory("/tmp/muxarr-stress");
            await File.WriteAllTextAsync("/tmp/muxarr-stress/result.txt", summary);
        }
        catch
        {
            // best effort
        }
    }

    private static AppDbContext CreateContext(string dbPath)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;
        return new AppDbContext(options);
    }

    private static async Task Migrate(AppDbContext context, string? target)
    {
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(target);
    }

    private static async Task SeedAsync(AppDbContext context)
    {
        var connection = (SqliteConnection)context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
        {
            await connection.OpenAsync();
        }

        await using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA synchronous = OFF; PRAGMA journal_mode = MEMORY;";
            await pragma.ExecuteNonQueryAsync();
        }

        await using var tx = (SqliteTransaction)await connection.BeginTransactionAsync();

        // Profile.
        await using (var cmd = connection.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                              INSERT INTO Profile
                                  (Id, Name, Directories, ClearVideoTrackNames, AudioSettings, SubtitleSettings,
                                   SkipHardlinkedFiles, CreatedDate, UpdatedDate)
                              VALUES
                                  (1, 'stress', '[]', 0, '{}', '{}', 0, '2026-04-01 00:00:00', '2026-04-01 00:00:00')
                              """;
            await cmd.ExecuteNonQueryAsync();
        }

        // MediaFile + MediaTrack.
        await using (var fileCmd = connection.CreateCommand())
        await using (var trackCmd = connection.CreateCommand())
        {
            fileCmd.Transaction = tx;
            fileCmd.CommandText = """
                                  INSERT INTO MediaFile
                                      (Id, ProfileId, Path, Size, ProbeOutput, TrackCount,
                                       HasRedundantTracks, HasNonStandardMetadata, HasScanWarning,
                                       ContainerType, Resolution, DurationMs, VideoBitDepth, HasFaststart,
                                       FileLastWriteTime, FileCreationTime, CreatedDate, UpdatedDate)
                                  VALUES
                                      ($id, 1, $path, $size, '', $trackCount, 0, 0, 0,
                                       $container, $resolution, $duration, 10, 0,
                                       '2026-04-01 00:00:00', '2026-04-01 00:00:00',
                                       '2026-04-01 00:00:00', '2026-04-01 00:00:00')
                                  """;
            var fId = fileCmd.Parameters.Add("$id", SqliteType.Integer);
            var fPath = fileCmd.Parameters.Add("$path", SqliteType.Text);
            var fSize = fileCmd.Parameters.Add("$size", SqliteType.Integer);
            var fTrackCount = fileCmd.Parameters.Add("$trackCount", SqliteType.Integer);
            var fContainer = fileCmd.Parameters.Add("$container", SqliteType.Text);
            var fResolution = fileCmd.Parameters.Add("$resolution", SqliteType.Text);
            var fDuration = fileCmd.Parameters.Add("$duration", SqliteType.Integer);

            trackCmd.Transaction = tx;
            trackCmd.CommandText = """
                                   INSERT INTO MediaTrack
                                       (Id, MediaFileId, TrackNumber, Type, IsCommentary, IsHearingImpaired,
                                        IsVisualImpaired, IsDefault, IsForced, IsOriginal,
                                        Codec, AudioChannels, LanguageCode, LanguageName, TrackName)
                                   VALUES
                                       ($id, $fileId, $trackNum, $type, $comm, $hi, $vi, $def, $forced, $orig,
                                        $codec, $channels, $langCode, $langName, $name)
                                   """;
            var tId = trackCmd.Parameters.Add("$id", SqliteType.Integer);
            var tFileId = trackCmd.Parameters.Add("$fileId", SqliteType.Integer);
            var tNum = trackCmd.Parameters.Add("$trackNum", SqliteType.Integer);
            var tType = trackCmd.Parameters.Add("$type", SqliteType.Text);
            var tComm = trackCmd.Parameters.Add("$comm", SqliteType.Integer);
            var tHi = trackCmd.Parameters.Add("$hi", SqliteType.Integer);
            var tVi = trackCmd.Parameters.Add("$vi", SqliteType.Integer);
            var tDef = trackCmd.Parameters.Add("$def", SqliteType.Integer);
            var tForced = trackCmd.Parameters.Add("$forced", SqliteType.Integer);
            var tOrig = trackCmd.Parameters.Add("$orig", SqliteType.Integer);
            var tCodec = trackCmd.Parameters.Add("$codec", SqliteType.Text);
            var tChannels = trackCmd.Parameters.Add("$channels", SqliteType.Integer);
            var tLangCode = trackCmd.Parameters.Add("$langCode", SqliteType.Text);
            var tLangName = trackCmd.Parameters.Add("$langName", SqliteType.Text);
            var tName = trackCmd.Parameters.Add("$name", SqliteType.Text);

            var trackId = 1;
            for (var i = 1; i <= FileCount; i++)
            {
                fId.Value = i;
                fPath.Value = $"/media/show/episode-{i:D6}.mkv";
                fSize.Value = 500_000_000L + i;
                fTrackCount.Value = TracksPerFile;
                fContainer.Value = i % 2 == 0 ? "Matroska" : "MP4/QuickTime";
                fResolution.Value = i % 3 == 0 ? "3840x2160" : "1920x1080";
                fDuration.Value = 1_200_000L + i * 13L % 5_400_000L;
                await fileCmd.ExecuteNonQueryAsync();

                // 1 video, 2 audio, 2 subtitle per file.
                for (var t = 0; t < TracksPerFile; t++)
                {
                    tId.Value = trackId++;
                    tFileId.Value = i;
                    tNum.Value = t;
                    if (t == 0)
                    {
                        tType.Value = "Video";
                        tCodec.Value = "H264";
                        tChannels.Value = 0;
                        tLangCode.Value = "und";
                        tLangName.Value = "Undetermined";
                        tName.Value = DBNull.Value;
                        tDef.Value = 1;
                        tOrig.Value = 0;
                    }
                    else if (t <= 2)
                    {
                        tType.Value = "Audio";
                        tCodec.Value = t == 1 ? "Aac" : "Eac3";
                        tChannels.Value = t == 1 ? 2 : 6;
                        tLangCode.Value = t == 1 ? "eng" : "jpn";
                        tLangName.Value = t == 1 ? "English" : "Japanese";
                        tName.Value = t == 1 ? "English Stereo" : "Japanese Surround";
                        tDef.Value = t == 1 ? 1 : 0;
                        tOrig.Value = t == 2 ? 1 : 0;
                    }
                    else
                    {
                        tType.Value = "Subtitles";
                        tCodec.Value = "SubRip";
                        tChannels.Value = 0;
                        tLangCode.Value = t == 3 ? "eng" : "spa";
                        tLangName.Value = t == 3 ? "English" : "Spanish";
                        tName.Value = t == 3 ? "English" : "Spanish";
                        tDef.Value = 0;
                        tOrig.Value = 0;
                    }

                    tComm.Value = 0;
                    tHi.Value = 0;
                    tVi.Value = 0;
                    tForced.Value = 0;
                    await trackCmd.ExecuteNonQueryAsync();
                }
            }
        }

        // MediaConversion: realistic JSON in TracksBefore/After/AllowedTracks.
        // Match the v0.8.1 TrackSnapshot shape exactly: JsonPropertyName("Id") on TrackNumber.
        var sampleTracks = BuildSampleTracksJson();

        await using (var convCmd = connection.CreateCommand())
        {
            convCmd.Transaction = tx;
            convCmd.CommandText = """
                                  INSERT INTO MediaConversion
                                      (Id, MediaFileId, Name, Log, Progress, SizeBefore, SizeAfter, SizeDifference,
                                       TracksBefore, TracksAfter, AllowedTracks, IsCustomConversion, State,
                                       CreatedDate, UpdatedDate)
                                  VALUES
                                      ($id, $fileId, $name, '', $progress, $sizeBefore, $sizeAfter, $sizeDiff,
                                       $tracksBefore, $tracksAfter, $allowedTracks, $isCustom, $state,
                                       '2026-04-01 00:00:00', '2026-04-01 00:00:00')
                                  """;
            var cId = convCmd.Parameters.Add("$id", SqliteType.Integer);
            var cFileId = convCmd.Parameters.Add("$fileId", SqliteType.Integer);
            var cName = convCmd.Parameters.Add("$name", SqliteType.Text);
            var cProgress = convCmd.Parameters.Add("$progress", SqliteType.Integer);
            var cSizeBefore = convCmd.Parameters.Add("$sizeBefore", SqliteType.Integer);
            var cSizeAfter = convCmd.Parameters.Add("$sizeAfter", SqliteType.Integer);
            var cSizeDiff = convCmd.Parameters.Add("$sizeDiff", SqliteType.Integer);
            var cTracksBefore = convCmd.Parameters.Add("$tracksBefore", SqliteType.Text);
            var cTracksAfter = convCmd.Parameters.Add("$tracksAfter", SqliteType.Text);
            var cAllowedTracks = convCmd.Parameters.Add("$allowedTracks", SqliteType.Text);
            var cIsCustom = convCmd.Parameters.Add("$isCustom", SqliteType.Integer);
            var cState = convCmd.Parameters.Add("$state", SqliteType.Text);

            var convId = 1;

            // Completed: full before + after JSON.
            for (var i = 0; i < CompletedConversions; i++, convId++)
            {
                cId.Value = convId;
                cFileId.Value = i % FileCount + 1;
                cName.Value = $"completed-{convId}.mkv";
                cProgress.Value = 100;
                cSizeBefore.Value = 1_000_000_000L;
                cSizeAfter.Value = 700_000_000L;
                cSizeDiff.Value = -300_000_000L;
                cTracksBefore.Value = sampleTracks.FullBefore;
                cTracksAfter.Value = sampleTracks.FullAfter;
                cAllowedTracks.Value = "[]";
                cIsCustom.Value = 0;
                cState.Value = "Completed";
                await convCmd.ExecuteNonQueryAsync();
            }

            // Failed: before but no after.
            for (var i = 0; i < FailedConversions; i++, convId++)
            {
                cId.Value = convId;
                cFileId.Value = i % FileCount + 1;
                cName.Value = $"failed-{convId}.mkv";
                cProgress.Value = 0;
                cSizeBefore.Value = 1_000_000_000L;
                cSizeAfter.Value = 0;
                cSizeDiff.Value = 0;
                cTracksBefore.Value = sampleTracks.FullBefore;
                cTracksAfter.Value = "[]";
                cAllowedTracks.Value = "[]";
                cIsCustom.Value = 0;
                cState.Value = "Failed";
                await convCmd.ExecuteNonQueryAsync();
            }

            // Queued non-custom: empty before/after, empty allowed (rebuilt at runtime).
            for (var i = 0; i < QueuedNonCustom; i++, convId++)
            {
                cId.Value = convId;
                cFileId.Value = i % FileCount + 1;
                cName.Value = $"queued-{convId}.mkv";
                cProgress.Value = 0;
                cSizeBefore.Value = 0;
                cSizeAfter.Value = 0;
                cSizeDiff.Value = 0;
                cTracksBefore.Value = "[]";
                cTracksAfter.Value = "[]";
                cAllowedTracks.Value = "[]";
                cIsCustom.Value = 0;
                cState.Value = "New";
                await convCmd.ExecuteNonQueryAsync();
            }

            // Queued custom: AllowedTracks holds the user's plan; ConversionPlan
            // is the empty default until the migration backfills it.
            for (var i = 0; i < QueuedCustom; i++, convId++)
            {
                cId.Value = convId;
                cFileId.Value = i % FileCount + 1;
                cName.Value = $"custom-{convId}.mkv";
                cProgress.Value = 0;
                cSizeBefore.Value = 0;
                cSizeAfter.Value = 0;
                cSizeDiff.Value = 0;
                cTracksBefore.Value = "[]";
                cTracksAfter.Value = "[]";
                cAllowedTracks.Value = sampleTracks.CustomAllowed;
                cIsCustom.Value = 1;
                cState.Value = "New";
                await convCmd.ExecuteNonQueryAsync();
            }

            // Processing: mid-flight. Mix custom/non-custom.
            for (var i = 0; i < ProcessingMixed; i++, convId++)
            {
                cId.Value = convId;
                cFileId.Value = i % FileCount + 1;
                cName.Value = $"processing-{convId}.mkv";
                cProgress.Value = 50;
                cSizeBefore.Value = 1_000_000_000L;
                cSizeAfter.Value = 0;
                cSizeDiff.Value = 0;
                cTracksBefore.Value = sampleTracks.FullBefore;
                cTracksAfter.Value = sampleTracks.FullAfter;
                cAllowedTracks.Value = i % 2 == 0 ? sampleTracks.CustomAllowed : "[]";
                cIsCustom.Value = i % 2 == 0 ? 1 : 0;
                cState.Value = "Processing";
                await convCmd.ExecuteNonQueryAsync();
            }
        }

        await tx.CommitAsync();
    }

    private record SampleTracks(string FullBefore, string FullAfter, string CustomAllowed);

    // Mirrors the v0.8.1 TrackSnapshot JSON shape: top-level "Id" maps to the
    // C# TrackNumber field, plain "Type" string, no IsDub field.
    private static SampleTracks BuildSampleTracksJson()
    {
        var before = new StringBuilder("[");
        before.Append(Track(0, "Video", "H264", langCode: "und", langName: "Undetermined", isDefault: true));
        before.Append(',').Append(Track(1, "Audio", "Aac", 2, "eng", "English", true, name: "English"));
        before.Append(',').Append(Track(2, "Audio", "Eac3", 6, "jpn", "Japanese", isOriginal: true, name: "Japanese"));
        before.Append(',').Append(Track(3, "Subtitles", "SubRip", langCode: "eng", langName: "English", name: "English"));
        before.Append(',').Append(Track(4, "Subtitles", "SubRip", langCode: "spa", langName: "Spanish", name: "Spanish"));
        before.Append(']');

        var after = new StringBuilder("[");
        after.Append(Track(0, "Video", "H264", langCode: "und", langName: "Undetermined", isDefault: true));
        after.Append(',').Append(Track(1, "Audio", "Aac", 2, "eng", "English", true, name: "English"));
        after.Append(',').Append(Track(2, "Subtitles", "SubRip", langCode: "eng", langName: "English", name: "English"));
        after.Append(']');

        var custom = new StringBuilder("[");
        custom.Append(Track(0, "Video", "H264", langCode: "und", langName: "Undetermined",
            isDefault: true, name: "User Video Title"));
        custom.Append(',').Append(Track(1, "Audio", "Aac", 2, "eng", "English",
            true, isOriginal: true, name: "User Audio Title"));
        custom.Append(',').Append(Track(2, "Subtitles", "SubRip", langCode: "eng", langName: "English",
            isHi: true, name: "User Sub Title"));
        custom.Append(']');

        return new SampleTracks(before.ToString(), after.ToString(), custom.ToString());
    }

    private static string Track(int id, string type, string codec, int channels = 0,
        string langCode = "und", string langName = "Undetermined",
        bool isDefault = false, bool isForced = false, bool isOriginal = false,
        bool isHi = false, bool isVi = false, bool isComm = false,
        string? name = null)
    {
        var nameJson = name == null ? "null" : $"\"{name}\"";
        return $$"""
                 {"Id":{{id}},"Type":"{{type}}","IsCommentary":{{B(isComm)}},"IsHearingImpaired":{{B(isHi)}},"IsVisualImpaired":{{B(isVi)}},"IsDefault":{{B(isDefault)}},"IsForced":{{B(isForced)}},"IsOriginal":{{B(isOriginal)}},"Codec":"{{codec}}","AudioChannels":{{channels}},"LanguageCode":"{{langCode}}","LanguageName":"{{langName}}","TrackName":{{nameJson}}}
                 """;
    }

    private static string B(bool b)
    {
        return b ? "true" : "false";
    }
}
