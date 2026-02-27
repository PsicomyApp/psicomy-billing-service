namespace Psicomy.Services.Billing.Options;

public class StripeOptions
{
    public const string SectionName = "Stripe";

    public string SecretKey { get; set; } = string.Empty;
    public string PublishableKey { get; set; } = string.Empty;
    public string WebhookSecret { get; set; } = string.Empty;
    public string DestinationId { get; set; } = string.Empty;
    public string? ApiVersion { get; set; }
    public int TimeoutSeconds { get; set; } = 30;
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// When true, creates Stripe Products and Prices on startup for plans that don't have them yet.
    /// Set via STRIPE_SEED_PRODUCTS environment variable.
    /// </summary>
    public bool SeedProducts { get; set; }
}
