using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Psicomy.Services.Billing.Data;
using Psicomy.Services.Billing.Middleware;
using Psicomy.Services.Billing.Options;
using Psicomy.Shared.Kernel.Messaging.Events;
using Rebus.Bus;
using Stripe;
using Stripe.Checkout;

namespace Psicomy.Services.Billing.Controllers;

[ApiController]
[Route("api/stripe")]
[Authorize]
public class StripeController : ControllerBase
{
    private readonly StripeOptions _stripeOptions;
    private readonly BillingDbContext _context;
    private readonly ILogger<StripeController> _logger;
    private readonly IBus _bus;

    public StripeController(
        IOptions<StripeOptions> stripeOptions,
        BillingDbContext context,
        ILogger<StripeController> logger,
        IBus bus)
    {
        _stripeOptions = stripeOptions.Value;
        _context = context;
        _logger = logger;
        _bus = bus;
    }

    /// <summary>
    /// Get Stripe publishable key for frontend
    /// </summary>
    [HttpGet("config")]
    [AllowAnonymous]
    public IActionResult GetConfig()
    {
        return Ok(new { publishableKey = _stripeOptions.PublishableKey });
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
    /// Create a checkout session for subscription payment (Stripe Hosted Checkout)
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

        // Determine which Stripe Price ID to use
        var isAnnual = string.Equals(request.Period, "annual", StringComparison.OrdinalIgnoreCase);
        var stripePriceId = isAnnual ? plan.StripePriceIdYearly : plan.StripePriceIdMonthly;

        if (string.IsNullOrEmpty(stripePriceId))
        {
            _logger.LogError("No Stripe Price ID configured for plan {PlanId}, period {Period}", plan.Id, request.Period);
            return BadRequest(new { error = "Plan pricing not configured" });
        }

        try
        {
            var frontendUrl = request.SuccessUrl?.Contains("psicomy") == true
                ? new Uri(request.SuccessUrl).GetLeftPart(UriPartial.Authority)
                : "https://psicomy.com.br";

            var options = new SessionCreateOptions
            {
                Mode = "subscription",
                LineItems = new List<SessionLineItemOptions>
                {
                    new SessionLineItemOptions
                    {
                        Price = stripePriceId,
                        Quantity = 1
                    }
                },
                SuccessUrl = request.SuccessUrl ?? $"{frontendUrl}/dashboard?payment=success",
                CancelUrl = request.CancelUrl ?? $"{frontendUrl}/upgrade?payment=cancelled",
                Metadata = new Dictionary<string, string>
                {
                    { "tenant_id", tenantId },
                    { "plan_id", plan.Id.ToString() },
                    { "period", request.Period ?? "monthly" }
                },
                CustomerEmail = User.FindFirst("email")?.Value
            };

            var service = new SessionService();
            var session = await service.CreateAsync(options);

            _logger.LogInformation("Created checkout session {SessionId} for tenant {TenantId}, plan {PlanId}, period {Period}",
                session.Id, tenantId, plan.Id, request.Period);

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

    /// <summary>
    /// Preview proration for a plan change
    /// </summary>
    [HttpPost("preview-plan-change")]
    public async Task<IActionResult> PreviewPlanChange([FromBody] PlanChangeRequest request)
    {
        var tenantId = HttpContext.GetTenantId();
        if (string.IsNullOrEmpty(tenantId))
            return Unauthorized(new { error = "Tenant ID not found" });

        var license = await _context.TenantLicenses
            .Include(l => l.Plan)
            .FirstOrDefaultAsync(l => l.TenantId == tenantId && l.IsActive);

        if (license == null || string.IsNullOrEmpty(license.StripeSubscriptionId))
            return NotFound(new { error = "No active subscription found" });

        var newPlan = await _context.PaymentPlans
            .FirstOrDefaultAsync(p => p.Id == request.PlanId && p.IsActive);

        if (newPlan == null)
            return NotFound(new { error = "Target plan not found" });

        var isAnnual = string.Equals(request.Period, "annual", StringComparison.OrdinalIgnoreCase);
        var newPriceId = isAnnual ? newPlan.StripePriceIdYearly : newPlan.StripePriceIdMonthly;

        if (string.IsNullOrEmpty(newPriceId))
            return BadRequest(new { error = "Target plan pricing not configured" });

        try
        {
            var subscriptionService = new SubscriptionService();
            var subscription = await subscriptionService.GetAsync(license.StripeSubscriptionId);
            var currentItem = subscription.Items.Data.FirstOrDefault();

            if (currentItem == null)
                return BadRequest(new { error = "No subscription items found" });

            // Preview upcoming invoice with the plan change
            var invoiceService = new InvoiceService();
            var previewOptions = new InvoiceCreatePreviewOptions
            {
                Customer = license.StripeCustomerId,
                Subscription = license.StripeSubscriptionId,
                SubscriptionDetails = new InvoiceSubscriptionDetailsOptions
                {
                    Items = new List<InvoiceSubscriptionDetailsItemOptions>
                    {
                        new InvoiceSubscriptionDetailsItemOptions
                        {
                            Id = currentItem.Id,
                            Price = newPriceId
                        }
                    },
                    ProrationBehavior = "create_prorations"
                }
            };

            var preview = await invoiceService.CreatePreviewAsync(previewOptions);

            return Ok(new
            {
                currentPlan = license.Plan?.Name,
                currentTier = license.Plan?.Tier,
                newPlan = newPlan.Name,
                newTier = newPlan.Tier,
                proratedAmount = preview.AmountDue / 100m,
                currency = preview.Currency,
                nextBillingDate = subscription.Items.Data.FirstOrDefault()?.CurrentPeriodEnd,
                immediateCharge = preview.AmountDue > 0
            });
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Error previewing plan change for tenant {TenantId}", tenantId);
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Execute a plan change (upgrade/downgrade)
    /// </summary>
    [HttpPost("change-plan")]
    public async Task<IActionResult> ChangePlan([FromBody] PlanChangeRequest request)
    {
        var tenantId = HttpContext.GetTenantId();
        if (string.IsNullOrEmpty(tenantId))
            return Unauthorized(new { error = "Tenant ID not found" });

        var license = await _context.TenantLicenses
            .Include(l => l.Plan)
            .FirstOrDefaultAsync(l => l.TenantId == tenantId && l.IsActive);

        if (license == null || string.IsNullOrEmpty(license.StripeSubscriptionId))
            return NotFound(new { error = "No active subscription found" });

        var newPlan = await _context.PaymentPlans
            .FirstOrDefaultAsync(p => p.Id == request.PlanId && p.IsActive);

        if (newPlan == null)
            return NotFound(new { error = "Target plan not found" });

        var isAnnual = string.Equals(request.Period, "annual", StringComparison.OrdinalIgnoreCase);
        var newPriceId = isAnnual ? newPlan.StripePriceIdYearly : newPlan.StripePriceIdMonthly;

        if (string.IsNullOrEmpty(newPriceId))
            return BadRequest(new { error = "Target plan pricing not configured" });

        try
        {
            var subscriptionService = new SubscriptionService();
            var subscription = await subscriptionService.GetAsync(license.StripeSubscriptionId);
            var currentItem = subscription.Items.Data.FirstOrDefault();

            if (currentItem == null)
                return BadRequest(new { error = "No subscription items found" });

            // Update the subscription with the new price
            var updateOptions = new SubscriptionUpdateOptions
            {
                Items = new List<SubscriptionItemOptions>
                {
                    new SubscriptionItemOptions
                    {
                        Id = currentItem.Id,
                        Price = newPriceId
                    }
                },
                ProrationBehavior = "create_prorations"
            };

            var updatedSubscription = await subscriptionService.UpdateAsync(
                license.StripeSubscriptionId, updateOptions);

            // Update local license
            var oldPlanName = license.Plan?.Name;
            license.PlanId = newPlan.Id;
            license.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            // Notify tenancy-service about the plan change
            await _bus.Publish(new PlanUpdatedEvent(
                TenantId: Guid.Empty,
                Slug: tenantId,
                PlanTier: newPlan.Tier,
                UpdatedAt: DateTime.UtcNow
            ));

            _logger.LogInformation(
                "Plan changed for tenant {TenantId}: {OldPlan} -> {NewPlan}",
                tenantId, oldPlanName, newPlan.Name);

            return Ok(new
            {
                success = true,
                subscription = new
                {
                    id = updatedSubscription.Id,
                    status = updatedSubscription.Status,
                    currentPeriodEnd = updatedSubscription.Items.Data.FirstOrDefault()?.CurrentPeriodEnd
                },
                newPlan = new
                {
                    newPlan.Id,
                    newPlan.Name,
                    newPlan.Tier
                }
            });
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Error changing plan for tenant {TenantId}", tenantId);
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Cancel subscription at period end
    /// </summary>
    [HttpPost("cancel-subscription")]
    public async Task<IActionResult> CancelSubscription()
    {
        var tenantId = HttpContext.GetTenantId();
        if (string.IsNullOrEmpty(tenantId))
            return Unauthorized(new { error = "Tenant ID not found" });

        var license = await _context.TenantLicenses
            .FirstOrDefaultAsync(l => l.TenantId == tenantId && l.IsActive);

        if (license == null || string.IsNullOrEmpty(license.StripeSubscriptionId))
            return NotFound(new { error = "No active subscription found" });

        try
        {
            var subscriptionService = new SubscriptionService();
            var updateOptions = new SubscriptionUpdateOptions
            {
                CancelAtPeriodEnd = true
            };

            var subscription = await subscriptionService.UpdateAsync(
                license.StripeSubscriptionId, updateOptions);

            license.Status = "cancelling";
            license.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            _logger.LogInformation("Subscription cancellation scheduled for tenant {TenantId}, cancels at {CancelAt}",
                tenantId, subscription.Items.Data.FirstOrDefault()?.CurrentPeriodEnd);

            return Ok(new
            {
                success = true,
                cancelAt = subscription.Items.Data.FirstOrDefault()?.CurrentPeriodEnd,
                message = "Subscription will be cancelled at the end of the current billing period"
            });
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Error cancelling subscription for tenant {TenantId}", tenantId);
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Reactivate a subscription that was scheduled for cancellation
    /// </summary>
    [HttpPost("reactivate-subscription")]
    public async Task<IActionResult> ReactivateSubscription()
    {
        var tenantId = HttpContext.GetTenantId();
        if (string.IsNullOrEmpty(tenantId))
            return Unauthorized(new { error = "Tenant ID not found" });

        var license = await _context.TenantLicenses
            .FirstOrDefaultAsync(l => l.TenantId == tenantId &&
                (l.Status == "cancelling" || l.Status == "active") &&
                l.IsActive);

        if (license == null || string.IsNullOrEmpty(license.StripeSubscriptionId))
            return NotFound(new { error = "No subscription found to reactivate" });

        try
        {
            var subscriptionService = new SubscriptionService();
            var updateOptions = new SubscriptionUpdateOptions
            {
                CancelAtPeriodEnd = false
            };

            var subscription = await subscriptionService.UpdateAsync(
                license.StripeSubscriptionId, updateOptions);

            license.Status = "active";
            license.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            _logger.LogInformation("Subscription reactivated for tenant {TenantId}", tenantId);

            return Ok(new
            {
                success = true,
                message = "Subscription reactivated successfully"
            });
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Error reactivating subscription for tenant {TenantId}", tenantId);
            return BadRequest(new { error = ex.Message });
        }
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

public record CreateCheckoutSessionRequest(
    Guid PlanId,
    string? Period = "monthly",
    string? SuccessUrl = null,
    string? CancelUrl = null
);
public record CreatePortalSessionRequest(string? ReturnUrl = null);
public record PlanChangeRequest(Guid PlanId, string? Period = "monthly");
