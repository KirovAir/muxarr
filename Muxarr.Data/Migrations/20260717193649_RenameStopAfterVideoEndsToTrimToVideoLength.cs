using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Muxarr.Data.Migrations
{
    /// <inheritdoc />
    public partial class RenameStopAfterVideoEndsToTrimToVideoLength : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "StopAfterVideoEnds",
                table: "Profile",
                newName: "TrimToVideoLength");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "TrimToVideoLength",
                table: "Profile",
                newName: "StopAfterVideoEnds");
        }
    }
}
