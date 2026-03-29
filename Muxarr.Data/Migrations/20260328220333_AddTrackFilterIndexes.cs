using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Muxarr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTrackFilterIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MediaTrack_Type",
                table: "MediaTrack");

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

            migrationBuilder.CreateIndex(
                name: "IX_MediaFile_ContainerType",
                table: "MediaFile",
                column: "ContainerType");

            migrationBuilder.CreateIndex(
                name: "IX_MediaFile_Resolution",
                table: "MediaFile",
                column: "Resolution");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MediaTrack_MediaFileId_Type_AudioChannels",
                table: "MediaTrack");

            migrationBuilder.DropIndex(
                name: "IX_MediaTrack_MediaFileId_Type_Codec",
                table: "MediaTrack");

            migrationBuilder.DropIndex(
                name: "IX_MediaTrack_MediaFileId_Type_LanguageName",
                table: "MediaTrack");

            migrationBuilder.DropIndex(
                name: "IX_MediaFile_ContainerType",
                table: "MediaFile");

            migrationBuilder.DropIndex(
                name: "IX_MediaFile_Resolution",
                table: "MediaFile");

            migrationBuilder.CreateIndex(
                name: "IX_MediaTrack_Type",
                table: "MediaTrack",
                column: "Type");
        }
    }
}
