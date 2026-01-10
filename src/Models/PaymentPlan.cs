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

    public string? StripePriceIdMonthly { get; set; }
    public string? StripePriceIdYearly { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public ICollection<TenantLicense> Licenses { get; set; } = new List<TenantLicense>();
}
