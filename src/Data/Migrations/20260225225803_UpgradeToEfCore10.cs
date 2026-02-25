using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Psicomy.Services.Billing.Data.Migrations
{
    /// <inheritdoc />
    public partial class UpgradeToEfCore10 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "StripePriceIdYearly",
                table: "PaymentPlans",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "StripePriceIdMonthly",
                table: "PaymentPlans",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.UpdateData(
                table: "PaymentPlans",
                keyColumn: "Id",
                keyValue: new Guid("22222222-2222-2222-2222-222222222222"),
                columns: new[] { "StripePriceIdMonthly", "StripePriceIdYearly" },
                values: new object[] { "price_1T4Ua5DaFYi3dWwbt7W5D8z0", "price_1T4Ua6DaFYi3dWwbXEEPW2Kt" });

            migrationBuilder.UpdateData(
                table: "PaymentPlans",
                keyColumn: "Id",
                keyValue: new Guid("33333333-3333-3333-3333-333333333333"),
                columns: new[] { "StripePriceIdMonthly", "StripePriceIdYearly" },
                values: new object[] { "price_1T4Ua6DaFYi3dWwbL3RCml0z", "price_1T4Ua6DaFYi3dWwbdvNFQSlt" });

            migrationBuilder.UpdateData(
                table: "PaymentPlans",
                keyColumn: "Id",
                keyValue: new Guid("44444444-4444-4444-4444-444444444444"),
                columns: new[] { "StripePriceIdMonthly", "StripePriceIdYearly" },
                values: new object[] { "price_1T4Ua7DaFYi3dWwbBpfUi8wT", "price_1T4Ua7DaFYi3dWwbE6X3dkJb" });

            migrationBuilder.UpdateData(
                table: "PaymentPlans",
                keyColumn: "Id",
                keyValue: new Guid("55555555-5555-5555-5555-555555555555"),
                columns: new[] { "StripePriceIdMonthly", "StripePriceIdYearly" },
                values: new object[] { "price_1T4Ua8DaFYi3dWwblNmhEDm8", "price_1T4Ua9DaFYi3dWwbgi1r39EL" });

            migrationBuilder.UpdateData(
                table: "PaymentPlans",
                keyColumn: "Id",
                keyValue: new Guid("66666666-6666-6666-6666-666666666666"),
                columns: new[] { "StripePriceIdMonthly", "StripePriceIdYearly" },
                values: new object[] { "price_1T4UaADaFYi3dWwbzSmmgMOq", "price_1T4UaBDaFYi3dWwbzP5ZV6yn" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "StripePriceIdYearly",
                table: "PaymentPlans",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "StripePriceIdMonthly",
                table: "PaymentPlans",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.UpdateData(
                table: "PaymentPlans",
                keyColumn: "Id",
                keyValue: new Guid("22222222-2222-2222-2222-222222222222"),
                columns: new[] { "StripePriceIdMonthly", "StripePriceIdYearly" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "PaymentPlans",
                keyColumn: "Id",
                keyValue: new Guid("33333333-3333-3333-3333-333333333333"),
                columns: new[] { "StripePriceIdMonthly", "StripePriceIdYearly" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "PaymentPlans",
                keyColumn: "Id",
                keyValue: new Guid("44444444-4444-4444-4444-444444444444"),
                columns: new[] { "StripePriceIdMonthly", "StripePriceIdYearly" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "PaymentPlans",
                keyColumn: "Id",
                keyValue: new Guid("55555555-5555-5555-5555-555555555555"),
                columns: new[] { "StripePriceIdMonthly", "StripePriceIdYearly" },
                values: new object[] { null, null });

            migrationBuilder.UpdateData(
                table: "PaymentPlans",
                keyColumn: "Id",
                keyValue: new Guid("66666666-6666-6666-6666-666666666666"),
                columns: new[] { "StripePriceIdMonthly", "StripePriceIdYearly" },
                values: new object[] { null, null });
        }
    }
}
