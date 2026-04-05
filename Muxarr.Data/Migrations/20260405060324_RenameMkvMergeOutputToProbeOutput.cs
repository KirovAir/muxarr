using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Muxarr.Data.Migrations
{
    /// <inheritdoc />
    public partial class RenameMkvMergeOutputToProbeOutput : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "MkvMergeOutput",
                table: "MediaFile",
                newName: "ProbeOutput");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "ProbeOutput",
                table: "MediaFile",
                newName: "MkvMergeOutput");
        }
    }
}
