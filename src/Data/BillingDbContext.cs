using Microsoft.EntityFrameworkCore;
using Psicomy.Services.Billing.Models;

namespace Psicomy.Services.Billing.Data;

public class BillingDbContext : DbContext
{
    public BillingDbContext(DbContextOptions<BillingDbContext> options) : base(options)
    {
    }

    public DbSet<TenantLicense> TenantLicenses => Set<TenantLicense>();
    public DbSet<PaymentPlan> PaymentPlans => Set<PaymentPlan>();
    public DbSet<PaymentInvoice> PaymentInvoices => Set<PaymentInvoice>();
    public DbSet<AcademicVerification> AcademicVerifications => Set<AcademicVerification>();
    public DbSet<ProcessedStripeEvent> ProcessedStripeEvents => Set<ProcessedStripeEvent>();
    public DbSet<SentTrialReminder> SentTrialReminders => Set<SentTrialReminder>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<ProcessedStripeEvent>(builder =>
        {
            builder.ToTable("ProcessedStripeEvents");
            builder.HasKey(e => e.EventId);
            builder.Property(e => e.EventId).HasMaxLength(255);
            builder.Property(e => e.EventType).HasMaxLength(100);
        });

        modelBuilder.Entity<TenantLicense>(builder =>
        {
            builder.ToTable("TenantLicenses");
            builder.HasKey(e => e.Id);
            builder.HasIndex(e => e.TenantId);
            builder.HasIndex(e => e.StripeCustomerId);
            builder.HasIndex(e => e.StripeSubscriptionId);
            builder.HasIndex(e => new { e.TenantId, e.IsActive });

            builder.Property(e => e.LastPaymentError).HasMaxLength(500);

            builder.HasOne(e => e.Plan)
                .WithMany(p => p.Licenses)
                .HasForeignKey(e => e.PlanId);
        });

        modelBuilder.Entity<PaymentPlan>(builder =>
        {
            builder.ToTable("PaymentPlans");
            builder.HasKey(e => e.Id);
            builder.Property(e => e.Name).HasMaxLength(100);
            builder.Property(e => e.Tier).HasMaxLength(50);
            builder.Property(e => e.MonthlyPrice).HasPrecision(18, 2);
            builder.Property(e => e.YearlyPrice).HasPrecision(18, 2);
            builder.Property(e => e.ExtraSeatPrice).HasPrecision(18, 2);
            builder.Property(e => e.ConnectFeePercent).HasPrecision(5, 2);
            builder.Property(e => e.StripeProductId).HasMaxLength(100);
            builder.Property(e => e.StripePriceIdMonthly).HasMaxLength(100);
            builder.Property(e => e.StripePriceIdYearly).HasMaxLength(100);
            builder.Property(e => e.StripePriceIdPerSeat).HasMaxLength(100);
            builder.Property(e => e.StripePriceIdPerSeatYearly).HasMaxLength(100);
        });

        modelBuilder.Entity<PaymentInvoice>(builder =>
        {
            builder.ToTable("PaymentInvoices");
            builder.HasKey(e => e.Id);
            builder.HasIndex(e => e.TenantId);
            builder.HasIndex(e => e.StripeInvoiceId);
            builder.Property(e => e.Amount).HasPrecision(18, 2);
            builder.Property(e => e.Currency).HasMaxLength(3);
            builder.Property(e => e.PaymentMethodType).HasMaxLength(20);

            builder.HasOne(e => e.License)
                .WithMany()
                .HasForeignKey(e => e.LicenseId);
        });

        modelBuilder.Entity<SentTrialReminder>(builder =>
        {
            builder.ToTable("SentTrialReminders");
            builder.HasKey(e => e.Id);
            builder.HasIndex(e => new { e.LicenseId, e.MilestoneDay }).IsUnique();
            builder.Property(e => e.TenantId).HasMaxLength(200);
        });

        modelBuilder.Entity<AcademicVerification>(builder =>
        {
            // Keep table name for backward compatibility with existing migrations
            builder.ToTable("StudentVerifications");
            builder.HasKey(e => e.Id);
            builder.HasIndex(e => e.TenantId);
            builder.HasIndex(e => e.UserId);
            builder.HasIndex(e => e.Email);
            builder.HasIndex(e => new { e.UserId, e.Status });
            builder.HasIndex(e => new { e.UserId, e.CreatedAt });
            builder.Property(e => e.FullName).HasMaxLength(200);
            builder.Property(e => e.Email).HasMaxLength(200);
            builder.Property(e => e.Phone).HasMaxLength(50);
            builder.Property(e => e.InstitutionName).HasMaxLength(300);
            builder.Property(e => e.CourseName).HasMaxLength(200);
            builder.Property(e => e.DocumentFileName).HasMaxLength(500);
            builder.Property(e => e.DocumentStoragePath).HasMaxLength(1000);
            builder.Property(e => e.DocumentContentType).HasMaxLength(100);
            builder.Property(e => e.Status).HasMaxLength(20);
            builder.Property(e => e.RejectionReason).HasMaxLength(1000);
            builder.Property(e => e.ReviewedBy).HasMaxLength(200);
        });

        // Seed default payment plans
        SeedPaymentPlans(modelBuilder);
    }

    private static void SeedPaymentPlans(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PaymentPlan>().HasData(
            new PaymentPlan
            {
                Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Name = "Free",
                Description = "Plano gratuito para estudantes e teste",
                Tier = "Free",
                MonthlyPrice = 0,
                YearlyPrice = 0,
                MaxUsers = 1,
                IncludedUsers = 1,
                IsActive = true,
                StripePriceIdMonthly = null,
                StripePriceIdYearly = null,
                CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                UpdatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new PaymentPlan
            {
                Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                Name = "Starter",
                Description = "Plano individual para profissionais",
                Tier = "Starter",
                MonthlyPrice = 49.90m,
                YearlyPrice = 499.00m,
                MaxUsers = 1,
                IncludedUsers = 1,
                IsActive = true,
                StripePriceIdMonthly = "price_1T7OGWGp0L8CI1tPdWz5QCW3",
                StripePriceIdYearly = "price_1T7OGWGp0L8CI1tPMmjd187s",
                CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                UpdatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new PaymentPlan
            {
                Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                Name = "Professional",
                Description = "Plano profissional com financeiro completo",
                Tier = "Professional",
                MonthlyPrice = 99.90m,
                YearlyPrice = 999.00m,
                MaxUsers = 1,
                IncludedUsers = 1,
                IsActive = true,
                StripePriceIdMonthly = "price_1T7OGTGp0L8CI1tPIlHoHb74",
                StripePriceIdYearly = "price_1T7OGTGp0L8CI1tPiNwYoCUW",
                CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                UpdatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new PaymentPlan
            {
                Id = Guid.Parse("44444444-4444-4444-4444-444444444444"),
                Name = "Team",
                Description = "Plano para equipes com multi-usuarios e RBAC",
                Tier = "Team",
                MonthlyPrice = 189.90m,
                YearlyPrice = 1899.00m,
                MaxUsers = -1,
                IncludedUsers = 5,
                ExtraSeatPrice = 49.90m,
                IsActive = true,
                StripePriceIdMonthly = "price_1T7OGQGp0L8CI1tPnXcGf2Vu",
                StripePriceIdYearly = "price_1T7OGQGp0L8CI1tPx3SDEXk1",
                StripePriceIdPerSeat = "price_1T7OGIGp0L8CI1tPZDxyDUry",
                StripePriceIdPerSeatYearly = "price_1T7OGIGp0L8CI1tPBRaBPfwH",
                CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                UpdatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new PaymentPlan
            {
                Id = Guid.Parse("55555555-5555-5555-5555-555555555555"),
                Name = "Business",
                Description = "Plano empresarial completo com BI e SLA",
                Tier = "Business",
                MonthlyPrice = 349.90m,
                YearlyPrice = 3499.00m,
                MaxUsers = -1,
                IncludedUsers = 15,
                ExtraSeatPrice = 59.90m,
                ConnectFeePercent = 8m,
                IsActive = true,
                StripePriceIdMonthly = "price_1T7OGMGp0L8CI1tPXeGWy9Od",
                StripePriceIdYearly = "price_1T7OGMGp0L8CI1tPWBzz9nLB",
                StripePriceIdPerSeat = "price_1T7OGIGp0L8CI1tPtsgP6U9l",
                StripePriceIdPerSeatYearly = "price_1T7OGIGp0L8CI1tPDb7TkUO5",
                CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                UpdatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new PaymentPlan
            {
                Id = Guid.Parse("66666666-6666-6666-6666-666666666666"),
                Name = "Enterprise",
                Description = "Plano enterprise com white label e gerente dedicado",
                Tier = "Enterprise",
                MonthlyPrice = 999.90m,
                YearlyPrice = 9999.00m,
                MaxUsers = -1,
                IncludedUsers = -1,
                ConnectFeePercent = 8m,
                IsActive = true,
                StripePriceIdMonthly = null,
                StripePriceIdYearly = null,
                CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                UpdatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            }
        );
    }
}
