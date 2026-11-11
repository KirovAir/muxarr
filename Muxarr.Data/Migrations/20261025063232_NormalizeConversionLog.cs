using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Muxarr.Data.Migrations
{
    /// <inheritdoc />
    public partial class NormalizeConversionLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ConversionLogEntry",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MediaConversionId = table.Column<int>(type: "INTEGER", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Message = table.Column<string>(type: "TEXT", nullable: false),
                    IsError = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConversionLogEntry", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ConversionLogEntry_MediaConversion_MediaConversionId",
                        column: x => x.MediaConversionId,
                        principalTable: "MediaConversion",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ConversionLogEntry_MediaConversionId",
                table: "ConversionLogEntry",
                column: "MediaConversionId");

            // One row per entry from the old text log. A line without the
            // "[yyyy-MM-dd HH:mm:ss.fff] " prefix is the continuation of a multi-line
            // message, so it is folded back into the entry above it.
            migrationBuilder.Sql("""
                WITH RECURSIVE split(ConversionId, Seq, Line, Rest) AS (
                    SELECT Id, 0, NULL, replace(Log, char(13), '') || char(10)
                    FROM MediaConversion
                    WHERE Log != ''
                    UNION ALL
                    SELECT ConversionId, Seq + 1,
                           substr(Rest, 1, instr(Rest, char(10)) - 1),
                           substr(Rest, instr(Rest, char(10)) + 1)
                    FROM split
                    WHERE Rest != ''
                ),
                lines AS (
                    SELECT ConversionId, Seq, Line,
                           Line GLOB '[[][0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9] [0-9][0-9]:[0-9][0-9]:[0-9][0-9].[0-9][0-9][0-9]] *' AS Stamped
                    FROM split
                    WHERE Seq > 0
                ),
                grouped AS (
                    SELECT ConversionId, Seq, Line, Stamped,
                           SUM(Stamped) OVER (PARTITION BY ConversionId ORDER BY Seq) AS Entry
                    FROM lines
                )
                INSERT INTO ConversionLogEntry (MediaConversionId, Timestamp, Message, IsError)
                SELECT g.ConversionId,
                       COALESCE(MAX(CASE WHEN g.Stamped THEN substr(g.Line, 2, 23) END), mc.CreatedDate),
                       group_concat(CASE WHEN g.Stamped THEN substr(g.Line, 27) ELSE g.Line END, char(10) ORDER BY g.Seq),
                       0
                FROM grouped g
                INNER JOIN MediaConversion mc ON mc.Id = g.ConversionId
                GROUP BY g.ConversionId, g.Entry
                ORDER BY g.ConversionId, g.Entry;
                """);

            // Native DROP COLUMN keeps this migration in one transaction; EF's DropColumn rebuilds the table.
            migrationBuilder.Sql("ALTER TABLE MediaConversion DROP COLUMN Log;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Log",
                table: "MediaConversion",
                type: "TEXT",
                maxLength: 2147483647,
                nullable: false,
                defaultValue: "");

            migrationBuilder.Sql("""
                UPDATE MediaConversion SET Log = COALESCE((
                    SELECT group_concat('[' || strftime('%Y-%m-%d %H:%M:%f', e.Timestamp) || '] ' || e.Message, char(10) ORDER BY e.Id)
                    FROM ConversionLogEntry e
                    WHERE e.MediaConversionId = MediaConversion.Id
                ), '');
                """);

            migrationBuilder.DropTable(
                name: "ConversionLogEntry");
        }
    }
}
