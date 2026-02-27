using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Psicomy.Services.Billing.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PaymentPlans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Tier = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    MonthlyPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    YearlyPrice = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    MaxUsers = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    StripePriceIdMonthly = table.Column<string>(type: "text", nullable: true),
                    StripePriceIdYearly = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentPlans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TenantLicenses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    LicenseStartDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LicenseEndDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AutoRenew = table.Column<bool>(type: "boolean", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    LastCheckedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TrialEndDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PaymentMethod = table.Column<string>(type: "text", nullable: false),
                    PaymentMethodLast4 = table.Column<string>(type: "text", nullable: true),
                    StripeCustomerId = table.Column<string>(type: "text", nullable: true),
                    StripeSubscriptionId = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastPaymentDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantLicenses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TenantLicenses_PaymentPlans_PlanId",
                        column: x => x.PlanId,
                        principalTable: "PaymentPlans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PaymentInvoices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    LicenseId = table.Column<Guid>(type: "uuid", nullable: false),
                    StripeInvoiceId = table.Column<string>(type: "text", nullable: true),
                    StripePaymentIntentId = table.Column<string>(type: "text", nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    PaidAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DueDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentInvoices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentInvoices_TenantLicenses_LicenseId",
                        column: x => x.LicenseId,
                        principalTable: "TenantLicenses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "PaymentPlans",
                columns: new[] { "Id", "CreatedAt", "Description", "IsActive", "MaxUsers", "MonthlyPrice", "Name", "StripePriceIdMonthly", "StripePriceIdYearly", "Tier", "UpdatedAt", "YearlyPrice" },
                values: new object[,]
                {
                    { new Guid("11111111-1111-1111-1111-111111111111"), new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Plano gratuito para estudantes", true, 1, 0m, "Student", null, null, "Student", new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 0m },
                    { new Guid("22222222-2222-2222-2222-222222222222"), new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Plano individual para profissionais", true, 1, 39.90m, "Basic Individual", null, null, "BasicIndividual", new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 399.00m },
                    { new Guid("33333333-3333-3333-3333-333333333333"), new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Plano profissional com financeiro", true, 1, 79.90m, "Basic Pro", null, null, "BasicPro", new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 799.00m },
                    { new Guid("44444444-4444-4444-4444-444444444444"), new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Plano empresarial com multi-usuarios", true, 5, 159.90m, "Enterprise Basic", null, null, "EnterpriseBasic", new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 1599.00m },
                    { new Guid("55555555-5555-5555-5555-555555555555"), new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Plano empresarial completo", true, 15, 299.90m, "Enterprise Pro", null, null, "EnterprisePro", new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 2999.00m },
                    { new Guid("66666666-6666-6666-6666-666666666666"), new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), "Plano empresarial ilimitado", true, -1, 499.90m, "Enterprise Plus", null, null, "EnterprisePlus", new DateTime(2024, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc), 4999.00m }
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentInvoices_LicenseId",
                table: "PaymentInvoices",
                column: "LicenseId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentInvoices_StripeInvoiceId",
                table: "PaymentInvoices",
                column: "StripeInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentInvoices_TenantId",
                table: "PaymentInvoices",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TenantLicenses_PlanId",
                table: "TenantLicenses",
                column: "PlanId");

            migrationBuilder.CreateIndex(
                name: "IX_TenantLicenses_StripeCustomerId",
                table: "TenantLicenses",
                column: "StripeCustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_TenantLicenses_StripeSubscriptionId",
                table: "TenantLicenses",
                column: "StripeSubscriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_TenantLicenses_TenantId",
                table: "TenantLicenses",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_TenantLicenses_TenantId_IsActive",
                table: "TenantLicenses",
                columns: new[] { "TenantId", "IsActive" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaymentInvoices");

            migrationBuilder.DropTable(
                name: "TenantLicenses");

            migrationBuilder.DropTable(
                name: "PaymentPlans");
        }
    }
}
