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
            migrationBuilder.AddColumn<bool>(
                name: "HasUsedPaidTrialExtension",
                table: "TenantLicenses",
                type: "boolean",
                nullable: false,
                defaultValue: false);
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
