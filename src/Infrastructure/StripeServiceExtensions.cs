using Stripe;

using Psicomy.Services.Billing.Options;

namespace Psicomy.Services.Billing.Infrastructure;

public static class StripeServiceExtensions
{
    public static IServiceCollection AddStripeServices(this IServiceCollection services, IConfiguration configuration)
    {
        var stripeOptions = new StripeOptions();
        configuration.GetSection(StripeOptions.SectionName).Bind(stripeOptions);

        // Check environment variables as fallback
        if (string.IsNullOrEmpty(stripeOptions.SecretKey))
            stripeOptions.SecretKey = Environment.GetEnvironmentVariable("STRIPE_SECRET_KEY") ?? string.Empty;
        if (string.IsNullOrEmpty(stripeOptions.WebhookSecret))
            stripeOptions.WebhookSecret = Environment.GetEnvironmentVariable("STRIPE_WEBHOOK_SECRET") ?? string.Empty;
        if (string.IsNullOrEmpty(stripeOptions.PublishableKey))
            stripeOptions.PublishableKey = Environment.GetEnvironmentVariable("STRIPE_PUBLISHABLE_KEY") ?? string.Empty;
        if (string.IsNullOrEmpty(stripeOptions.DestinationId))
            stripeOptions.DestinationId = Environment.GetEnvironmentVariable("STRIPE_DESTINATION_ID") ?? string.Empty;

        var connectFeePercent = Environment.GetEnvironmentVariable("STRIPE_CONNECT_FEE_PERCENT");
        if (!string.IsNullOrEmpty(connectFeePercent) && decimal.TryParse(connectFeePercent, out var feePercent))
            stripeOptions.ApplicationFeePercent = Math.Clamp(feePercent, 5m, 10m);

        var gracePeriodDays = Environment.GetEnvironmentVariable("BILLING_GRACE_PERIOD_DAYS");
        if (!string.IsNullOrEmpty(gracePeriodDays) && int.TryParse(gracePeriodDays, out var days))
            stripeOptions.GracePeriodDays = Math.Max(days, 1);

        var seedProducts = Environment.GetEnvironmentVariable("STRIPE_SEED_PRODUCTS");
        if (!string.IsNullOrEmpty(seedProducts))
            stripeOptions.SeedProducts = string.Equals(seedProducts, "true", StringComparison.OrdinalIgnoreCase);

        services.Configure<StripeOptions>(configuration.GetSection(StripeOptions.SectionName));
        services.AddSingleton(stripeOptions);

        // Configure Stripe API key globally
        if (!string.IsNullOrEmpty(stripeOptions.SecretKey))
        {
            StripeConfiguration.ApiKey = stripeOptions.SecretKey;
        }

        return services;
    }
}
