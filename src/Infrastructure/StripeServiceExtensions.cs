using Stripe;

namespace Psicomy.Services.Billing.Infrastructure;

public static class StripeServiceExtensions
{
    public static IServiceCollection AddStripeServices(this IServiceCollection services, IConfiguration configuration)
    {
        var stripeSettings = new StripeSettings();
        configuration.GetSection(StripeSettings.SectionName).Bind(stripeSettings);

        // Check environment variables as fallback
        if (string.IsNullOrEmpty(stripeSettings.SecretKey))
            stripeSettings.SecretKey = Environment.GetEnvironmentVariable("STRIPE_SECRET_KEY") ?? string.Empty;
        if (string.IsNullOrEmpty(stripeSettings.WebhookSecret))
            stripeSettings.WebhookSecret = Environment.GetEnvironmentVariable("STRIPE_WEBHOOK_SECRET") ?? string.Empty;
        if (string.IsNullOrEmpty(stripeSettings.PublishableKey))
            stripeSettings.PublishableKey = Environment.GetEnvironmentVariable("STRIPE_PUBLISHABLE_KEY") ?? string.Empty;

        services.AddSingleton(stripeSettings);

        // Configure Stripe API key globally
        if (!string.IsNullOrEmpty(stripeSettings.SecretKey))
        {
            StripeConfiguration.ApiKey = stripeSettings.SecretKey;
        }

        return services;
    }
}
