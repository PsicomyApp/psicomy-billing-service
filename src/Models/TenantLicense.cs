namespace Psicomy.Services.Billing.Models;

public class TenantLicense
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public Guid PlanId { get; set; }
    public PaymentPlan? Plan { get; set; }

    public DateTime LicenseStartDate { get; set; }
    public DateTime LicenseEndDate { get; set; }
    public bool AutoRenew { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? LastCheckedAt { get; set; }
    public DateTime? TrialEndDate { get; set; }

    public string PaymentMethod { get; set; } = "card";
    public string? PaymentMethodLast4 { get; set; }

    public string? StripeCustomerId { get; set; }
    public string? StripeSubscriptionId { get; set; }

    public string Status { get; set; } = "trial";
    public DateTime? ExpiresAt { get; set; }
    public DateTime? LastPaymentDate { get; set; }
    public DateTime? CancelledAt { get; set; }

    public int PaymentRetryCount { get; set; }
    public string? LastPaymentError { get; set; }
    public DateTime? GracePeriodEndsAt { get; set; }

    /// <summary>
    /// Tracks whether this tenant has already used their one-time paid plan trial extension.
    /// Once true, no further trial extensions are available.
    /// </summary>
    public bool HasUsedPaidTrialExtension { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
