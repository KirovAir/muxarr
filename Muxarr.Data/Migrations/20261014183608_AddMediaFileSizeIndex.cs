using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Muxarr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMediaFileSizeIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_MediaFile_Size",
                table: "MediaFile",
                column: "Size");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MediaFile_Size",
                table: "MediaFile");
        }
    }
}
