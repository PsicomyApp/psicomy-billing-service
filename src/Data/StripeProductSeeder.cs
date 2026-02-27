using Microsoft.EntityFrameworkCore;
using Psicomy.Services.Billing.Models;
using Psicomy.Services.Billing.Options;
using Stripe;

namespace Psicomy.Services.Billing.Data;

/// <summary>
/// Creates Stripe Products and Prices for each PaymentPlan, storing the resulting IDs in the database.
/// Runs once at startup when STRIPE_SEED_PRODUCTS=true and a valid Stripe secret key is configured.
/// Existing active subscriptions are NOT modified — only new checkouts use the new prices.
/// </summary>
public class StripeProductSeeder
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<StripeProductSeeder> _logger;

    public StripeProductSeeder(
        IServiceProvider serviceProvider,
        ILogger<StripeProductSeeder> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        var stripeOptions = scope.ServiceProvider.GetRequiredService<StripeOptions>();

        if (string.IsNullOrEmpty(stripeOptions.SecretKey) ||
            stripeOptions.SecretKey.StartsWith("sk_test_change"))
        {
            _logger.LogWarning("Stripe secret key not configured. Skipping Stripe product seeding.");
            return;
        }

        var plans = await context.PaymentPlans
            .Where(p => p.IsActive)
            .OrderBy(p => p.MonthlyPrice)
            .ToListAsync(cancellationToken);

        _logger.LogInformation("Starting Stripe product/price seeding for {Count} plans", plans.Count);

        foreach (var plan in plans)
        {
            try
            {
                await SeedPlanAsync(plan, cancellationToken);
            }
            catch (StripeException ex)
            {
                _logger.LogError(ex, "Stripe API error seeding plan {PlanName}: {Message}", plan.Name, ex.Message);
            }
        }

        await context.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Stripe product/price seeding completed");
    }

    private async Task SeedPlanAsync(PaymentPlan plan, CancellationToken cancellationToken)
    {
        // Skip free plans (Student)
        if (plan.MonthlyPrice == 0 && (plan.YearlyPrice ?? 0) == 0)
        {
            _logger.LogInformation("Skipping free plan {PlanName}", plan.Name);
            return;
        }

        // If product already exists in Stripe, skip creation
        if (!string.IsNullOrEmpty(plan.StripeProductId))
        {
            _logger.LogInformation("Plan {PlanName} already has StripeProductId {ProductId}, checking prices",
                plan.Name, plan.StripeProductId);
            await EnsurePricesExist(plan, cancellationToken);
            return;
        }

        // Create Stripe Product
        var productService = new ProductService();
        var product = await productService.CreateAsync(new ProductCreateOptions
        {
            Name = $"Psicomy - {plan.Name}",
            Description = plan.Description,
            Metadata = new Dictionary<string, string>
            {
                { "plan_id", plan.Id.ToString() },
                { "tier", plan.Tier }
            }
        }, cancellationToken: cancellationToken);

        plan.StripeProductId = product.Id;
        _logger.LogInformation("Created Stripe Product {ProductId} for plan {PlanName}", product.Id, plan.Name);

        await EnsurePricesExist(plan, cancellationToken);
    }

    private async Task EnsurePricesExist(PaymentPlan plan, CancellationToken cancellationToken)
    {
        var priceService = new PriceService();

        // Monthly price
        if (string.IsNullOrEmpty(plan.StripePriceIdMonthly) && plan.MonthlyPrice > 0)
        {
            var monthlyPrice = await priceService.CreateAsync(new PriceCreateOptions
            {
                Product = plan.StripeProductId,
                UnitAmount = (long)(plan.MonthlyPrice * 100),
                Currency = "brl",
                Recurring = new PriceRecurringOptions
                {
                    Interval = "month"
                },
                Metadata = new Dictionary<string, string>
                {
                    { "plan_id", plan.Id.ToString() },
                    { "tier", plan.Tier },
                    { "period", "monthly" }
                }
            }, cancellationToken: cancellationToken);

            plan.StripePriceIdMonthly = monthlyPrice.Id;
            _logger.LogInformation("Created monthly price {PriceId} for {PlanName}: R${Amount}/mo",
                monthlyPrice.Id, plan.Name, plan.MonthlyPrice);
        }

        // Annual price
        if (string.IsNullOrEmpty(plan.StripePriceIdYearly) && (plan.YearlyPrice ?? 0) > 0)
        {
            var yearlyPrice = await priceService.CreateAsync(new PriceCreateOptions
            {
                Product = plan.StripeProductId,
                UnitAmount = (long)(plan.YearlyPrice!.Value * 100),
                Currency = "brl",
                Recurring = new PriceRecurringOptions
                {
                    Interval = "year"
                },
                Metadata = new Dictionary<string, string>
                {
                    { "plan_id", plan.Id.ToString() },
                    { "tier", plan.Tier },
                    { "period", "annual" }
                }
            }, cancellationToken: cancellationToken);

            plan.StripePriceIdYearly = yearlyPrice.Id;
            _logger.LogInformation("Created annual price {PriceId} for {PlanName}: R${Amount}/yr",
                yearlyPrice.Id, plan.Name, plan.YearlyPrice);
        }

        // Per-seat addon price (EnterprisePlus only)
        if (plan.Tier == "EnterprisePlus" &&
            string.IsNullOrEmpty(plan.StripePriceIdPerSeat) &&
            (plan.ExtraSeatPrice ?? 0) > 0)
        {
            var perSeatPrice = await priceService.CreateAsync(new PriceCreateOptions
            {
                Product = plan.StripeProductId,
                UnitAmount = (long)(plan.ExtraSeatPrice!.Value * 100),
                Currency = "brl",
                Recurring = new PriceRecurringOptions
                {
                    Interval = "month"
                },
                Metadata = new Dictionary<string, string>
                {
                    { "plan_id", plan.Id.ToString() },
                    { "tier", plan.Tier },
                    { "type", "per_seat" },
                    { "included_users", plan.IncludedUsers.ToString() }
                }
            }, cancellationToken: cancellationToken);

            plan.StripePriceIdPerSeat = perSeatPrice.Id;
            _logger.LogInformation(
                "Created per-seat price {PriceId} for {PlanName}: R${Amount}/user/mo (beyond {Included} users)",
                perSeatPrice.Id, plan.Name, plan.ExtraSeatPrice, plan.IncludedUsers);
        }

        plan.UpdatedAt = DateTime.UtcNow;
    }
}
