using System.Net;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Psicomy.Services.Billing.Data;
using Psicomy.Services.Billing.Infrastructure;
using Psicomy.Services.Billing.Models;
using Psicomy.Shared.Kernel.Messaging.Notifications;
using Rebus.Bus;

namespace Psicomy.Services.Billing.Controllers;

[ApiController]
[Route("api/stripe/academic-verification")]
[Authorize]
public class AcademicVerificationController : ControllerBase
{
    private readonly BillingDbContext _context;
    private readonly IStorageService _storageService;
    private readonly IBus _bus;
    private readonly ILogger<AcademicVerificationController> _logger;

    private static readonly string[] AllowedContentTypes =
    {
        "application/pdf",
        "image/jpeg",
        "image/png",
        "image/webp"
    };

    private const long MaxFileSizeBytes = 10 * 1024 * 1024; // 10 MB
    private const int MaxRejectionsPerMonth = 3;
    private const int BlockDurationDays = 30;
    private const int MaxPageSize = 100;
    private const string AdminRoles = "Admin,Administrador,Gestor,admin,administrador,gestor";

    public AcademicVerificationController(
        BillingDbContext context,
        IStorageService storageService,
        IBus bus,
        ILogger<AcademicVerificationController> logger)
    {
        _context = context;
        _storageService = storageService;
        _bus = bus;
        _logger = logger;
    }

    /// <summary>
    /// Submit academic eligibility verification with enrollment certificate.
    /// </summary>
    [HttpPost("submit")]
    public async Task<IActionResult> SubmitVerification([FromForm] AcademicVerificationRequest request)
    {
        var userId = User.FindFirst("sub")?.Value
            ?? User.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")?.Value;
        var tenantId = User.FindFirst("tenant")?.Value;

        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(tenantId))
        {
            return Unauthorized(new { error = "Invalid token" });
        }

        // Check if user is blocked.
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

        // Check if there is already a pending verification.
        var pendingVerification = await _context.AcademicVerifications
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

        // Validate file.
        if (request.Document == null || request.Document.Length == 0)
        {
            return BadRequest(new { error = "Documento e obrigatorio" });
        }

        if (request.Document.Length > MaxFileSizeBytes)
        {
            return BadRequest(new { error = "Arquivo muito grande. Maximo permitido: 10MB" });
        }

        if (!AllowedContentTypes.Contains(request.Document.ContentType.ToLowerInvariant()))
        {
            return BadRequest(new { error = "Tipo de arquivo nao permitido. Use PDF, JPEG, PNG ou WebP" });
        }

        string storagePath;
        try
        {
            await using var stream = request.Document.OpenReadStream();
            storagePath = await _storageService.UploadFileAsync(
                stream,
                request.Document.FileName,
                request.Document.ContentType,
                $"academic-verifications/{tenantId}/{userId}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload academic verification document");
            return StatusCode(500, new { error = "Erro ao fazer upload do documento" });
        }

        var verification = new AcademicVerification
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

        _context.AcademicVerifications.Add(verification);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Academic verification submitted: {VerificationId} for user {UserId}", verification.Id, userId);

        return Ok(new
        {
            verificationId = verification.Id,
            status = "pending",
            message = "Solicitacao enviada com sucesso. Aguarde a analise do documento."
        });
    }

    /// <summary>
    /// Get current verification status for the user.
    /// </summary>
    [HttpGet("status")]
    public async Task<IActionResult> GetStatus()
    {
        var userId = User.FindFirst("sub")?.Value
            ?? User.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")?.Value;

        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized(new { error = "Invalid token" });
        }

        var verification = await _context.AcademicVerifications
            .Where(v => v.UserId == userId)
            .OrderByDescending(v => v.CreatedAt)
            .FirstOrDefaultAsync();

        var blockStatus = await GetBlockStatusAsync(userId);

        var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var rejectionsThisMonth = await _context.AcademicVerifications
            .CountAsync(v => v.UserId == userId && v.Status == "rejected" && v.CreatedAt >= monthStart);

        return Ok(new
        {
            hasVerification = verification != null,
            verification = verification == null
                ? null
                : new
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
    /// Get verification history for the user.
    /// </summary>
    [HttpGet("history")]
    public async Task<IActionResult> GetHistory()
    {
        var userId = User.FindFirst("sub")?.Value
            ?? User.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")?.Value;

        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized(new { error = "Invalid token" });
        }

        var verifications = await _context.AcademicVerifications
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
    /// Admin: List pending verifications ordered oldest-first.
    /// </summary>
    [HttpGet("admin/pending")]
    [Authorize(Roles = AdminRoles)]
    public async Task<IActionResult> GetPendingVerifications(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var query = _context.AcademicVerifications
            .AsNoTracking()
            .Where(v => v.Status == "pending");

        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalizedSearch = search.Trim().ToLowerInvariant();
            query = query.Where(v =>
                v.FullName.ToLower().Contains(normalizedSearch)
                || v.Email.ToLower().Contains(normalizedSearch)
                || v.InstitutionName.ToLower().Contains(normalizedSearch)
                || v.CourseName.ToLower().Contains(normalizedSearch));
        }

        var total = await query.CountAsync();
        var items = await query
            .OrderBy(v => v.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(v => new AdminAcademicVerificationItem(
                v.Id,
                v.TenantId,
                v.UserId,
                v.FullName,
                v.Email,
                v.Phone,
                v.InstitutionName,
                v.CourseName,
                v.ExpectedGraduationYear,
                v.DocumentFileName,
                v.DocumentContentType,
                v.DocumentSizeBytes,
                v.CreatedAt,
                v.IsBlocked,
                v.BlockedUntil))
            .ToListAsync();

        return Ok(new AdminPagedResult<AdminAcademicVerificationItem>(items, total, page, pageSize));
    }

    /// <summary>
    /// Admin: List reviewed verifications (approved/rejected), newest first.
    /// </summary>
    [HttpGet("admin/reviewed")]
    [Authorize(Roles = AdminRoles)]
    public async Task<IActionResult> GetReviewedVerifications(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        var query = _context.AcademicVerifications
            .AsNoTracking()
            .Where(v => v.Status != "pending");

        if (!string.IsNullOrWhiteSpace(status))
        {
            var normalizedStatus = status.Trim().ToLowerInvariant();
            query = query.Where(v => v.Status == normalizedStatus);
        }

        var total = await query.CountAsync();
        var items = await query
            .OrderByDescending(v => v.ReviewedAt)
            .ThenByDescending(v => v.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(v => new AdminReviewedAcademicVerificationItem(
                v.Id,
                v.TenantId,
                v.UserId,
                v.FullName,
                v.Email,
                v.InstitutionName,
                v.CourseName,
                v.Status,
                v.RejectionReason,
                v.ReviewedAt,
                v.ReviewedBy,
                v.CreatedAt))
            .ToListAsync();

        return Ok(new AdminPagedResult<AdminReviewedAcademicVerificationItem>(items, total, page, pageSize));
    }

    /// <summary>
    /// Admin: Aggregated stats for the manual review dashboard.
    /// </summary>
    [HttpGet("admin/stats")]
    [Authorize(Roles = AdminRoles)]
    public async Task<IActionResult> GetAdminStats()
    {
        var utcNow = DateTime.UtcNow;
        var monthStart = new DateTime(utcNow.Year, utcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var dayStart = utcNow.Date;

        var pendingCount = await _context.AcademicVerifications.CountAsync(v => v.Status == "pending");
        var approvedThisMonth = await _context.AcademicVerifications.CountAsync(v => v.Status == "approved" && v.ReviewedAt >= monthStart);
        var rejectedThisMonth = await _context.AcademicVerifications.CountAsync(v => v.Status == "rejected" && v.ReviewedAt >= monthStart);
        var reviewedToday = await _context.AcademicVerifications.CountAsync(v => v.ReviewedAt >= dayStart);
        var currentlyBlockedUsers = await _context.AcademicVerifications
            .Where(v => v.IsBlocked && v.BlockedUntil > utcNow)
            .Select(v => v.UserId)
            .Distinct()
            .CountAsync();

        return Ok(new AdminAcademicVerificationStats(
            pendingCount,
            approvedThisMonth,
            rejectedThisMonth,
            reviewedToday,
            currentlyBlockedUsers));
    }

    /// <summary>
    /// Admin: Get a temporary URL to inspect the uploaded student document.
    /// </summary>
    [HttpGet("admin/document/{verificationId:guid}/url")]
    [Authorize(Roles = AdminRoles)]
    public async Task<IActionResult> GetDocumentAccessUrl(Guid verificationId)
    {
        var verification = await _context.AcademicVerifications
            .AsNoTracking()
            .Where(v => v.Id == verificationId)
            .Select(v => new
            {
                v.Id,
                v.DocumentStoragePath,
                v.DocumentFileName,
                v.DocumentContentType,
                v.DocumentSizeBytes
            })
            .FirstOrDefaultAsync();

        if (verification == null)
        {
            return NotFound(new { error = "Verificacao nao encontrada" });
        }

        if (string.IsNullOrWhiteSpace(verification.DocumentStoragePath))
        {
            return NotFound(new { error = "Documento nao encontrado para esta verificacao" });
        }

        const int expirationMinutes = 30;

        try
        {
            var url = await _storageService.GetPresignedUrlAsync(verification.DocumentStoragePath, expirationMinutes);
            return Ok(new
            {
                verificationId = verification.Id,
                fileName = verification.DocumentFileName,
                contentType = verification.DocumentContentType,
                sizeBytes = verification.DocumentSizeBytes,
                url,
                expiresInMinutes = expirationMinutes
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create presigned URL for verification {VerificationId}", verificationId);
            return StatusCode(500, new { error = "Nao foi possivel gerar URL temporaria do documento" });
        }
    }

    /// <summary>
    /// Admin: Review a verification (approve/reject).
    /// </summary>
    [HttpPost("admin/review/{verificationId:guid}")]
    [Authorize(Roles = AdminRoles)]
    public async Task<IActionResult> ReviewVerification(Guid verificationId, [FromBody] ReviewAcademicVerificationRequest request)
    {
        var reviewerId = User.FindFirst("sub")?.Value;
        var verification = await _context.AcademicVerifications.FindAsync(verificationId);

        if (verification == null)
        {
            return NotFound(new { error = "Verificacao nao encontrada" });
        }

        if (verification.Status != "pending")
        {
            return BadRequest(new { error = "Esta verificacao ja foi revisada" });
        }

        if (!request.Approved && string.IsNullOrWhiteSpace(request.RejectionReason))
        {
            return BadRequest(new { error = "Motivo da rejeicao e obrigatorio" });
        }

        var normalizedReason = request.RejectionReason?.Trim();
        if (!string.IsNullOrWhiteSpace(normalizedReason) && normalizedReason.Length > 1000)
        {
            return BadRequest(new { error = "Motivo da rejeicao excede 1000 caracteres" });
        }

        verification.Status = request.Approved ? "approved" : "rejected";
        verification.RejectionReason = request.Approved ? null : normalizedReason;
        verification.ReviewedAt = DateTime.UtcNow;
        verification.ReviewedBy = reviewerId;
        verification.UpdatedAt = DateTime.UtcNow;

        if (!request.Approved)
        {
            var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            var rejectionsThisMonth = await _context.AcademicVerifications
                .CountAsync(v => v.UserId == verification.UserId && v.Status == "rejected" && v.CreatedAt >= monthStart);

            if (rejectionsThisMonth >= MaxRejectionsPerMonth)
            {
                verification.IsBlocked = true;
                verification.BlockedUntil = DateTime.UtcNow.AddDays(BlockDurationDays);
                _logger.LogWarning(
                    "User {UserId} blocked for {Days} days due to {Count} rejections",
                    verification.UserId,
                    BlockDurationDays,
                    rejectionsThisMonth);
            }
        }
        else
        {
            verification.IsBlocked = false;
            verification.BlockedUntil = null;
            await ActivateAcademicPlanAsync(verification.TenantId, verification.UserId);
        }

        await _context.SaveChangesAsync();

        var rejectionEmailQueued = false;
        if (!request.Approved && request.NotifyUserByEmail)
        {
            rejectionEmailQueued = await TryQueueRejectionEmailAsync(verification);
        }

        _logger.LogInformation(
            "Verification {VerificationId} reviewed: {Status} by {ReviewerId}",
            verificationId,
            verification.Status,
            reviewerId);

        return Ok(new
        {
            verificationId,
            status = verification.Status,
            message = request.Approved ? "Verificacao aprovada" : "Verificacao rejeitada",
            rejectionEmailQueued
        });
    }

    private async Task<bool> TryQueueRejectionEmailAsync(AcademicVerification verification)
    {
        if (string.IsNullOrWhiteSpace(verification.Email))
        {
            _logger.LogWarning("Skipping rejection email for verification {VerificationId}: missing email", verification.Id);
            return false;
        }

        var fullName = WebUtility.HtmlEncode(verification.FullName);
        var institution = WebUtility.HtmlEncode(verification.InstitutionName);
        var course = WebUtility.HtmlEncode(verification.CourseName);
        var reason = WebUtility.HtmlEncode(verification.RejectionReason ?? "Motivo nao informado");

        var subject = "Psicomy - Validacao estudantil reprovada";
        var htmlBody = $"""
            <p>Ola, {fullName}.</p>
            <p>Concluimos a analise manual do seu comprovante academico e sua solicitacao foi <strong>reprovada</strong>.</p>
            <p><strong>Instituicao:</strong> {institution}<br/>
            <strong>Curso:</strong> {course}</p>
            <p><strong>Motivo informado:</strong><br/>{reason}</p>
            <p>Voce pode enviar uma nova solicitacao com um documento atualizado pela area de assinaturas da plataforma.</p>
            <p>Equipe Psicomy</p>
            """;

        var plainText = new StringBuilder()
            .AppendLine($"Ola, {verification.FullName}.")
            .AppendLine("Concluimos a analise manual do seu comprovante academico e sua solicitacao foi reprovada.")
            .AppendLine($"Instituicao: {verification.InstitutionName}")
            .AppendLine($"Curso: {verification.CourseName}")
            .AppendLine($"Motivo informado: {verification.RejectionReason}")
            .AppendLine("Voce pode enviar uma nova solicitacao com um documento atualizado pela area de assinaturas da plataforma.")
            .AppendLine("Equipe Psicomy")
            .ToString();

        try
        {
            await _bus.Publish(new SendEmailEvent(
                verification.Email,
                subject,
                htmlBody,
                From: null,
                PlainTextBody: plainText));

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to queue rejection email for verification {VerificationId}", verification.Id);
            return false;
        }
    }

    private async Task<(bool IsBlocked, DateTime? BlockedUntil)> GetBlockStatusAsync(string userId)
    {
        var blockedVerification = await _context.AcademicVerifications
            .Where(v => v.UserId == userId && v.IsBlocked && v.BlockedUntil > DateTime.UtcNow)
            .OrderByDescending(v => v.BlockedUntil)
            .FirstOrDefaultAsync();

        if (blockedVerification != null)
        {
            return (true, blockedVerification.BlockedUntil);
        }

        var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var rejectionsThisMonth = await _context.AcademicVerifications
            .CountAsync(v => v.UserId == userId && v.Status == "rejected" && v.CreatedAt >= monthStart);

        if (rejectionsThisMonth >= MaxRejectionsPerMonth)
        {
            return (true, DateTime.UtcNow.AddDays(BlockDurationDays));
        }

        return (false, null);
    }

    private async Task ActivateAcademicPlanAsync(string tenantId, string userId)
    {
        var freePlan = await _context.PaymentPlans
            .FirstOrDefaultAsync(p => p.Tier == "Free" && p.IsActive);

        if (freePlan == null)
        {
            _logger.LogWarning("Free plan not found for academic verification");
            return;
        }

        var existingLicense = await _context.TenantLicenses
            .FirstOrDefaultAsync(l => l.TenantId == tenantId);

        if (existingLicense != null)
        {
            // Extend trial to 180 days from original signup date.
            var extendedTrialEnd = existingLicense.LicenseStartDate.AddDays(180);
            existingLicense.PlanId = freePlan.Id;
            existingLicense.TrialEndDate = extendedTrialEnd;
            existingLicense.LicenseEndDate = extendedTrialEnd;
            existingLicense.ExpiresAt = extendedTrialEnd;
            existingLicense.Status = "trial";
            existingLicense.IsActive = true;
            existingLicense.PaymentMethod = "academic_verification";
            existingLicense.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            var now = DateTime.UtcNow;
            var extendedTrialEnd = now.AddDays(180);
            var license = new TenantLicense
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                PlanId = freePlan.Id,
                Status = "trial",
                IsActive = true,
                LicenseStartDate = now,
                LicenseEndDate = extendedTrialEnd,
                TrialEndDate = extendedTrialEnd,
                ExpiresAt = extendedTrialEnd,
                PaymentMethod = "academic_verification",
                CreatedAt = now,
                UpdatedAt = now
            };

            _context.TenantLicenses.Add(license);
        }

        _logger.LogInformation(
            "Student trial extended to 180 days via academic verification for tenant {TenantId} and user {UserId}",
            tenantId,
            userId);
    }
}

public class AcademicVerificationRequest
{
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string InstitutionName { get; set; } = string.Empty;
    public string CourseName { get; set; } = string.Empty;
    public int? ExpectedGraduationYear { get; set; }
    public IFormFile Document { get; set; } = null!;
}

public class ReviewAcademicVerificationRequest
{
    public bool Approved { get; set; }
    public string? RejectionReason { get; set; }
    public bool NotifyUserByEmail { get; set; } = true;
}

public record AdminAcademicVerificationItem(
    Guid Id,
    string TenantId,
    string UserId,
    string FullName,
    string Email,
    string? Phone,
    string InstitutionName,
    string CourseName,
    int? ExpectedGraduationYear,
    string DocumentFileName,
    string DocumentContentType,
    long DocumentSizeBytes,
    DateTime CreatedAt,
    bool IsBlocked,
    DateTime? BlockedUntil);

public record AdminReviewedAcademicVerificationItem(
    Guid Id,
    string TenantId,
    string UserId,
    string FullName,
    string Email,
    string InstitutionName,
    string CourseName,
    string Status,
    string? RejectionReason,
    DateTime? ReviewedAt,
    string? ReviewedBy,
    DateTime CreatedAt);

public record AdminAcademicVerificationStats(
    int PendingCount,
    int ApprovedThisMonth,
    int RejectedThisMonth,
    int ReviewedToday,
    int CurrentlyBlockedUsers);

public record AdminPagedResult<T>(
    IReadOnlyList<T> Items,
    int Total,
    int Page,
    int PageSize)
{
    public int TotalPages => (int)Math.Ceiling(Total / (double)PageSize);
}
