using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Muxarr.Data.Migrations
{
    /// <summary>
    /// Backfills raw codec strings that were left as-is by earlier scans because
    /// no enum existed for them (Mpeg4, Mpeg2Video, Vc1, Mp2). Same pattern as
    /// NormalizeRawCodecStrings.
    /// </summary>
    public partial class NormalizeNewCodecEnums : Migration
    {
        private static readonly (string Raw, string EnumName)[] RawCodecMappings =
        [
            // Video
            ("mpeg4", "Mpeg4"),
            ("MPEG-4p2", "Mpeg4"),
            ("mpeg2video", "Mpeg2Video"),
            ("MPEG-2", "Mpeg2Video"),
            ("MPEG-1/2", "Mpeg2Video"),
            ("vc1", "Vc1"),
            ("VC-1", "Vc1"),

            // Audio
            ("mp2", "Mp2"),
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var (raw, enumName) in RawCodecMappings)
            {
                var escapedRaw = raw.Replace("'", "''");
                migrationBuilder.Sql(
                    $"UPDATE MediaTrack SET Codec = '{enumName}' WHERE Codec = '{escapedRaw}' COLLATE NOCASE AND Codec != '{enumName}';");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Not reversible - we don't know which raw variant each row originally had.
        }
    }
}
