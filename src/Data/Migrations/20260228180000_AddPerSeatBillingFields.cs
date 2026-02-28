using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Psicomy.Services.Billing.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPerSeatBillingFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ExtraSeatPrice",
                table: "PaymentPlans",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "IncludedUsers",
                table: "PaymentPlans",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "StripeProductId",
                table: "PaymentPlans",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StripePriceIdPerSeat",
                table: "PaymentPlans",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            // Update seed data: IncludedUsers for all plans
            migrationBuilder.UpdateData(
                table: "PaymentPlans",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111111"),
                column: "IncludedUsers",
                value: 1);

            migrationBuilder.UpdateData(
                table: "PaymentPlans",
                keyColumn: "Id",
                keyValue: new Guid("22222222-2222-2222-2222-222222222222"),
                column: "IncludedUsers",
                value: 1);

            migrationBuilder.UpdateData(
                table: "PaymentPlans",
                keyColumn: "Id",
                keyValue: new Guid("33333333-3333-3333-3333-333333333333"),
                column: "IncludedUsers",
                value: 1);

            // EnterpriseBasic: MaxUsers 5->8, IncludedUsers=8
            migrationBuilder.UpdateData(
                table: "PaymentPlans",
                keyColumn: "Id",
                keyValue: new Guid("44444444-4444-4444-4444-444444444444"),
                columns: new[] { "MaxUsers", "IncludedUsers" },
                values: new object[] { 8, 8 });

            // EnterprisePro: IncludedUsers=15
            migrationBuilder.UpdateData(
                table: "PaymentPlans",
                keyColumn: "Id",
                keyValue: new Guid("55555555-5555-5555-5555-555555555555"),
                column: "IncludedUsers",
                value: 15);

            // EnterprisePlus: updated pricing, IncludedUsers=15, ExtraSeatPrice=35
            migrationBuilder.UpdateData(
                table: "PaymentPlans",
                keyColumn: "Id",
                keyValue: new Guid("66666666-6666-6666-6666-666666666666"),
                columns: new[] { "MonthlyPrice", "YearlyPrice", "IncludedUsers", "ExtraSeatPrice" },
                values: new object[] { 349.90m, 3499.00m, 15, 35m });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Revert EnterprisePlus pricing
            migrationBuilder.UpdateData(
                table: "PaymentPlans",
                keyColumn: "Id",
                keyValue: new Guid("66666666-6666-6666-6666-666666666666"),
                columns: new[] { "MonthlyPrice", "YearlyPrice" },
                values: new object[] { 499.90m, 4999.00m });

            // Revert EnterpriseBasic MaxUsers
            migrationBuilder.UpdateData(
                table: "PaymentPlans",
                keyColumn: "Id",
                keyValue: new Guid("44444444-4444-4444-4444-444444444444"),
                column: "MaxUsers",
                value: 5);

            migrationBuilder.DropColumn(
                name: "ExtraSeatPrice",
                table: "PaymentPlans");

            migrationBuilder.DropColumn(
                name: "IncludedUsers",
                table: "PaymentPlans");

            migrationBuilder.DropColumn(
                name: "StripeProductId",
                table: "PaymentPlans");

            migrationBuilder.DropColumn(
                name: "StripePriceIdPerSeat",
                table: "PaymentPlans");
        }
    }
}
