using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Psicomy.Services.Billing.Data;
using Psicomy.Services.Billing.Infrastructure;
using Psicomy.Services.Billing.Models;

namespace Psicomy.Services.Billing.Controllers;

[ApiController]
[Route("api/stripe/student-verification")]
[Authorize]
public class StudentVerificationController : ControllerBase
{
    private readonly BillingDbContext _context;
    private readonly IStorageService _storageService;
    private readonly ILogger<StudentVerificationController> _logger;

    private static readonly string[] AllowedContentTypes = { "application/pdf", "image/jpeg", "image/png", "image/webp" };
    private const long MaxFileSizeBytes = 10 * 1024 * 1024; // 10 MB
    private const int MaxRejectionsPerMonth = 3;
    private const int BlockDurationDays = 30;

    public StudentVerificationController(
        BillingDbContext context,
        IStorageService storageService,
        ILogger<StudentVerificationController> logger)
    {
        _context = context;
        _storageService = storageService;
        _logger = logger;
    }

    /// <summary>
    /// Submit student verification with enrollment certificate
    /// </summary>
    [HttpPost("submit")]
    public async Task<IActionResult> SubmitVerification([FromForm] StudentVerificationRequest request)
    {
        var userId = User.FindFirst("sub")?.Value ?? User.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")?.Value;
        var tenantId = User.FindFirst("tenant")?.Value;

        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(tenantId))
        {
            return Unauthorized(new { error = "Invalid token" });
        }

        // Check if user is blocked
        var blockStatus = await GetBlockStatusAsync(userId);
        if (blockStatus.IsBlocked)
        {
            return BadRequest(new
            {
                error = "blocked",
                message = $"Voce esta bloqueado ate {blockStatus.BlockedUntil:dd/MM/yyyy} devido a multiplas rejeicoes",
                blockedUntil = blockStatus.BlockedUntil
            });
        }

        // Check if there's already a pending verification
        var pendingVerification = await _context.StudentVerifications
            .FirstOrDefaultAsync(v => v.UserId == userId && v.Status == "pending");

        if (pendingVerification != null)
        {
            return BadRequest(new
            {
                error = "pending_exists",
                message = "Voce ja possui uma solicitacao de verificacao em analise",
                verificationId = pendingVerification.Id
            });
        }

        // Validate file
        if (request.Document == null || request.Document.Length == 0)
        {
            return BadRequest(new { error = "Documento e obrigatorio" });
        }

        if (request.Document.Length > MaxFileSizeBytes)
        {
            return BadRequest(new { error = "Arquivo muito grande. Maximo permitido: 10MB" });
        }

        if (!AllowedContentTypes.Contains(request.Document.ContentType.ToLower()))
        {
            return BadRequest(new { error = "Tipo de arquivo nao permitido. Use PDF, JPEG, PNG ou WebP" });
        }

        // Upload file to storage
        string storagePath;
        try
        {
            await using var stream = request.Document.OpenReadStream();
            storagePath = await _storageService.UploadFileAsync(
                stream,
                request.Document.FileName,
                request.Document.ContentType,
                $"student-verifications/{tenantId}/{userId}"
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload student verification document");
            return StatusCode(500, new { error = "Erro ao fazer upload do documento" });
        }

        // Create verification record
        var verification = new StudentVerification
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            UserId = userId,
            FullName = request.FullName,
            Email = request.Email,
            Phone = request.Phone,
            InstitutionName = request.InstitutionName,
            CourseName = request.CourseName,
            ExpectedGraduationYear = request.ExpectedGraduationYear,
            DocumentFileName = request.Document.FileName,
            DocumentStoragePath = storagePath,
            DocumentContentType = request.Document.ContentType,
            DocumentSizeBytes = request.Document.Length,
            Status = "pending",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _context.StudentVerifications.Add(verification);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Student verification submitted: {VerificationId} for user {UserId}", verification.Id, userId);

        return Ok(new
        {
            verificationId = verification.Id,
            status = "pending",
            message = "Solicitacao enviada com sucesso. Aguarde a analise do documento."
        });
    }

    /// <summary>
    /// Get current verification status for the user
    /// </summary>
    [HttpGet("status")]
    public async Task<IActionResult> GetStatus()
    {
        var userId = User.FindFirst("sub")?.Value ?? User.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")?.Value;

        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized(new { error = "Invalid token" });
        }

        // Get latest verification
        var verification = await _context.StudentVerifications
            .Where(v => v.UserId == userId)
            .OrderByDescending(v => v.CreatedAt)
            .FirstOrDefaultAsync();

        // Get block status
        var blockStatus = await GetBlockStatusAsync(userId);

        // Count rejections this month
        var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var rejectionsThisMonth = await _context.StudentVerifications
            .CountAsync(v => v.UserId == userId && v.Status == "rejected" && v.CreatedAt >= monthStart);

        return Ok(new
        {
            hasVerification = verification != null,
            verification = verification == null ? null : new
            {
                id = verification.Id,
                status = verification.Status,
                fullName = verification.FullName,
                institutionName = verification.InstitutionName,
                courseName = verification.CourseName,
                rejectionReason = verification.RejectionReason,
                createdAt = verification.CreatedAt,
                reviewedAt = verification.ReviewedAt
            },
            isBlocked = blockStatus.IsBlocked,
            blockedUntil = blockStatus.BlockedUntil,
            rejectionsThisMonth,
            maxRejectionsAllowed = MaxRejectionsPerMonth
        });
    }

    /// <summary>
    /// Get verification history for the user
    /// </summary>
    [HttpGet("history")]
    public async Task<IActionResult> GetHistory()
    {
        var userId = User.FindFirst("sub")?.Value ?? User.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")?.Value;

        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized(new { error = "Invalid token" });
        }

        var verifications = await _context.StudentVerifications
            .Where(v => v.UserId == userId)
            .OrderByDescending(v => v.CreatedAt)
            .Select(v => new
            {
                id = v.Id,
                status = v.Status,
                institutionName = v.InstitutionName,
                courseName = v.CourseName,
                rejectionReason = v.RejectionReason,
                createdAt = v.CreatedAt,
                reviewedAt = v.ReviewedAt
            })
            .ToListAsync();

        return Ok(verifications);
    }

    /// <summary>
    /// Admin: List pending verifications (for future admin panel)
    /// </summary>
    [HttpGet("admin/pending")]
    [Authorize(Roles = "administrador,gestor")]
    public async Task<IActionResult> GetPendingVerifications([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var query = _context.StudentVerifications
            .Where(v => v.Status == "pending")
            .OrderBy(v => v.CreatedAt);

        var total = await query.CountAsync();
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return Ok(new
        {
            items,
            total,
            page,
            pageSize,
            totalPages = (int)Math.Ceiling(total / (double)pageSize)
        });
    }

    /// <summary>
    /// Admin: Review a verification (approve/reject)
    /// </summary>
    [HttpPost("admin/review/{verificationId}")]
    [Authorize(Roles = "administrador,gestor")]
    public async Task<IActionResult> ReviewVerification(Guid verificationId, [FromBody] ReviewVerificationRequest request)
    {
        var reviewerId = User.FindFirst("sub")?.Value;
        var verification = await _context.StudentVerifications.FindAsync(verificationId);

        if (verification == null)
        {
            return NotFound(new { error = "Verificacao nao encontrada" });
        }

        if (verification.Status != "pending")
        {
            return BadRequest(new { error = "Esta verificacao ja foi revisada" });
        }

        verification.Status = request.Approved ? "approved" : "rejected";
        verification.RejectionReason = request.Approved ? null : request.RejectionReason;
        verification.ReviewedAt = DateTime.UtcNow;
        verification.ReviewedBy = reviewerId;
        verification.UpdatedAt = DateTime.UtcNow;

        // If rejected, check if user should be blocked
        if (!request.Approved)
        {
            var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var rejectionsThisMonth = await _context.StudentVerifications
                .CountAsync(v => v.UserId == verification.UserId && v.Status == "rejected" && v.CreatedAt >= monthStart);

            if (rejectionsThisMonth >= MaxRejectionsPerMonth)
            {
                verification.IsBlocked = true;
                verification.BlockedUntil = DateTime.UtcNow.AddDays(BlockDurationDays);
                _logger.LogWarning("User {UserId} blocked for {Days} days due to {Count} rejections",
                    verification.UserId, BlockDurationDays, rejectionsThisMonth);
            }
        }
        else
        {
            // If approved, activate student plan license
            await ActivateStudentPlanAsync(verification.TenantId, verification.UserId);
        }

        await _context.SaveChangesAsync();

        _logger.LogInformation("Verification {VerificationId} reviewed: {Status} by {ReviewerId}",
            verificationId, verification.Status, reviewerId);

        return Ok(new
        {
            verificationId,
            status = verification.Status,
            message = request.Approved ? "Verificacao aprovada" : "Verificacao rejeitada"
        });
    }

    private async Task<(bool IsBlocked, DateTime? BlockedUntil)> GetBlockStatusAsync(string userId)
    {
        var blockedVerification = await _context.StudentVerifications
            .Where(v => v.UserId == userId && v.IsBlocked && v.BlockedUntil > DateTime.UtcNow)
            .OrderByDescending(v => v.BlockedUntil)
            .FirstOrDefaultAsync();

        if (blockedVerification != null)
        {
            return (true, blockedVerification.BlockedUntil);
        }

        // Check rejection count this month
        var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var rejectionsThisMonth = await _context.StudentVerifications
            .CountAsync(v => v.UserId == userId && v.Status == "rejected" && v.CreatedAt >= monthStart);

        if (rejectionsThisMonth >= MaxRejectionsPerMonth)
        {
            return (true, DateTime.UtcNow.AddDays(BlockDurationDays));
        }

        return (false, null);
    }

    private async Task ActivateStudentPlanAsync(string tenantId, string userId)
    {
        var studentPlan = await _context.PaymentPlans
            .FirstOrDefaultAsync(p => p.Tier == "Student" && p.IsActive);

        if (studentPlan == null)
        {
            _logger.LogWarning("Student plan not found");
            return;
        }

        var existingLicense = await _context.TenantLicenses
            .FirstOrDefaultAsync(l => l.TenantId == tenantId && l.IsActive);

        if (existingLicense != null)
        {
            existingLicense.PlanId = studentPlan.Id;
            existingLicense.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            var license = new TenantLicense
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                PlanId = studentPlan.Id,
                Status = "active",
                IsActive = true,
                LicenseStartDate = DateTime.UtcNow,
                LicenseEndDate = DateTime.UtcNow.AddYears(1),
                ExpiresAt = DateTime.UtcNow.AddYears(1),
                PaymentMethod = "student_verification",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _context.TenantLicenses.Add(license);
        }

        _logger.LogInformation("Student plan activated for tenant {TenantId}", tenantId);
    }
}

public class StudentVerificationRequest
{
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string InstitutionName { get; set; } = string.Empty;
    public string CourseName { get; set; } = string.Empty;
    public int? ExpectedGraduationYear { get; set; }
    public IFormFile Document { get; set; } = null!;
}

public class ReviewVerificationRequest
{
    public bool Approved { get; set; }
    public string? RejectionReason { get; set; }
}
