namespace Psicomy.Services.Billing.Models;

public class SentTrialReminder
{
    public Guid Id { get; set; }
    public Guid LicenseId { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public int MilestoneDay { get; set; }
    public DateTime SentAt { get; set; } = DateTime.UtcNow;
}
