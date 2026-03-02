using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Psicomy.Services.Billing.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGracePeriodFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PaymentRetryCount",
                table: "TenantLicenses",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "LastPaymentError",
                table: "TenantLicenses",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "GracePeriodEndsAt",
                table: "TenantLicenses",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PaymentRetryCount",
                table: "TenantLicenses");

            migrationBuilder.DropColumn(
                name: "LastPaymentError",
                table: "TenantLicenses");

            migrationBuilder.DropColumn(
                name: "GracePeriodEndsAt",
                table: "TenantLicenses");
        }
    }
}
