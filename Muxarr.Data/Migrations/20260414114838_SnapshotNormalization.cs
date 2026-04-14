using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Muxarr.Data.Migrations
{
    /// <inheritdoc />
    public partial class SnapshotNormalization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. New tables.
            migrationBuilder.CreateTable(
                name: "MediaSnapshot",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CapturedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ContainerType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Resolution = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    DurationMs = table.Column<long>(type: "INTEGER", nullable: false),
                    VideoBitDepth = table.Column<int>(type: "INTEGER", nullable: false),
                    TrackCount = table.Column<int>(type: "INTEGER", nullable: false),
                    HasChapters = table.Column<bool>(type: "INTEGER", nullable: false),
                    HasAttachments = table.Column<bool>(type: "INTEGER", nullable: false),
                    HasFaststart = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaSnapshot", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TrackSnapshot",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SnapshotId = table.Column<int>(type: "INTEGER", nullable: false),
                    Index = table.Column<int>(type: "INTEGER", nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    IsCommentary = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsHearingImpaired = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsVisualImpaired = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsDefault = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsForced = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsOriginal = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsDub = table.Column<bool>(type: "INTEGER", nullable: false),
                    Codec = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    AudioChannels = table.Column<int>(type: "INTEGER", nullable: false),
                    LanguageCode = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    LanguageName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    DurationMs = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackSnapshot", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrackSnapshot_MediaSnapshot_SnapshotId",
                        column: x => x.SnapshotId,
                        principalTable: "MediaSnapshot",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // 2. New FK columns on existing tables.
            migrationBuilder.AddColumn<int>(
                name: "SnapshotId",
                table: "MediaFile",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AfterSnapshotId",
                table: "MediaConversion",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BeforeSnapshotId",
                table: "MediaConversion",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConversionPlan",
                table: "MediaConversion",
                type: "TEXT",
                nullable: false,
                defaultValue: "{\"Tracks\":[],\"HasChapters\":null,\"HasAttachments\":null,\"Faststart\":null}");

            // 3. Data migration: build MediaSnapshot/TrackSnapshot rows from the
            // old columns and MediaTrack table before dropping them, using
            // temporary correlation columns that are dropped at the end.
            migrationBuilder.Sql("""
                ALTER TABLE MediaSnapshot ADD COLUMN _TempFileId INTEGER;
                ALTER TABLE MediaSnapshot ADD COLUMN _TempConvBeforeId INTEGER;
                ALTER TABLE MediaSnapshot ADD COLUMN _TempConvAfterId INTEGER;

                -- One current snapshot per MediaFile, carrying its probe metadata forward.
                INSERT INTO MediaSnapshot
                    (_TempFileId, CapturedAt, ContainerType, Resolution, DurationMs,
                     VideoBitDepth, TrackCount, HasChapters, HasAttachments, HasFaststart)
                SELECT Id, UpdatedDate, ContainerType, Resolution, DurationMs,
                       VideoBitDepth, TrackCount, 0, 0, HasFaststart
                FROM MediaFile;

                UPDATE MediaFile SET SnapshotId = (
                    SELECT Id FROM MediaSnapshot WHERE _TempFileId = MediaFile.Id
                );

                -- MediaTrack rows move onto their file's current snapshot.
                INSERT INTO TrackSnapshot
                    (SnapshotId, "Index", Type, IsCommentary, IsHearingImpaired,
                     IsVisualImpaired, IsDefault, IsForced, IsOriginal, IsDub,
                     Codec, AudioChannels, LanguageCode, LanguageName, Name, DurationMs)
                SELECT ms.Id, mt.TrackNumber, mt.Type, mt.IsCommentary, mt.IsHearingImpaired,
                       mt.IsVisualImpaired, mt.IsDefault, mt.IsForced, mt.IsOriginal, 0,
                       mt.Codec, mt.AudioChannels, mt.LanguageCode, mt.LanguageName, mt.TrackName, 0
                FROM MediaTrack mt
                INNER JOIN MediaSnapshot ms ON ms._TempFileId = mt.MediaFileId;

                -- Historical before/after snapshots per conversion, reconstructed
                -- from the old JSON columns so the Conversions page still renders
                -- what each conversion saw.
                INSERT INTO MediaSnapshot
                    (_TempConvBeforeId, CapturedAt, DurationMs, VideoBitDepth,
                     TrackCount, HasChapters, HasAttachments, HasFaststart)
                SELECT Id, CreatedDate, 0, 0, 0, 0, 0, 0
                FROM MediaConversion
                WHERE TracksBefore IS NOT NULL AND TracksBefore != '' AND TracksBefore != '[]';

                UPDATE MediaConversion SET BeforeSnapshotId = (
                    SELECT Id FROM MediaSnapshot WHERE _TempConvBeforeId = MediaConversion.Id
                );

                INSERT INTO MediaSnapshot
                    (_TempConvAfterId, CapturedAt, DurationMs, VideoBitDepth,
                     TrackCount, HasChapters, HasAttachments, HasFaststart)
                SELECT Id, UpdatedDate, 0, 0, 0, 0, 0, 0
                FROM MediaConversion
                WHERE TracksAfter IS NOT NULL AND TracksAfter != '' AND TracksAfter != '[]';

                UPDATE MediaConversion SET AfterSnapshotId = (
                    SELECT Id FROM MediaSnapshot WHERE _TempConvAfterId = MediaConversion.Id
                );

                INSERT INTO TrackSnapshot
                    (SnapshotId, "Index", Type, IsCommentary, IsHearingImpaired,
                     IsVisualImpaired, IsDefault, IsForced, IsOriginal, IsDub,
                     Codec, AudioChannels, LanguageCode, LanguageName, Name, DurationMs)
                SELECT ms.Id,
                       COALESCE(CAST(json_extract(tr.value, '$.Id') AS INTEGER), 0),
                       COALESCE(json_extract(tr.value, '$.Type'), 'Unknown'),
                       COALESCE(CAST(json_extract(tr.value, '$.IsCommentary') AS INTEGER), 0),
                       COALESCE(CAST(json_extract(tr.value, '$.IsHearingImpaired') AS INTEGER), 0),
                       COALESCE(CAST(json_extract(tr.value, '$.IsVisualImpaired') AS INTEGER), 0),
                       COALESCE(CAST(json_extract(tr.value, '$.IsDefault') AS INTEGER), 0),
                       COALESCE(CAST(json_extract(tr.value, '$.IsForced') AS INTEGER), 0),
                       COALESCE(CAST(json_extract(tr.value, '$.IsOriginal') AS INTEGER), 0),
                       0,
                       COALESCE(json_extract(tr.value, '$.Codec'), ''),
                       COALESCE(CAST(json_extract(tr.value, '$.AudioChannels') AS INTEGER), 0),
                       COALESCE(json_extract(tr.value, '$.LanguageCode'), ''),
                       COALESCE(json_extract(tr.value, '$.LanguageName'), ''),
                       json_extract(tr.value, '$.TrackName'),
                       0
                FROM MediaConversion mc
                INNER JOIN MediaSnapshot ms ON ms._TempConvBeforeId = mc.Id
                CROSS JOIN json_each(mc.TracksBefore) tr;

                INSERT INTO TrackSnapshot
                    (SnapshotId, "Index", Type, IsCommentary, IsHearingImpaired,
                     IsVisualImpaired, IsDefault, IsForced, IsOriginal, IsDub,
                     Codec, AudioChannels, LanguageCode, LanguageName, Name, DurationMs)
                SELECT ms.Id,
                       COALESCE(CAST(json_extract(tr.value, '$.Id') AS INTEGER), 0),
                       COALESCE(json_extract(tr.value, '$.Type'), 'Unknown'),
                       COALESCE(CAST(json_extract(tr.value, '$.IsCommentary') AS INTEGER), 0),
                       COALESCE(CAST(json_extract(tr.value, '$.IsHearingImpaired') AS INTEGER), 0),
                       COALESCE(CAST(json_extract(tr.value, '$.IsVisualImpaired') AS INTEGER), 0),
                       COALESCE(CAST(json_extract(tr.value, '$.IsDefault') AS INTEGER), 0),
                       COALESCE(CAST(json_extract(tr.value, '$.IsForced') AS INTEGER), 0),
                       COALESCE(CAST(json_extract(tr.value, '$.IsOriginal') AS INTEGER), 0),
                       0,
                       COALESCE(json_extract(tr.value, '$.Codec'), ''),
                       COALESCE(CAST(json_extract(tr.value, '$.AudioChannels') AS INTEGER), 0),
                       COALESCE(json_extract(tr.value, '$.LanguageCode'), ''),
                       COALESCE(json_extract(tr.value, '$.LanguageName'), ''),
                       json_extract(tr.value, '$.TrackName'),
                       0
                FROM MediaConversion mc
                INNER JOIN MediaSnapshot ms ON ms._TempConvAfterId = mc.Id
                CROSS JOIN json_each(mc.TracksAfter) tr;

                UPDATE MediaSnapshot SET TrackCount = (
                    SELECT COUNT(*) FROM TrackSnapshot WHERE SnapshotId = MediaSnapshot.Id
                ) WHERE _TempConvBeforeId IS NOT NULL OR _TempConvAfterId IS NOT NULL;

                ALTER TABLE MediaSnapshot DROP COLUMN _TempFileId;
                ALTER TABLE MediaSnapshot DROP COLUMN _TempConvBeforeId;
                ALTER TABLE MediaSnapshot DROP COLUMN _TempConvAfterId;
                """);

            // 4. Drop old indexes, columns and MediaTrack.
            migrationBuilder.DropIndex(
                name: "IX_MediaFile_ContainerType",
                table: "MediaFile");

            migrationBuilder.DropIndex(
                name: "IX_MediaFile_Resolution",
                table: "MediaFile");

            migrationBuilder.DropColumn(name: "ContainerType", table: "MediaFile");
            migrationBuilder.DropColumn(name: "DurationMs", table: "MediaFile");
            migrationBuilder.DropColumn(name: "HasFaststart", table: "MediaFile");
            migrationBuilder.DropColumn(name: "Resolution", table: "MediaFile");
            migrationBuilder.DropColumn(name: "TrackCount", table: "MediaFile");
            migrationBuilder.DropColumn(name: "VideoBitDepth", table: "MediaFile");

            migrationBuilder.DropColumn(name: "AllowedTracks", table: "MediaConversion");
            migrationBuilder.DropColumn(name: "TracksAfter", table: "MediaConversion");
            migrationBuilder.DropColumn(name: "TracksBefore", table: "MediaConversion");

            migrationBuilder.DropTable(name: "MediaTrack");

            // 5. New indexes + FKs.
            migrationBuilder.CreateIndex(
                name: "IX_MediaFile_FileCreationTime",
                table: "MediaFile",
                column: "FileCreationTime");

            migrationBuilder.CreateIndex(
                name: "IX_MediaFile_SnapshotId",
                table: "MediaFile",
                column: "SnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaConversion_AfterSnapshotId",
                table: "MediaConversion",
                column: "AfterSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaConversion_BeforeSnapshotId",
                table: "MediaConversion",
                column: "BeforeSnapshotId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaSnapshot_ContainerType",
                table: "MediaSnapshot",
                column: "ContainerType");

            migrationBuilder.CreateIndex(
                name: "IX_MediaSnapshot_Resolution",
                table: "MediaSnapshot",
                column: "Resolution");

            migrationBuilder.CreateIndex(
                name: "IX_TrackSnapshot_SnapshotId_Index",
                table: "TrackSnapshot",
                columns: new[] { "SnapshotId", "Index" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrackSnapshot_SnapshotId_Type_AudioChannels",
                table: "TrackSnapshot",
                columns: new[] { "SnapshotId", "Type", "AudioChannels" });

            migrationBuilder.CreateIndex(
                name: "IX_TrackSnapshot_SnapshotId_Type_Codec",
                table: "TrackSnapshot",
                columns: new[] { "SnapshotId", "Type", "Codec" });

            migrationBuilder.CreateIndex(
                name: "IX_TrackSnapshot_SnapshotId_Type_LanguageName",
                table: "TrackSnapshot",
                columns: new[] { "SnapshotId", "Type", "LanguageName" });

            migrationBuilder.AddForeignKey(
                name: "FK_MediaConversion_MediaSnapshot_AfterSnapshotId",
                table: "MediaConversion",
                column: "AfterSnapshotId",
                principalTable: "MediaSnapshot",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_MediaConversion_MediaSnapshot_BeforeSnapshotId",
                table: "MediaConversion",
                column: "BeforeSnapshotId",
                principalTable: "MediaSnapshot",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_MediaFile_MediaSnapshot_SnapshotId",
                table: "MediaFile",
                column: "SnapshotId",
                principalTable: "MediaSnapshot",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MediaConversion_MediaSnapshot_AfterSnapshotId",
                table: "MediaConversion");

            migrationBuilder.DropForeignKey(
                name: "FK_MediaConversion_MediaSnapshot_BeforeSnapshotId",
                table: "MediaConversion");

            migrationBuilder.DropForeignKey(
                name: "FK_MediaFile_MediaSnapshot_SnapshotId",
                table: "MediaFile");

            migrationBuilder.DropTable(name: "TrackSnapshot");
            migrationBuilder.DropTable(name: "MediaSnapshot");

            migrationBuilder.DropIndex(
                name: "IX_MediaFile_FileCreationTime",
                table: "MediaFile");

            migrationBuilder.DropIndex(
                name: "IX_MediaFile_SnapshotId",
                table: "MediaFile");

            migrationBuilder.DropIndex(
                name: "IX_MediaConversion_AfterSnapshotId",
                table: "MediaConversion");

            migrationBuilder.DropIndex(
                name: "IX_MediaConversion_BeforeSnapshotId",
                table: "MediaConversion");

            migrationBuilder.DropColumn(name: "SnapshotId", table: "MediaFile");
            migrationBuilder.DropColumn(name: "AfterSnapshotId", table: "MediaConversion");
            migrationBuilder.DropColumn(name: "BeforeSnapshotId", table: "MediaConversion");
            migrationBuilder.DropColumn(name: "ConversionPlan", table: "MediaConversion");

            migrationBuilder.AddColumn<string>(
                name: "ContainerType",
                table: "MediaFile",
                type: "TEXT",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "DurationMs",
                table: "MediaFile",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<bool>(
                name: "HasFaststart",
                table: "MediaFile",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Resolution",
                table: "MediaFile",
                type: "TEXT",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TrackCount",
                table: "MediaFile",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "VideoBitDepth",
                table: "MediaFile",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "AllowedTracks",
                table: "MediaConversion",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TracksAfter",
                table: "MediaConversion",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TracksBefore",
                table: "MediaConversion",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "MediaTrack",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MediaFileId = table.Column<int>(type: "INTEGER", nullable: false),
                    AudioChannels = table.Column<int>(type: "INTEGER", nullable: false),
                    Codec = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    IsCommentary = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsDefault = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsForced = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsHearingImpaired = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsOriginal = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsVisualImpaired = table.Column<bool>(type: "INTEGER", nullable: false),
                    LanguageCode = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    LanguageName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    TrackName = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    TrackNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaTrack", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MediaTrack_MediaFile_MediaFileId",
                        column: x => x.MediaFileId,
                        principalTable: "MediaFile",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MediaFile_ContainerType",
                table: "MediaFile",
                column: "ContainerType");

            migrationBuilder.CreateIndex(
                name: "IX_MediaFile_Resolution",
                table: "MediaFile",
                column: "Resolution");

            migrationBuilder.CreateIndex(
                name: "IX_MediaTrack_MediaFileId_TrackNumber",
                table: "MediaTrack",
                columns: new[] { "MediaFileId", "TrackNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MediaTrack_MediaFileId_Type_AudioChannels",
                table: "MediaTrack",
                columns: new[] { "MediaFileId", "Type", "AudioChannels" });

            migrationBuilder.CreateIndex(
                name: "IX_MediaTrack_MediaFileId_Type_Codec",
                table: "MediaTrack",
                columns: new[] { "MediaFileId", "Type", "Codec" });

            migrationBuilder.CreateIndex(
                name: "IX_MediaTrack_MediaFileId_Type_LanguageName",
                table: "MediaTrack",
                columns: new[] { "MediaFileId", "Type", "LanguageName" });
        }
    }
}
