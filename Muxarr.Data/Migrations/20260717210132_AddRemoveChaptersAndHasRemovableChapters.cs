using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Muxarr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRemoveChaptersAndHasRemovableChapters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "RemoveChapters",
                table: "Profile",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasRemovableChapters",
                table: "MediaFile",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RemoveChapters",
                table: "Profile");

            migrationBuilder.DropColumn(
                name: "HasRemovableChapters",
                table: "MediaFile");
        }
    }
}
