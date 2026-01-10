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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<TenantLicense>(builder =>
        {
            builder.ToTable("TenantLicenses");
            builder.HasKey(e => e.Id);
            builder.HasIndex(e => e.TenantId);
            builder.HasIndex(e => e.StripeCustomerId);
            builder.HasIndex(e => e.StripeSubscriptionId);
            builder.HasIndex(e => new { e.TenantId, e.IsActive });

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
        });

        modelBuilder.Entity<PaymentInvoice>(builder =>
        {
            builder.ToTable("PaymentInvoices");
            builder.HasKey(e => e.Id);
            builder.HasIndex(e => e.TenantId);
            builder.HasIndex(e => e.StripeInvoiceId);
            builder.Property(e => e.Amount).HasPrecision(18, 2);
            builder.Property(e => e.Currency).HasMaxLength(3);

            builder.HasOne(e => e.License)
                .WithMany()
                .HasForeignKey(e => e.LicenseId);
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
                Name = "Student",
                Description = "Plano gratuito para estudantes",
                Tier = "Student",
                MonthlyPrice = 0,
                YearlyPrice = 0,
                MaxUsers = 1,
                IsActive = true,
                CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                UpdatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new PaymentPlan
            {
                Id = Guid.Parse("22222222-2222-2222-2222-222222222222"),
                Name = "Basic Individual",
                Description = "Plano individual para profissionais",
                Tier = "BasicIndividual",
                MonthlyPrice = 39.90m,
                YearlyPrice = 399.00m,
                MaxUsers = 1,
                IsActive = true,
                CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                UpdatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new PaymentPlan
            {
                Id = Guid.Parse("33333333-3333-3333-3333-333333333333"),
                Name = "Basic Pro",
                Description = "Plano profissional com financeiro",
                Tier = "BasicPro",
                MonthlyPrice = 79.90m,
                YearlyPrice = 799.00m,
                MaxUsers = 1,
                IsActive = true,
                CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                UpdatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new PaymentPlan
            {
                Id = Guid.Parse("44444444-4444-4444-4444-444444444444"),
                Name = "Enterprise Basic",
                Description = "Plano empresarial com multi-usuarios",
                Tier = "EnterpriseBasic",
                MonthlyPrice = 159.90m,
                YearlyPrice = 1599.00m,
                MaxUsers = 5,
                IsActive = true,
                CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                UpdatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new PaymentPlan
            {
                Id = Guid.Parse("55555555-5555-5555-5555-555555555555"),
                Name = "Enterprise Pro",
                Description = "Plano empresarial completo",
                Tier = "EnterprisePro",
                MonthlyPrice = 299.90m,
                YearlyPrice = 2999.00m,
                MaxUsers = 15,
                IsActive = true,
                CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                UpdatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new PaymentPlan
            {
                Id = Guid.Parse("66666666-6666-6666-6666-666666666666"),
                Name = "Enterprise Plus",
                Description = "Plano empresarial ilimitado",
                Tier = "EnterprisePlus",
                MonthlyPrice = 499.90m,
                YearlyPrice = 4999.00m,
                MaxUsers = -1, // Unlimited
                IsActive = true,
                CreatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                UpdatedAt = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            }
        );
    }
}
