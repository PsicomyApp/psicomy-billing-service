namespace Psicomy.Services.Billing.Models;

public class PaymentInvoice
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public Guid LicenseId { get; set; }
    public TenantLicense? License { get; set; }

    public string? StripeInvoiceId { get; set; }
    public string? StripePaymentIntentId { get; set; }

    public decimal Amount { get; set; }
    public string Currency { get; set; } = "brl";
    public string Status { get; set; } = "pending";

    public DateTime? PaidAt { get; set; }
    public DateTime? DueDate { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
