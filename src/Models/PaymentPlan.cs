namespace Psicomy.Services.Billing.Models;

public class PaymentPlan
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string Tier { get; set; } = "Student";
    public decimal MonthlyPrice { get; set; }
    public decimal? YearlyPrice { get; set; }
    public int MaxUsers { get; set; } = 1;
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Stripe Product ID for this plan.
    /// </summary>
    public string? StripeProductId { get; set; }

    public string? StripePriceIdMonthly { get; set; }
    public string? StripePriceIdYearly { get; set; }

    /// <summary>
    /// Stripe Price ID for per-seat addon (EnterprisePlus).
    /// </summary>
    public string? StripePriceIdPerSeat { get; set; }

    /// <summary>
    /// Number of users included in the base price before per-seat billing.
    /// </summary>
    public int IncludedUsers { get; set; } = 1;

    /// <summary>
    /// Price per extra user seat beyond IncludedUsers (EnterprisePlus).
    /// </summary>
    public decimal? ExtraSeatPrice { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<TenantLicense> Licenses { get; set; } = new List<TenantLicense>();
}
