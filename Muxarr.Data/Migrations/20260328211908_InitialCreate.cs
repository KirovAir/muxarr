using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Muxarr.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Config",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(255)", nullable: false),
                    Value = table.Column<string>(type: "json", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedDate = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Config", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DataProtectionKeys",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FriendlyName = table.Column<string>(type: "TEXT", nullable: true),
                    Xml = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataProtectionKeys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LogEntry",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Timestamp = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Level = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Message = table.Column<string>(type: "TEXT", nullable: false),
                    Exception = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LogEntry", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MediaInfo",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    IsMovie = table.Column<bool>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    OriginalLanguage = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Path = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedDate = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaInfo", x => new { x.Id, x.IsMovie });
                });

            migrationBuilder.CreateTable(
                name: "Profile",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Directories = table.Column<string>(type: "TEXT", nullable: false),
                    ClearVideoTrackNames = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    AudioSettings = table.Column<string>(type: "TEXT", nullable: false),
                    SubtitleSettings = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedDate = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Profile", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MediaFile",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProfileId = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: true),
                    OriginalLanguage = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Path = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false),
                    Size = table.Column<long>(type: "INTEGER", nullable: false),
                    MkvMergeOutput = table.Column<string>(type: "TEXT", nullable: true),
                    TrackCount = table.Column<int>(type: "INTEGER", nullable: false),
                    HasRedundantTracks = table.Column<bool>(type: "INTEGER", nullable: false),
                    HasNonStandardMetadata = table.Column<bool>(type: "INTEGER", nullable: false),
                    ContainerType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Resolution = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    DurationMs = table.Column<long>(type: "INTEGER", nullable: false),
                    VideoBitDepth = table.Column<int>(type: "INTEGER", nullable: false),
                    FileLastWriteTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    FileCreationTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedDate = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaFile", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MediaFile_Profile_ProfileId",
                        column: x => x.ProfileId,
                        principalTable: "Profile",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MediaConversion",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MediaFileId = table.Column<int>(type: "INTEGER", nullable: true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false),
                    TempFilePath = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: true),
                    Log = table.Column<string>(type: "TEXT", maxLength: 2147483647, nullable: false),
                    Progress = table.Column<int>(type: "INTEGER", nullable: false),
                    SizeBefore = table.Column<long>(type: "INTEGER", nullable: false),
                    SizeAfter = table.Column<long>(type: "INTEGER", nullable: false),
                    SizeDifference = table.Column<long>(type: "INTEGER", nullable: false),
                    TracksBefore = table.Column<string>(type: "TEXT", nullable: false),
                    TracksAfter = table.Column<string>(type: "TEXT", nullable: false),
                    AllowedTracks = table.Column<string>(type: "TEXT", nullable: false),
                    IsCustomConversion = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    State = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedDate = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaConversion", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MediaConversion_MediaFile_MediaFileId",
                        column: x => x.MediaFileId,
                        principalTable: "MediaFile",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "MediaTrack",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MediaFileId = table.Column<int>(type: "INTEGER", nullable: false),
                    TrackNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    IsCommentary = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsHearingImpaired = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsVisualImpaired = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsForced = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsOriginal = table.Column<bool>(type: "INTEGER", nullable: false),
                    Codec = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    AudioChannels = table.Column<int>(type: "INTEGER", nullable: false),
                    LanguageCode = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    LanguageName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    TrackName = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaTrack", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MediaTrack_MediaFile_MediaFileId",
                        column: x => x.MediaFileId,
                        principalTable: "MediaFile",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LogEntry_Level",
                table: "LogEntry",
                column: "Level");

            migrationBuilder.CreateIndex(
                name: "IX_LogEntry_Timestamp",
                table: "LogEntry",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_MediaConversion_MediaFileId",
                table: "MediaConversion",
                column: "MediaFileId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaFile_Path",
                table: "MediaFile",
                column: "Path");

            migrationBuilder.CreateIndex(
                name: "IX_MediaFile_ProfileId",
                table: "MediaFile",
                column: "ProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaInfo_Path",
                table: "MediaInfo",
                column: "Path");

            migrationBuilder.CreateIndex(
                name: "IX_MediaTrack_MediaFileId_TrackNumber",
                table: "MediaTrack",
                columns: new[] { "MediaFileId", "TrackNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MediaTrack_Type",
                table: "MediaTrack",
                column: "Type");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Config");

            migrationBuilder.DropTable(
                name: "DataProtectionKeys");

            migrationBuilder.DropTable(
                name: "LogEntry");

            migrationBuilder.DropTable(
                name: "MediaConversion");

            migrationBuilder.DropTable(
                name: "MediaInfo");

            migrationBuilder.DropTable(
                name: "MediaTrack");

            migrationBuilder.DropTable(
                name: "MediaFile");

            migrationBuilder.DropTable(
                name: "Profile");
        }
    }
}
