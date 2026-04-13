using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Muxarr.Data.Migrations
{
    // TargetSnapshot JSON shape changed: MediaSnapshot/TrackSnapshot (observed,
    // fully populated) -> TargetSnapshot/TargetTrack (desired, nullable fields
    // with inherit semantics). Historical rows keep their JSON (extra fields
    // drop on deserialize, bools cast to bool? cleanly) and remain viewable.
    // Non-terminal rows are dropped so in-flight conversions re-queue against
    // the new planner rather than mis-interpreting old "every bool is explicit"
    // state as fresh opinions.
    public partial class TargetSnapshotRefactor : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DELETE FROM \"MediaConversion\" WHERE \"State\" IN ('New', 'Processing');");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
