using System.Globalization;
using Microsoft.Extensions.Options;
using Stripe;

using Psicomy.Services.Billing.Options;

namespace Psicomy.Services.Billing.Infrastructure;

public static class StripeServiceExtensions
{
    public static IServiceCollection AddStripeServices(this IServiceCollection services, IConfiguration configuration)
    {
        var environmentName = configuration["ASPNETCORE_ENVIRONMENT"]
            ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? "Production";
        var isDevelopment = string.Equals(environmentName, "Development", StringComparison.OrdinalIgnoreCase);

        services.AddOptions<StripeOptions>()
            .Configure(options =>
            {
                configuration.GetSection(StripeOptions.SectionName).Bind(options);
                ApplyEnvironmentOverrides(options);
                NormalizeRedirectHosts(options);
            })
            .Validate(
                options => isDevelopment || !string.IsNullOrWhiteSpace(options.SecretKey),
                "Stripe SecretKey must be configured outside Development.")
            .Validate(
                options => isDevelopment || !string.IsNullOrWhiteSpace(options.PublishableKey),
                "Stripe PublishableKey must be configured outside Development.")
            .Validate(
                options => isDevelopment || !string.IsNullOrWhiteSpace(options.WebhookSecret),
                "Stripe WebhookSecret must be configured outside Development.")
            .ValidateOnStart();

        services.PostConfigure<StripeOptions>(options =>
        {
            if (string.IsNullOrWhiteSpace(options.SecretKey))
            {
                return;
            }

            StripeConfiguration.ApiKey = options.SecretKey;
            StripeConfiguration.MaxNetworkRetries = Math.Max(0, options.MaxRetries);
        });

        // Keep direct injection compatibility in existing code paths.
        services.AddSingleton(sp => sp.GetRequiredService<IOptions<StripeOptions>>().Value);

        return services;
    }

    private static void ApplyEnvironmentOverrides(StripeOptions stripeOptions)
    {
        if (string.IsNullOrWhiteSpace(stripeOptions.SecretKey))
            stripeOptions.SecretKey = Environment.GetEnvironmentVariable("STRIPE_SECRET_KEY") ?? string.Empty;

        if (string.IsNullOrWhiteSpace(stripeOptions.WebhookSecret))
            stripeOptions.WebhookSecret = Environment.GetEnvironmentVariable("STRIPE_WEBHOOK_SECRET") ?? string.Empty;

        if (string.IsNullOrWhiteSpace(stripeOptions.PublishableKey))
            stripeOptions.PublishableKey = Environment.GetEnvironmentVariable("STRIPE_PUBLISHABLE_KEY") ?? string.Empty;

        if (string.IsNullOrWhiteSpace(stripeOptions.DestinationId))
            stripeOptions.DestinationId = Environment.GetEnvironmentVariable("STRIPE_DESTINATION_ID") ?? string.Empty;

        var defaultFrontendUrl = Environment.GetEnvironmentVariable("STRIPE_DEFAULT_FRONTEND_URL");
        if (!string.IsNullOrWhiteSpace(defaultFrontendUrl))
        {
            stripeOptions.DefaultFrontendUrl = defaultFrontendUrl.Trim();
        }

        var allowedRedirectHosts = Environment.GetEnvironmentVariable("STRIPE_ALLOWED_REDIRECT_HOSTS");
        if (!string.IsNullOrWhiteSpace(allowedRedirectHosts))
        {
            stripeOptions.AllowedRedirectHosts = ParseHostList(allowedRedirectHosts);
        }

        var connectFeePercent = Environment.GetEnvironmentVariable("STRIPE_CONNECT_FEE_PERCENT");
        if (!string.IsNullOrWhiteSpace(connectFeePercent) &&
            decimal.TryParse(connectFeePercent, NumberStyles.Number, CultureInfo.InvariantCulture, out var feePercent))
        {
            stripeOptions.ApplicationFeePercent = Math.Clamp(feePercent, 5m, 10m);
        }

        var gracePeriodDays = Environment.GetEnvironmentVariable("BILLING_GRACE_PERIOD_DAYS");
        if (!string.IsNullOrWhiteSpace(gracePeriodDays) && int.TryParse(gracePeriodDays, out var days))
        {
            stripeOptions.GracePeriodDays = Math.Max(days, 1);
        }

        var seedProducts = Environment.GetEnvironmentVariable("STRIPE_SEED_PRODUCTS");
        if (!string.IsNullOrWhiteSpace(seedProducts))
        {
            stripeOptions.SeedProducts = string.Equals(seedProducts, "true", StringComparison.OrdinalIgnoreCase);
        }
    }

    private static void NormalizeRedirectHosts(StripeOptions stripeOptions)
    {
        var hosts = (stripeOptions.AllowedRedirectHosts ?? [])
            .SelectMany(NormalizeHostCandidate)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (Uri.TryCreate(stripeOptions.DefaultFrontendUrl, UriKind.Absolute, out var frontendUri))
        {
            hosts.Add(frontendUri.Host.ToLowerInvariant());
            stripeOptions.DefaultFrontendUrl = frontendUri.GetLeftPart(UriPartial.Authority);
        }
        else
        {
            stripeOptions.DefaultFrontendUrl = "https://psicomy.com.br";
            hosts.Add("psicomy.com.br");
        }

        stripeOptions.AllowedRedirectHosts = hosts
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string[] ParseHostList(string value)
    {
        return value
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(host => host.ToLowerInvariant())
            .ToArray();
    }

    private static IEnumerable<string> NormalizeHostCandidate(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            yield break;
        }

        var trimmed = candidate.Trim();

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absoluteUri))
        {
            yield return absoluteUri.Host.ToLowerInvariant();
            yield break;
        }

        if (Uri.TryCreate($"https://{trimmed}", UriKind.Absolute, out var hostUri))
        {
            yield return hostUri.Host.ToLowerInvariant();
            yield break;
        }
    }
}
