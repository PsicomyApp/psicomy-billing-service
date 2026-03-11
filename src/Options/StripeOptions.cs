namespace Psicomy.Services.Billing.Options;

public class StripeOptions
{
    public const string SectionName = "Stripe";

    public string SecretKey { get; set; } = string.Empty;
    public string PublishableKey { get; set; } = string.Empty;
    public string WebhookSecret { get; set; } = string.Empty;
    public string DestinationId { get; set; } = string.Empty;
    public string DefaultFrontendUrl { get; set; } = "https://psicomy.com.br";
    public string[] AllowedRedirectHosts { get; set; } = [];

    /// <summary>
    /// Default Stripe Connect application fee percentage (range: 5-10%).
    /// Can be overridden per-plan via PaymentPlan.ConnectFeePercent.
    /// Set via STRIPE_CONNECT_FEE_PERCENT environment variable. Default: 8%.
    /// </summary>
    public decimal ApplicationFeePercent { get; set; } = 8m;

    public string? ApiVersion { get; set; }
    public int TimeoutSeconds { get; set; } = 30;
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Number of days after license ExpiresAt before full deactivation.
    /// Set via BILLING_GRACE_PERIOD_DAYS environment variable. Default: 10.
    /// </summary>
    public int GracePeriodDays { get; set; } = 10;

    /// <summary>
    /// When true, creates Stripe Products and Prices on startup for plans that don't have them yet.
    /// Set via STRIPE_SEED_PRODUCTS environment variable.
    /// </summary>
    public bool SeedProducts { get; set; }
}
