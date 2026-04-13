using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Muxarr.Data.Migrations
{
    /// <inheritdoc />
    public partial class ExpandMediaModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "DurationMs",
                table: "MediaTrack",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<int>(
                name: "ChapterCount",
                table: "MediaFile",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "AttachmentCount",
                table: "MediaFile",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.RenameColumn(
                name: "TracksBefore",
                table: "MediaConversion",
                newName: "SnapshotBefore");

            migrationBuilder.RenameColumn(
                name: "TracksAfter",
                table: "MediaConversion",
                newName: "SnapshotAfter");

            migrationBuilder.RenameColumn(
                name: "AllowedTracks",
                table: "MediaConversion",
                newName: "ConversionPlan");

            // Wrap existing JSON arrays (List<TrackSnapshot>) in the MediaSnapshot envelope.
            // Old: [{...}, {...}]  New: {"Tracks":[{...}],"HasChapters":false,...}
            const string wrapSql = """
                UPDATE MediaConversion
                SET SnapshotBefore = CASE
                        WHEN SnapshotBefore IS NOT NULL AND SnapshotBefore != ''
                        THEN '{"Tracks":' || SnapshotBefore || ',"HasChapters":false,"HasAttachments":false}'
                        ELSE '{"Tracks":[],"HasChapters":false,"HasAttachments":false}'
                    END,
                    SnapshotAfter = CASE
                        WHEN SnapshotAfter IS NOT NULL AND SnapshotAfter != ''
                        THEN '{"Tracks":' || SnapshotAfter || ',"HasChapters":false,"HasAttachments":false}'
                        ELSE '{"Tracks":[],"HasChapters":false,"HasAttachments":false}'
                    END,
                    ConversionPlan = CASE
                        WHEN ConversionPlan IS NOT NULL AND ConversionPlan != ''
                        THEN '{"Tracks":' || ConversionPlan || ',"HasChapters":false,"HasAttachments":false}'
                        ELSE '{"Tracks":[],"HasChapters":false,"HasAttachments":false}'
                    END
                """;

            migrationBuilder.Sql(wrapSql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Unwrap MediaSnapshot envelope back to plain track arrays.
            const string unwrapSql = """
                UPDATE MediaConversion
                SET SnapshotBefore = json_extract(SnapshotBefore, '$.Tracks'),
                    SnapshotAfter = json_extract(SnapshotAfter, '$.Tracks'),
                    ConversionPlan = json_extract(ConversionPlan, '$.Tracks')
                """;

            migrationBuilder.Sql(unwrapSql);

            migrationBuilder.RenameColumn(
                name: "SnapshotBefore",
                table: "MediaConversion",
                newName: "TracksBefore");

            migrationBuilder.RenameColumn(
                name: "SnapshotAfter",
                table: "MediaConversion",
                newName: "TracksAfter");

            migrationBuilder.RenameColumn(
                name: "ConversionPlan",
                table: "MediaConversion",
                newName: "AllowedTracks");

            migrationBuilder.DropColumn(
                name: "DurationMs",
                table: "MediaTrack");

            migrationBuilder.DropColumn(
                name: "ChapterCount",
                table: "MediaFile");

            migrationBuilder.DropColumn(
                name: "AttachmentCount",
                table: "MediaFile");
        }
    }
}
