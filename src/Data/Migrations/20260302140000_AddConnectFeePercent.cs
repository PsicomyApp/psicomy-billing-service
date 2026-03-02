using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Psicomy.Services.Billing.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddConnectFeePercent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ConnectFeePercent",
                table: "PaymentPlans",
                type: "numeric(5,2)",
                precision: 5,
                scale: 2,
                nullable: true);

            // Set default fee for EnterprisePro and EnterprisePlus
            migrationBuilder.UpdateData(
                table: "PaymentPlans",
                keyColumn: "Id",
                keyValue: new Guid("55555555-5555-5555-5555-555555555555"),
                column: "ConnectFeePercent",
                value: 8m);

            migrationBuilder.UpdateData(
                table: "PaymentPlans",
                keyColumn: "Id",
                keyValue: new Guid("66666666-6666-6666-6666-666666666666"),
                column: "ConnectFeePercent",
                value: 8m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ConnectFeePercent",
                table: "PaymentPlans");
        }
    }
}
