namespace Psicomy.Services.Billing.Models;

public class StudentVerification
{
    public Guid Id { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;

    // User registration data
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string InstitutionName { get; set; } = string.Empty;
    public string CourseName { get; set; } = string.Empty;
    public int? ExpectedGraduationYear { get; set; }

    // Document data
    public string DocumentFileName { get; set; } = string.Empty;
    public string DocumentStoragePath { get; set; } = string.Empty;
    public string DocumentContentType { get; set; } = string.Empty;
    public long DocumentSizeBytes { get; set; }

    // Status: pending, approved, rejected
    public string Status { get; set; } = "pending";
    public string? RejectionReason { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewedBy { get; set; }

    // Blocking
    public bool IsBlocked { get; set; }
    public DateTime? BlockedUntil { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
