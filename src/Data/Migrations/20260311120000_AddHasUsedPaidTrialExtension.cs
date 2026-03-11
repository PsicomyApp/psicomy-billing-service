using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Psicomy.Services.Billing.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddHasUsedPaidTrialExtension : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Idempotent: column may already exist from a previous partial migration attempt
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF NOT EXISTS (
                        SELECT 1 FROM information_schema.columns
                        WHERE table_name = 'TenantLicenses'
                        AND column_name = 'HasUsedPaidTrialExtension'
                    ) THEN
                        ALTER TABLE "TenantLicenses"
                        ADD COLUMN "HasUsedPaidTrialExtension" boolean NOT NULL DEFAULT false;
                    END IF;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "HasUsedPaidTrialExtension",
                table: "TenantLicenses");
        }
    }
}
