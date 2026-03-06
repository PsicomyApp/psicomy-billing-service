using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Psicomy.Services.Billing.Data;
using Psicomy.Services.Billing.Infrastructure;
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
    private readonly BillingMetrics _metrics;

    public StripeController(
        IOptions<StripeOptions> stripeOptions,
        BillingDbContext context,
        ILogger<StripeController> logger,
        IBus bus,
        BillingMetrics metrics)
    {
        _stripeOptions = stripeOptions.Value;
        _context = context;
        _logger = logger;
        _bus = bus;
        _metrics = metrics;
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
                p.MaxUsers,
                p.IncludedUsers,
                p.ExtraSeatPrice
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

        if (string.IsNullOrWhiteSpace(_stripeOptions.SecretKey))
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "Stripe is not configured" });

        if (!string.IsNullOrWhiteSpace(request.Period) &&
            !string.Equals(request.Period, "monthly", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(request.Period, "annual", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { error = "Invalid billing period. Use 'monthly' or 'annual'." });
        }

        if (request.ExtraSeats is < 0)
            return BadRequest(new { error = "Extra seats cannot be negative." });

        var plan = await _context.PaymentPlans
            .FirstOrDefaultAsync(p => p.Id == request.PlanId && p.IsActive);

        if (plan == null)
            return NotFound(new { error = "Payment plan not found" });

        // Free plan - activate without payment
        if (plan.Tier == "Free" || plan.MonthlyPrice == 0)
            return await ActivateFreePlan(tenantId, plan.Id);

        // Enterprise plan - checkout blocked, must contact sales
        if (plan.Tier == "Enterprise")
            return BadRequest(new { error = "Enterprise plan requires contacting sales (contato@psicomy.com.br)" });

        var normalizedPeriod = string.Equals(request.Period, "annual", StringComparison.OrdinalIgnoreCase)
            ? "annual"
            : "monthly";

        // Determine which Stripe Price ID to use
        var isAnnual = normalizedPeriod == "annual";
        var stripePriceId = isAnnual ? plan.StripePriceIdYearly : plan.StripePriceIdMonthly;

        if (string.IsNullOrEmpty(stripePriceId))
        {
            _logger.LogError("No Stripe Price ID configured for plan {PlanId}, period {Period}", plan.Id, request.Period);
            return BadRequest(new { error = "Plan pricing not configured" });
        }

        try
        {
            var fallbackBaseUri = ResolveBaseRedirectUri(request.SuccessUrl, request.CancelUrl);
            var successUrl = BuildSafeRedirectUrl(request.SuccessUrl, fallbackBaseUri, "/dashboard?payment=success");
            var cancelUrl = BuildSafeRedirectUrl(request.CancelUrl, fallbackBaseUri, "/upgrade?payment=cancelled");
            var extraSeats = request.ExtraSeats ?? 0;

            var lineItems = new List<SessionLineItemOptions>
            {
                new SessionLineItemOptions
                {
                    Price = stripePriceId,
                    Quantity = 1
                }
            };

            if (extraSeats > 0)
            {
                // Per-seat addon line item for Team and Business plans.
                if ((plan.ExtraSeatPrice ?? 0) <= 0)
                {
                    return BadRequest(new { error = "Extra seats are not available for this plan." });
                }

                var perSeatPriceId = isAnnual ? plan.StripePriceIdPerSeatYearly : plan.StripePriceIdPerSeat;
                if (string.IsNullOrWhiteSpace(perSeatPriceId))
                {
                    _logger.LogError("Per-seat Stripe Price ID missing for plan {PlanId} ({PlanTier})", plan.Id, plan.Tier);
                    return BadRequest(new { error = "Per-seat pricing is not configured for this plan." });
                }

                lineItems.Add(new SessionLineItemOptions
                {
                    Price = perSeatPriceId,
                    Quantity = extraSeats
                });
            }

            var options = new SessionCreateOptions
            {
                Mode = "subscription",
                LineItems = lineItems,
                SuccessUrl = successUrl,
                CancelUrl = cancelUrl,
                Metadata = new Dictionary<string, string>
                {
                    { "tenant_id", tenantId },
                    { "plan_id", plan.Id.ToString() },
                    { "period", normalizedPeriod },
                    { "extra_seats", extraSeats.ToString() }
                },
                CustomerEmail = User.FindFirst("email")?.Value,
                AutomaticTax = new SessionAutomaticTaxOptions { Enabled = true },
                CustomerCreation = "always",
                TaxIdCollection = new SessionTaxIdCollectionOptions { Enabled = true },
                SubscriptionData = new SessionSubscriptionDataOptions
                {
                    Metadata = new Dictionary<string, string>
                    {
                        { "tenant_id", tenantId },
                        { "plan_id", plan.Id.ToString() },
                        { "per_seat_price_monthly", plan.StripePriceIdPerSeat ?? "" },
                        { "per_seat_price_yearly", plan.StripePriceIdPerSeatYearly ?? "" }
                    }
                }
            };

            // Stripe Connect: apply application fee and transfer for Business/Enterprise
            if ((plan.Tier == "Business" || plan.Tier == "Enterprise") &&
                !string.IsNullOrEmpty(_stripeOptions.DestinationId))
            {
                var feePercent = plan.ConnectFeePercent ?? _stripeOptions.ApplicationFeePercent;
                options.SubscriptionData.ApplicationFeePercent = feePercent;
                options.SubscriptionData.TransferData = new SessionSubscriptionDataTransferDataOptions
                {
                    Destination = _stripeOptions.DestinationId
                };

                _logger.LogInformation(
                    "Stripe Connect enabled for plan {PlanTier}: fee {FeePercent}%, destination {Destination}",
                    plan.Tier, feePercent, _stripeOptions.DestinationId);
            }

            var service = new SessionService();
            var session = await service.CreateAsync(options);

            _metrics.RecordCheckoutSessionCreated(plan.Tier);
            _logger.LogInformation("Created checkout session {SessionId} for tenant {TenantId}, plan {PlanId}, period {Period}",
                session.Id, tenantId, plan.Id, normalizedPeriod);

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

        if (string.IsNullOrWhiteSpace(_stripeOptions.SecretKey))
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { error = "Stripe is not configured" });

        var license = await _context.TenantLicenses
            .Where(l => l.TenantId == tenantId && !string.IsNullOrEmpty(l.StripeCustomerId))
            .OrderByDescending(l => l.IsActive)
            .ThenByDescending(l => l.UpdatedAt)
            .FirstOrDefaultAsync();

        if (license == null || string.IsNullOrEmpty(license.StripeCustomerId))
            return NotFound(new { error = "No subscription found" });

        try
        {
            var fallbackBaseUri = ResolveBaseRedirectUri(request.ReturnUrl);
            var returnUrl = BuildSafeRedirectUrl(
                request.ReturnUrl,
                fallbackBaseUri,
                "/dashboard/settings/billing");

            var options = new Stripe.BillingPortal.SessionCreateOptions
            {
                Customer = license.StripeCustomerId,
                ReturnUrl = returnUrl
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
            .Where(l => l.TenantId == tenantId)
            .OrderByDescending(l => l.IsActive)
            .ThenByDescending(l => l.UpdatedAt)
            .FirstOrDefaultAsync();

        if (license == null)
            return NotFound(new { error = "No subscription found" });

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

            var isUpgrade = preview.AmountDue > 0;
            var isDowngrade = preview.AmountDue < 0;

            return Ok(new
            {
                currentPlan = license.Plan?.Name,
                currentTier = license.Plan?.Tier,
                newPlan = newPlan.Name,
                newTier = newPlan.Tier,
                proratedAmount = Math.Abs(preview.AmountDue) / 100m,
                currency = preview.Currency,
                nextBillingDate = subscription.Items.Data.FirstOrDefault()?.CurrentPeriodEnd,
                immediateCharge = isUpgrade,
                credit = isDowngrade,
                direction = isUpgrade ? "upgrade" : isDowngrade ? "downgrade" : "same"
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

    private Uri ResolveBaseRedirectUri(params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (TryValidateRedirectUrl(candidate, out var validatedUri))
            {
                return new Uri(validatedUri.GetLeftPart(UriPartial.Authority));
            }
        }

        if (TryValidateRedirectUrl(_stripeOptions.DefaultFrontendUrl, out var configuredDefaultUri))
        {
            return new Uri(configuredDefaultUri.GetLeftPart(UriPartial.Authority));
        }

        return new Uri("https://psicomy.com.br");
    }

    private string BuildSafeRedirectUrl(string? candidateUrl, Uri fallbackBaseUri, string fallbackPath)
    {
        if (TryValidateRedirectUrl(candidateUrl, out var validatedUri))
        {
            return validatedUri.ToString();
        }

        return new Uri(fallbackBaseUri, fallbackPath).ToString();
    }

    private bool TryValidateRedirectUrl(string? candidateUrl, out Uri validatedUri)
    {
        validatedUri = null!;

        if (string.IsNullOrWhiteSpace(candidateUrl) ||
            !Uri.TryCreate(candidateUrl, UriKind.Absolute, out var parsedUri))
        {
            return false;
        }

        var isHttps = parsedUri.Scheme == Uri.UriSchemeHttps;
        var isLocalHttp = parsedUri.Scheme == Uri.UriSchemeHttp &&
            (string.Equals(parsedUri.Host, "localhost", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(parsedUri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase));

        if (!isHttps && !isLocalHttp)
        {
            return false;
        }

        var isAllowedHost = (_stripeOptions.AllowedRedirectHosts ?? [])
            .Any(host =>
                string.Equals(host, parsedUri.Host, StringComparison.OrdinalIgnoreCase) ||
                (host.StartsWith('.') && parsedUri.Host.EndsWith(host, StringComparison.OrdinalIgnoreCase)));

        if (!isAllowedHost)
        {
            return false;
        }

        validatedUri = parsedUri;
        return true;
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
        _logger.LogInformation("Activated free plan for tenant {TenantId}", tenantId);

        return Ok(new
        {
            success = true,
            message = "Free plan activated successfully",
            redirectUrl = "/dashboard?plan=activated"
        });
    }
}

public record CreateCheckoutSessionRequest(
    Guid PlanId,
    string? Period = "monthly",
    string? SuccessUrl = null,
    string? CancelUrl = null,
    int? ExtraSeats = null
);
public record CreatePortalSessionRequest(string? ReturnUrl = null);
public record PlanChangeRequest(Guid PlanId, string? Period = "monthly");
