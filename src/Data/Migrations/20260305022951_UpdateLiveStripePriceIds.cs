using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Psicomy.Services.Billing.Data.Migrations
{
    /// <inheritdoc />
    public partial class UpdateLiveStripePriceIds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "StripePriceIdPerSeatYearly",
                table: "PaymentPlans",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.UpdateData(
                table: "PaymentPlans",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111111"),
                columns: new[] { "Description", "Name", "StripePriceIdPerSeatYearly", "Tier" },
                values: new object[] { "Plano gratuito para estudantes e teste", "Free", null, "Free" });

            migrationBuilder.UpdateData(
                table: "PaymentPlans",
                keyColumn: "Id",
                keyValue: new Guid("22222222-2222-2222-2222-222222222222"),
                columns: new[] { "MonthlyPrice", "Name", "StripePriceIdMonthly", "StripePriceIdPerSeatYearly", "StripePriceIdYearly", "Tier", "YearlyPrice" },
                values: new object[] { 49.90m, "Starter", "price_1T7OGWGp0L8CI1tPdWz5QCW3", null, "price_1T7OGWGp0L8CI1tPMmjd187s", "Starter", 499.00m });

            migrationBuilder.UpdateData(
                table: "PaymentPlans",
                keyColumn: "Id",
                keyValue: new Guid("33333333-3333-3333-3333-333333333333"),
                columns: new[] { "Description", "MonthlyPrice", "Name", "StripePriceIdMonthly", "StripePriceIdPerSeatYearly", "StripePriceIdYearly", "Tier", "YearlyPrice" },
                values: new object[] { "Plano profissional com financeiro completo", 99.90m, "Professional", "price_1T7OGTGp0L8CI1tPIlHoHb74", null, "price_1T7OGTGp0L8CI1tPiNwYoCUW", "Professional", 999.00m });

            migrationBuilder.UpdateData(
                table: "PaymentPlans",
                keyColumn: "Id",
                keyValue: new Guid("44444444-4444-4444-4444-444444444444"),
                columns: new[] { "Description", "ExtraSeatPrice", "IncludedUsers", "MaxUsers", "MonthlyPrice", "Name", "StripePriceIdMonthly", "StripePriceIdPerSeat", "StripePriceIdPerSeatYearly", "StripePriceIdYearly", "Tier", "YearlyPrice" },
                values: new object[] { "Plano para equipes com multi-usuarios e RBAC", 49.90m, 5, -1, 189.90m, "Team", "price_1T7OGQGp0L8CI1tPnXcGf2Vu", "price_1T7OGIGp0L8CI1tPZDxyDUry", "price_1T7OGIGp0L8CI1tPBRaBPfwH", "price_1T7OGQGp0L8CI1tPx3SDEXk1", "Team", 1899.00m });

            migrationBuilder.UpdateData(
                table: "PaymentPlans",
                keyColumn: "Id",
                keyValue: new Guid("55555555-5555-5555-5555-555555555555"),
                columns: new[] { "Description", "ExtraSeatPrice", "MaxUsers", "MonthlyPrice", "Name", "StripePriceIdMonthly", "StripePriceIdPerSeat", "StripePriceIdPerSeatYearly", "StripePriceIdYearly", "Tier", "YearlyPrice" },
                values: new object[] { "Plano empresarial completo com BI e SLA", 59.90m, -1, 349.90m, "Business", "price_1T7OGMGp0L8CI1tPXeGWy9Od", "price_1T7OGIGp0L8CI1tPtsgP6U9l", "price_1T7OGIGp0L8CI1tPDb7TkUO5", "price_1T7OGMGp0L8CI1tPWBzz9nLB", "Business", 3499.00m });

            migrationBuilder.UpdateData(
                table: "PaymentPlans",
                keyColumn: "Id",
                keyValue: new Guid("66666666-6666-6666-6666-666666666666"),
                columns: new[] { "Description", "ExtraSeatPrice", "IncludedUsers", "MonthlyPrice", "Name", "StripePriceIdMonthly", "StripePriceIdPerSeatYearly", "StripePriceIdYearly", "Tier", "YearlyPrice" },
                values: new object[] { "Plano enterprise com white label e gerente dedicado", null, -1, 999.90m, "Enterprise", null, null, null, "Enterprise", 9999.00m });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StripePriceIdPerSeatYearly",
                table: "PaymentPlans");

            migrationBuilder.UpdateData(
                table: "PaymentPlans",
                keyColumn: "Id",
                keyValue: new Guid("11111111-1111-1111-1111-111111111111"),
                columns: new[] { "Description", "Name", "Tier" },
                values: new object[] { "Plano gratuito para estudantes", "Student", "Student" });

            migrationBuilder.UpdateData(
                table: "PaymentPlans",
                keyColumn: "Id",
                keyValue: new Guid("22222222-2222-2222-2222-222222222222"),
                columns: new[] { "MonthlyPrice", "Name", "StripePriceIdMonthly", "StripePriceIdYearly", "Tier", "YearlyPrice" },
                values: new object[] { 39.90m, "Basic Individual", "price_1T4Ua5DaFYi3dWwbt7W5D8z0", "price_1T4Ua6DaFYi3dWwbXEEPW2Kt", "BasicIndividual", 399.00m });

            migrationBuilder.UpdateData(
                table: "PaymentPlans",
                keyColumn: "Id",
                keyValue: new Guid("33333333-3333-3333-3333-333333333333"),
                columns: new[] { "Description", "MonthlyPrice", "Name", "StripePriceIdMonthly", "StripePriceIdYearly", "Tier", "YearlyPrice" },
                values: new object[] { "Plano profissional com financeiro", 79.90m, "Basic Pro", "price_1T4Ua6DaFYi3dWwbL3RCml0z", "price_1T4Ua6DaFYi3dWwbdvNFQSlt", "BasicPro", 799.00m });

            migrationBuilder.UpdateData(
                table: "PaymentPlans",
                keyColumn: "Id",
                keyValue: new Guid("44444444-4444-4444-4444-444444444444"),
                columns: new[] { "Description", "ExtraSeatPrice", "IncludedUsers", "MaxUsers", "MonthlyPrice", "Name", "StripePriceIdMonthly", "StripePriceIdPerSeat", "StripePriceIdYearly", "Tier", "YearlyPrice" },
                values: new object[] { "Plano empresarial com multi-usuarios", null, 8, 8, 159.90m, "Enterprise Basic", "price_1T4Ua7DaFYi3dWwbBpfUi8wT", null, "price_1T4Ua7DaFYi3dWwbE6X3dkJb", "EnterpriseBasic", 1599.00m });

            migrationBuilder.UpdateData(
                table: "PaymentPlans",
                keyColumn: "Id",
                keyValue: new Guid("55555555-5555-5555-5555-555555555555"),
                columns: new[] { "Description", "ExtraSeatPrice", "MaxUsers", "MonthlyPrice", "Name", "StripePriceIdMonthly", "StripePriceIdPerSeat", "StripePriceIdYearly", "Tier", "YearlyPrice" },
                values: new object[] { "Plano empresarial completo", null, 15, 299.90m, "Enterprise Pro", "price_1T4Ua8DaFYi3dWwblNmhEDm8", null, "price_1T4Ua9DaFYi3dWwbgi1r39EL", "EnterprisePro", 2999.00m });

            migrationBuilder.UpdateData(
                table: "PaymentPlans",
                keyColumn: "Id",
                keyValue: new Guid("66666666-6666-6666-6666-666666666666"),
                columns: new[] { "Description", "ExtraSeatPrice", "IncludedUsers", "MonthlyPrice", "Name", "StripePriceIdMonthly", "StripePriceIdYearly", "Tier", "YearlyPrice" },
                values: new object[] { "Plano empresarial ilimitado com cobranca variavel", 35m, 15, 349.90m, "Enterprise Plus", "price_1T4UaADaFYi3dWwbzSmmgMOq", "price_1T4UaBDaFYi3dWwbzP5ZV6yn", "EnterprisePlus", 3499.00m });
        }
    }
}
