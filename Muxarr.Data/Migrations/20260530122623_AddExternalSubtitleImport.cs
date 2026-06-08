using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Muxarr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddExternalSubtitleImport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "DeleteExternalSubtitleSource",
                table: "Profile",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ImportExternalSubtitles",
                table: "Profile",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ExternalSubtitles",
                table: "MediaFile",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "HasExternalSubtitles",
                table: "MediaFile",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeleteExternalSubtitleSource",
                table: "Profile");

            migrationBuilder.DropColumn(
                name: "ImportExternalSubtitles",
                table: "Profile");

            migrationBuilder.DropColumn(
                name: "ExternalSubtitles",
                table: "MediaFile");

            migrationBuilder.DropColumn(
                name: "HasExternalSubtitles",
                table: "MediaFile");
        }
    }
}
