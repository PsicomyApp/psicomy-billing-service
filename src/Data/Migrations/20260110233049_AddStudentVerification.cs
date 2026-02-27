using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Psicomy.Services.Billing.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStudentVerification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StudentVerifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    FullName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    InstitutionName = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    CourseName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ExpectedGraduationYear = table.Column<int>(type: "integer", nullable: true),
                    DocumentFileName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    DocumentStoragePath = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    DocumentContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DocumentSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    RejectionReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReviewedBy = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    IsBlocked = table.Column<bool>(type: "boolean", nullable: false),
                    BlockedUntil = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentVerifications", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StudentVerifications_Email",
                table: "StudentVerifications",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_StudentVerifications_TenantId",
                table: "StudentVerifications",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentVerifications_UserId",
                table: "StudentVerifications",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_StudentVerifications_UserId_CreatedAt",
                table: "StudentVerifications",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_StudentVerifications_UserId_Status",
                table: "StudentVerifications",
                columns: new[] { "UserId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StudentVerifications");
        }
    }
}
