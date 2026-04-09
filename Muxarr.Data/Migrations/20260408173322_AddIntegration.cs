using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Muxarr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddIntegration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Create Integration table to replace JSON-based Config entries for Sonarr/Radarr.
            // Seed from existing configs, then rebuild MediaInfo with a FK so cascade delete
            // cleans up when a service is removed.
            migrationBuilder.Sql("""
                CREATE TABLE Integration (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL,
                    Type TEXT NOT NULL,
                    Url TEXT NOT NULL DEFAULT '',
                    ApiKey TEXT NOT NULL DEFAULT '',
                    CreatedDate TEXT NOT NULL,
                    UpdatedDate TEXT NOT NULL
                );

                INSERT INTO Integration (Name, Type, Url, ApiKey, CreatedDate, UpdatedDate)
                SELECT 'Sonarr', 'Sonarr',
                    COALESCE(json_extract(Value, '$.Url'), ''),
                    COALESCE(json_extract(Value, '$.ApiKey'), ''),
                    datetime('now'), datetime('now')
                FROM Config WHERE Id = 'Sonarr'
                  AND Value IS NOT NULL
                  AND COALESCE(json_extract(Value, '$.Url'), '') != '';

                INSERT INTO Integration (Name, Type, Url, ApiKey, CreatedDate, UpdatedDate)
                SELECT 'Radarr', 'Radarr',
                    COALESCE(json_extract(Value, '$.Url'), ''),
                    COALESCE(json_extract(Value, '$.ApiKey'), ''),
                    datetime('now'), datetime('now')
                FROM Config WHERE Id = 'Radarr'
                  AND Value IS NOT NULL
                  AND COALESCE(json_extract(Value, '$.Url'), '') != '';

                CREATE TABLE MediaInfo_new (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ExternalId INTEGER NOT NULL,
                    IntegrationId INTEGER NULL,
                    Title TEXT NOT NULL,
                    OriginalLanguage TEXT NOT NULL,
                    Path TEXT NOT NULL,
                    IsMovie INTEGER NOT NULL,
                    CreatedDate TEXT NOT NULL,
                    UpdatedDate TEXT NOT NULL,
                    FOREIGN KEY (IntegrationId) REFERENCES Integration(Id) ON DELETE CASCADE
                );

                INSERT INTO MediaInfo_new (Id, ExternalId, IntegrationId, Title,
                    OriginalLanguage, Path, IsMovie, CreatedDate, UpdatedDate)
                SELECT m.Id, m.ExternalId,
                    (SELECT s.Id FROM Integration s
                     WHERE s.Type = CASE WHEN m.IsMovie = 1 THEN 'Radarr' ELSE 'Sonarr' END
                     LIMIT 1),
                    m.Title, m.OriginalLanguage, m.Path, m.IsMovie,
                    m.CreatedDate, m.UpdatedDate
                FROM MediaInfo m;

                DROP TABLE MediaInfo;
                ALTER TABLE MediaInfo_new RENAME TO MediaInfo;

                CREATE UNIQUE INDEX IX_MediaInfo_ExternalId_IntegrationId ON MediaInfo (ExternalId, IntegrationId);
                CREATE INDEX IX_MediaInfo_IntegrationId ON MediaInfo (IntegrationId);
                CREATE INDEX IX_MediaInfo_Path ON MediaInfo (Path);

                DELETE FROM Config WHERE Id IN ('Sonarr', 'Radarr');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                INSERT OR IGNORE INTO Config (Id, Value, CreatedDate, UpdatedDate)
                SELECT Type,
                    json_object('Url', Url, 'ApiKey', ApiKey),
                    CreatedDate, UpdatedDate
                FROM Integration
                ORDER BY Id;

                CREATE TABLE MediaInfo_old (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ExternalId INTEGER NOT NULL,
                    Title TEXT NOT NULL,
                    OriginalLanguage TEXT NOT NULL,
                    Path TEXT NOT NULL,
                    IsMovie INTEGER NOT NULL,
                    CreatedDate TEXT NOT NULL,
                    UpdatedDate TEXT NOT NULL
                );

                INSERT INTO MediaInfo_old (Id, ExternalId, Title, OriginalLanguage, Path, IsMovie, CreatedDate, UpdatedDate)
                SELECT Id, ExternalId, Title, OriginalLanguage, Path, IsMovie, CreatedDate, UpdatedDate
                FROM MediaInfo
                WHERE Id IN (
                    SELECT MIN(Id) FROM MediaInfo GROUP BY ExternalId, IsMovie
                );

                DROP TABLE MediaInfo;
                ALTER TABLE MediaInfo_old RENAME TO MediaInfo;

                CREATE UNIQUE INDEX IX_MediaInfo_ExternalId_IsMovie ON MediaInfo (ExternalId, IsMovie);
                CREATE INDEX IX_MediaInfo_Path ON MediaInfo (Path);

                DROP TABLE Integration;
                """);
        }
    }
}
