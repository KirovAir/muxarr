using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Muxarr.Data.Migrations
{
    /// <summary>
    /// Migrates AllowedLanguages in AudioSettings/SubtitleSettings:
    /// 1. Wraps plain IsoLanguage objects in LanguagePreference: {"Name":"English",...} → {"Language":{"Name":"English",...}}
    /// 2. Converts KeepOriginalLanguage boolean into an "Original Language" list entry (appended at end).
    /// </summary>
    public partial class MigrateLanguagePreference : Migration
    {
        private static readonly string[] Columns = ["AudioSettings", "SubtitleSettings"];

        private const string OriginalLanguageJson =
            """{"Language":{"Name":"Original Language","DisplayName":"Original Language","TwoLetterCode":"orig","NativeName":"Original","ThreeLetterCodes":["orig"]}}""";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var column in Columns)
            {
                // Step 1: Wrap each AllowedLanguages element: {IsoLanguage} → {"Language": {IsoLanguage}}
                // Only applies to old format (first element lacks "Language" key). Idempotent.
                migrationBuilder.Sql($"""
                    UPDATE Profile
                    SET {column} = json_set(
                        {column},
                        '$.AllowedLanguages',
                        (SELECT json_group_array(json_object('Language', json(value)))
                         FROM json_each({column}, '$.AllowedLanguages'))
                    )
                    WHERE {column} IS NOT NULL
                      AND json_array_length({column}, '$.AllowedLanguages') > 0
                      AND json_extract({column}, '$.AllowedLanguages[0].Language') IS NULL;
                    """);

                // Step 2: Convert KeepOriginalLanguage=true → append "Original Language" entry to the list.
                migrationBuilder.Sql($"""
                    UPDATE Profile
                    SET {column} = json_set(
                        {column},
                        '$.AllowedLanguages',
                        json_insert(
                            json_extract({column}, '$.AllowedLanguages'),
                            '$[#]',
                            json('{OriginalLanguageJson}')
                        )
                    )
                    WHERE {column} IS NOT NULL
                      AND json_extract({column}, '$.KeepOriginalLanguage') = 1;
                    """);

                // Step 3: Remove the now-obsolete KeepOriginalLanguage key from the JSON.
                migrationBuilder.Sql($"""
                    UPDATE Profile
                    SET {column} = json_remove({column}, '$.KeepOriginalLanguage')
                    WHERE {column} IS NOT NULL
                      AND json_extract({column}, '$.KeepOriginalLanguage') IS NOT NULL;
                    """);
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var column in Columns)
            {
                // Restore KeepOriginalLanguage=true if "Original Language" entry exists.
                migrationBuilder.Sql($"""
                    UPDATE Profile
                    SET {column} = json_set({column}, '$.KeepOriginalLanguage', 1)
                    WHERE {column} IS NOT NULL
                      AND EXISTS (
                        SELECT 1 FROM json_each({column}, '$.AllowedLanguages')
                        WHERE json_extract(value, '$.Language.Name') = 'Original Language'
                      );
                    """);

                // Remove "Original Language" entries.
                migrationBuilder.Sql($"""
                    UPDATE Profile
                    SET {column} = json_set(
                        {column},
                        '$.AllowedLanguages',
                        (SELECT json_group_array(json(value))
                         FROM json_each({column}, '$.AllowedLanguages')
                         WHERE json_extract(value, '$.Language.Name') != 'Original Language')
                    )
                    WHERE {column} IS NOT NULL
                      AND json_array_length({column}, '$.AllowedLanguages') > 0;
                    """);

                // Unwrap LanguagePreference back to plain IsoLanguage.
                migrationBuilder.Sql($"""
                    UPDATE Profile
                    SET {column} = json_set(
                        {column},
                        '$.AllowedLanguages',
                        (SELECT json_group_array(json(json_extract(value, '$.Language')))
                         FROM json_each({column}, '$.AllowedLanguages'))
                    )
                    WHERE {column} IS NOT NULL
                      AND json_array_length({column}, '$.AllowedLanguages') > 0
                      AND json_extract({column}, '$.AllowedLanguages[0].Language') IS NOT NULL;
                    """);
            }
        }
    }
}
