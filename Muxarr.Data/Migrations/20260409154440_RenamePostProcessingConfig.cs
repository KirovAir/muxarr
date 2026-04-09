using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Muxarr.Data.Migrations
{
    /// <inheritdoc />
    public partial class RenamePostProcessingConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Rename JSON fields: Enabled -> PostProcessingEnabled, Command -> PostProcessingCommand
            migrationBuilder.Sql("""
                UPDATE Config
                SET Value = REPLACE(REPLACE(Value,
                    '"Enabled":', '"PostProcessingEnabled":'),
                    '"Command":', '"PostProcessingCommand":')
                WHERE Id = 'PostProcessing';
                """);

            // Rename config key: PostProcessing -> Processing
            migrationBuilder.Sql("UPDATE Config SET Id = 'Processing' WHERE Id = 'PostProcessing';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE Config SET Id = 'PostProcessing' WHERE Id = 'Processing';");

            migrationBuilder.Sql("""
                UPDATE Config
                SET Value = REPLACE(REPLACE(Value,
                    '"PostProcessingEnabled":', '"Enabled":'),
                    '"PostProcessingCommand":', '"Command":')
                WHERE Id = 'PostProcessing';
                """);
        }
    }
}
