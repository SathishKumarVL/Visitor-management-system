using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Tiaano.Vms.Api.Data.Migrations;

/// <summary>
/// LocationId on FeedbackQuestions. Same as backend/AddFeedbackQuestionLocation.sql;
/// idempotent so databases that already ran the script are unaffected.
/// </summary>
[DbContext(typeof(ApplicationDbContext))]
[Migration("20260926110000_AddFeedbackQuestionLocation")]
public partial class AddFeedbackQuestionLocation : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            IF COL_LENGTH('dbo.FeedbackQuestions', 'LocationId') IS NULL
              ALTER TABLE dbo.FeedbackQuestions ADD LocationId uniqueidentifier NULL;
            """);
        migrationBuilder.Sql("""
            IF NOT EXISTS (SELECT 1 FROM sys.foreign_keys WHERE name = N'FK_FeedbackQuestions_Locations_LocationId')
              ALTER TABLE dbo.FeedbackQuestions WITH CHECK
                ADD CONSTRAINT FK_FeedbackQuestions_Locations_LocationId
                FOREIGN KEY (LocationId) REFERENCES dbo.Locations (Id);

            IF NOT EXISTS (SELECT 1 FROM sys.indexes
                           WHERE name = N'IX_FeedbackQuestions_LocationId' AND object_id = OBJECT_ID(N'dbo.FeedbackQuestions'))
              CREATE INDEX IX_FeedbackQuestions_LocationId ON dbo.FeedbackQuestions (LocationId);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Non-destructive: leave location column in place.
    }
}
