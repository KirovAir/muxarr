using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Muxarr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTrackDuration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "Duration",
                table: "MediaTrack",
                type: "REAL",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "HasInvalidDuration",
                table: "MediaFile",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Duration",
                table: "MediaTrack");

            migrationBuilder.DropColumn(
                name: "HasInvalidDuration",
                table: "MediaFile");
        }
    }
}
