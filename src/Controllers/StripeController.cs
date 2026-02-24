using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Psicomy.Services.Billing.Data;
using Psicomy.Services.Billing.Infrastructure;
using Psicomy.Services.Billing.Middleware;
using Stripe;
using Stripe.Checkout;

namespace Psicomy.Services.Billing.Controllers;

[ApiController]
[Route("api/stripe")]
[Authorize]
public class StripeController : ControllerBase
{
    private readonly StripeSettings _stripeSettings;
    private readonly BillingDbContext _context;
    private readonly ILogger<StripeController> _logger;

    public StripeController(
        StripeSettings stripeSettings,
        BillingDbContext context,
        ILogger<StripeController> logger)
    {
        _stripeSettings = stripeSettings;
        _context = context;
        _logger = logger;
    }

    /// <summary>
    /// Get Stripe publishable key for frontend
    /// </summary>
    [HttpGet("config")]
    [AllowAnonymous]
    public IActionResult GetConfig()
    {
        return Ok(new { publishableKey = _stripeSettings.PublishableKey });
    }

    /// <summary>
    /// Get all available payment plans
    /// </summary>
    [HttpGet("plans")]
    [AllowAnonymous]
    public async Task<IActionResult> GetPlans()
    {
        var plans = await _context.PaymentPlans
            .Where(p => p.IsActive)
            .OrderBy(p => p.MonthlyPrice)
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.Description,
                p.Tier,
                p.MonthlyPrice,
                p.YearlyPrice,
                p.MaxUsers
            })
            .ToListAsync();

        return Ok(plans);
    }

    /// <summary>
    /// Create a payment intent for Stripe Elements
    /// </summary>
    [HttpPost("create-intent")]
    [AllowAnonymous]
    public async Task<IActionResult> CreatePaymentIntent([FromBody] CreatePaymentIntentRequest request)
    {
        if (request == null || request.PlanId == Guid.Empty)
            return BadRequest(new { error = "Invalid request" });

        var plan = await _context.PaymentPlans
            .FirstOrDefaultAsync(p => p.Id == request.PlanId && p.IsActive);

        if (plan == null)
            return NotFound(new { error = "Payment plan not found" });

        // Calculate amount
        long amount = (long)(plan.MonthlyPrice * 100);
        if (request.Period == "annual")
        {
             amount = (long)(plan.YearlyPrice * 100);
        }

        try
        {
            var options = new PaymentIntentCreateOptions
            {
                Amount = amount,
                Currency = "brl",
                AutomaticPaymentMethods = new PaymentIntentAutomaticPaymentMethodsOptions
                {
                    Enabled = true,
                },
                Metadata = new Dictionary<string, string>
                {
                    { "plan_id", plan.Id.ToString() },
                    { "plan_name", plan.Name },
                    { "period", request.Period ?? "monthly" },
                    { "tenant_slug", request.TenantSlug },
                    { "user_email", request.UserEmail },
                    { "user_name", request.UserName },
                    { "document", request.Document }
                }
            };
            
            if (!string.IsNullOrEmpty(request.UserEmail)) 
            {
                 options.ReceiptEmail = request.UserEmail;
            }

            var service = new PaymentIntentService();
            var intent = await service.CreateAsync(options);

            return Ok(new { clientSecret = intent.ClientSecret });
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Error creating payment intent");
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Create a checkout session for subscription payment
    /// </summary>
    [HttpPost("create-checkout-session")]
    public async Task<IActionResult> CreateCheckoutSession([FromBody] CreateCheckoutSessionRequest request)
    {
        var tenantId = HttpContext.GetTenantId();
        if (string.IsNullOrEmpty(tenantId))
            return Unauthorized(new { error = "Tenant ID not found" });

        var plan = await _context.PaymentPlans
            .FirstOrDefaultAsync(p => p.Id == request.PlanId && p.IsActive);

        if (plan == null)
            return NotFound(new { error = "Payment plan not found" });

        // Student plan - activate without payment
        if (plan.Tier == "Student" || plan.MonthlyPrice == 0)
            return await ActivateFreePlan(tenantId, plan.Id);

        try
        {
            var baseUrl = $"{Request.Scheme}://{Request.Host}";
            var frontendUrl = request.SuccessUrl?.Contains("psicomy") == true
                ? new Uri(request.SuccessUrl).GetLeftPart(UriPartial.Authority)
                : "https://psicomy.com.br";

            var options = new SessionCreateOptions
            {
                PaymentMethodTypes = new List<string> { "card" },
                Mode = "subscription",
                LineItems = new List<SessionLineItemOptions>
                {
                    new SessionLineItemOptions
                    {
                        PriceData = new SessionLineItemPriceDataOptions
                        {
                            Currency = "brl",
                            ProductData = new SessionLineItemPriceDataProductDataOptions
                            {
                                Name = plan.Name,
                                Description = plan.Description
                            },
                            UnitAmount = (long)(plan.MonthlyPrice * 100),
                            Recurring = new SessionLineItemPriceDataRecurringOptions
                            {
                                Interval = "month"
                            }
                        },
                        Quantity = 1
                    }
                },
                SuccessUrl = request.SuccessUrl ?? $"{frontendUrl}/dashboard?payment=success",
                CancelUrl = request.CancelUrl ?? $"{frontendUrl}/upgrade?payment=cancelled",
                Metadata = new Dictionary<string, string>
                {
                    { "tenant_id", tenantId },
                    { "plan_id", plan.Id.ToString() }
                },
                CustomerEmail = User.FindFirst("email")?.Value
            };

            var service = new SessionService();
            var session = await service.CreateAsync(options);

            _logger.LogInformation("Created checkout session {SessionId} for tenant {TenantId}, plan {PlanId}",
                session.Id, tenantId, plan.Id);

            return Ok(new { sessionId = session.Id, url = session.Url });
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Error creating Stripe checkout session for tenant {TenantId}", tenantId);
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Create a billing portal session for managing subscription
    /// </summary>
    [HttpPost("create-portal-session")]
    public async Task<IActionResult> CreatePortalSession([FromBody] CreatePortalSessionRequest request)
    {
        var tenantId = HttpContext.GetTenantId();
        if (string.IsNullOrEmpty(tenantId))
            return Unauthorized(new { error = "Tenant ID not found" });

        var license = await _context.TenantLicenses
            .FirstOrDefaultAsync(l => l.TenantId == tenantId && l.IsActive);

        if (license == null || string.IsNullOrEmpty(license.StripeCustomerId))
            return NotFound(new { error = "No active subscription found" });

        try
        {
            var options = new Stripe.BillingPortal.SessionCreateOptions
            {
                Customer = license.StripeCustomerId,
                ReturnUrl = request.ReturnUrl ?? "https://psicomy.com.br/dashboard/settings/billing"
            };

            var service = new Stripe.BillingPortal.SessionService();
            var session = await service.CreateAsync(options);

            return Ok(new { url = session.Url });
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Error creating Stripe portal session for tenant {TenantId}", tenantId);
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Get current tenant's subscription status
    /// </summary>
    [HttpGet("subscription")]
    public async Task<IActionResult> GetSubscription()
    {
        var tenantId = HttpContext.GetTenantId();
        if (string.IsNullOrEmpty(tenantId))
            return Unauthorized(new { error = "Tenant ID not found" });

        var license = await _context.TenantLicenses
            .Include(l => l.Plan)
            .FirstOrDefaultAsync(l => l.TenantId == tenantId && l.IsActive);

        if (license == null)
            return NotFound(new { error = "No active subscription found" });

        return Ok(new
        {
            license.Id,
            license.TenantId,
            license.Status,
            license.LicenseStartDate,
            license.LicenseEndDate,
            license.ExpiresAt,
            license.AutoRenew,
            license.PaymentMethod,
            license.PaymentMethodLast4,
            Plan = license.Plan != null ? new
            {
                license.Plan.Id,
                license.Plan.Name,
                license.Plan.Tier,
                license.Plan.MonthlyPrice,
                license.Plan.MaxUsers
            } : null
        });
    }

    private async Task<IActionResult> ActivateFreePlan(string tenantId, Guid planId)
    {
        var license = await _context.TenantLicenses
            .FirstOrDefaultAsync(l => l.TenantId == tenantId);

        if (license == null)
        {
            license = new Models.TenantLicense
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                PlanId = planId,
                LicenseStartDate = DateTime.UtcNow,
                LicenseEndDate = DateTime.UtcNow.AddYears(100),
                AutoRenew = false,
                IsActive = true,
                Status = "active",
                PaymentMethod = "free",
                ExpiresAt = DateTime.UtcNow.AddYears(100),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _context.TenantLicenses.Add(license);
        }
        else
        {
            license.PlanId = planId;
            license.Status = "active";
            license.IsActive = true;
            license.PaymentMethod = "free";
            license.LicenseEndDate = DateTime.UtcNow.AddYears(100);
            license.ExpiresAt = DateTime.UtcNow.AddYears(100);
            license.UpdatedAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync();
        _logger.LogInformation("Activated free Student plan for tenant {TenantId}", tenantId);

        return Ok(new
        {
            success = true,
            message = "Student plan activated successfully",
            redirectUrl = "/dashboard?plan=activated"
        });
    }
}

public record CreateCheckoutSessionRequest(Guid PlanId, string? SuccessUrl = null, string? CancelUrl = null);
public record CreatePortalSessionRequest(string? ReturnUrl = null);
public record CreatePaymentIntentRequest(
    Guid PlanId, 
    string? Period, 
    string TenantSlug, 
    string UserEmail, 
    string UserName, 
    string Document
);
