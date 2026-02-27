using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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
public class StripeWebhookController : ControllerBase
{
    private readonly StripeOptions _stripeOptions;
    private readonly BillingDbContext _context;
    private readonly ILogger<StripeWebhookController> _logger;
    private readonly IBus _bus;

    public StripeWebhookController(
        Microsoft.Extensions.Options.IOptions<StripeOptions> stripeOptions,
        BillingDbContext context,
        ILogger<StripeWebhookController> logger,
        IBus bus)
    {
        _stripeOptions = stripeOptions.Value;
        _context = context;
        _logger = logger;
        _bus = bus;
    }

    /// <summary>
    /// Stripe webhook endpoint - receives payment events
    /// </summary>
    [HttpPost("webhook")]
    [AllowAnonymous]
    public async Task<IActionResult> HandleWebhook()
    {
        var json = await new StreamReader(HttpContext.Request.Body).ReadToEndAsync();

        try
        {
            var stripeEvent = EventUtility.ConstructEvent(
                json,
                Request.Headers["Stripe-Signature"],
                _stripeOptions.WebhookSecret
            );

            _logger.LogInformation("Stripe webhook received: {EventType} ({EventId})", stripeEvent.Type, stripeEvent.Id);

            switch (stripeEvent.Type)
            {
                case "checkout.session.completed":
                    await HandleCheckoutSessionCompleted(stripeEvent);
                    break;

                case "invoice.payment_succeeded":
                    await HandleInvoicePaymentSucceeded(stripeEvent);
                    break;

                case "invoice.payment_failed":
                    await HandleInvoicePaymentFailed(stripeEvent);
                    break;

                case "customer.subscription.deleted":
                    await HandleSubscriptionDeleted(stripeEvent);
                    break;

                case "customer.subscription.updated":
                    await HandleSubscriptionUpdated(stripeEvent);
                    break;

                default:
                    _logger.LogInformation("Unhandled Stripe event type: {EventType}", stripeEvent.Type);
                    break;
            }

            return Ok(new { received = true });
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Stripe webhook signature verification failed");
            return BadRequest(new { error = "Webhook signature verification failed" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing Stripe webhook");
            return StatusCode(500, new { error = "Internal server error processing webhook" });
        }
    }

    private async Task HandleCheckoutSessionCompleted(Event stripeEvent)
    {
        var session = stripeEvent.Data.Object as Session;
        if (session == null)
        {
            _logger.LogWarning("checkout.session.completed: Session object is null");
            return;
        }

        _logger.LogInformation(
            "Checkout session completed: {SessionId}, Customer: {CustomerId}, Subscription: {SubscriptionId}",
            session.Id, session.CustomerId, session.SubscriptionId);

        var tenantId = session.Metadata?.GetValueOrDefault("tenant_id");
        if (string.IsNullOrEmpty(tenantId))
        {
            _logger.LogWarning("checkout.session.completed: No tenant_id in metadata");
            return;
        }

        var planIdStr = session.Metadata?.GetValueOrDefault("plan_id");
        Guid.TryParse(planIdStr, out var planId);

        var license = await _context.TenantLicenses
            .FirstOrDefaultAsync(l => l.TenantId == tenantId && l.IsActive);

        if (license != null)
        {
            license.StripeCustomerId = session.CustomerId;
            license.StripeSubscriptionId = session.SubscriptionId;
            license.Status = "active";
            license.UpdatedAt = DateTime.UtcNow;
            if (planId != Guid.Empty) license.PlanId = planId;
        }
        else
        {
            license = new Models.TenantLicense
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                PlanId = planId,
                StripeCustomerId = session.CustomerId,
                StripeSubscriptionId = session.SubscriptionId,
                Status = "active",
                IsActive = true,
                LicenseStartDate = DateTime.UtcNow,
                LicenseEndDate = DateTime.UtcNow.AddMonths(1),
                ExpiresAt = DateTime.UtcNow.AddMonths(1).AddDays(3),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _context.TenantLicenses.Add(license);
        }

        await _context.SaveChangesAsync();
        _logger.LogInformation("Updated license for tenant {TenantId} with Stripe subscription", tenantId);
    }

    private async Task HandleInvoicePaymentSucceeded(Event stripeEvent)
    {
        var invoice = stripeEvent.Data.Object as Invoice;
        if (invoice == null) return;

        _logger.LogInformation("Invoice payment succeeded: {InvoiceId}, Customer: {CustomerId}, Amount: {Amount}",
            invoice.Id, invoice.CustomerId, invoice.AmountPaid / 100m);

        var license = await _context.TenantLicenses
            .FirstOrDefaultAsync(l => l.StripeCustomerId == invoice.CustomerId && l.IsActive);

        if (license != null)
        {
            license.Status = "active";
            license.LastPaymentDate = DateTime.UtcNow;
            license.UpdatedAt = DateTime.UtcNow;

            if (invoice.PeriodEnd != default)
                license.ExpiresAt = invoice.PeriodEnd.AddDays(3);

            // Extract PaymentIntentId from the invoice
            string? paymentIntentId = null;
            try
            {
                var payment = invoice.Payments?.FirstOrDefault(p => p.Status == "paid");
                paymentIntentId = payment?.Payment?.PaymentIntentId;
            }
            catch
            {
                // Fallback: PaymentIntentId might not be available in all Stripe API versions
            }

            var paymentInvoice = new Models.PaymentInvoice
            {
                Id = Guid.NewGuid(),
                TenantId = license.TenantId,
                LicenseId = license.Id,
                StripeInvoiceId = invoice.Id,
                StripePaymentIntentId = paymentIntentId,
                Amount = invoice.AmountPaid / 100m,
                Currency = invoice.Currency,
                Status = "paid",
                PaidAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            _context.PaymentInvoices.Add(paymentInvoice);

            await _context.SaveChangesAsync();
        }
    }

    private async Task HandleInvoicePaymentFailed(Event stripeEvent)
    {
        var invoice = stripeEvent.Data.Object as Invoice;
        if (invoice == null) return;

        _logger.LogWarning("Invoice payment failed: {InvoiceId}, Customer: {CustomerId}",
            invoice.Id, invoice.CustomerId);

        var license = await _context.TenantLicenses
            .FirstOrDefaultAsync(l => l.StripeCustomerId == invoice.CustomerId && l.IsActive);

        if (license != null)
        {
            license.Status = "payment_failed";
            license.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }
    }

    private async Task HandleSubscriptionDeleted(Event stripeEvent)
    {
        var subscription = stripeEvent.Data.Object as Subscription;
        if (subscription == null) return;

        _logger.LogInformation("Subscription deleted: {SubscriptionId}, Customer: {CustomerId}",
            subscription.Id, subscription.CustomerId);

        var license = await _context.TenantLicenses
            .FirstOrDefaultAsync(l => l.StripeSubscriptionId == subscription.Id);

        if (license != null)
        {
            license.Status = "cancelled";
            license.IsActive = false;
            license.CancelledAt = DateTime.UtcNow;
            license.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }
    }

    private async Task HandleSubscriptionUpdated(Event stripeEvent)
    {
        var subscription = stripeEvent.Data.Object as Subscription;
        if (subscription == null) return;

        _logger.LogInformation("Subscription updated: {SubscriptionId}, Status: {Status}",
            subscription.Id, subscription.Status);

        var license = await _context.TenantLicenses
            .FirstOrDefaultAsync(l => l.StripeSubscriptionId == subscription.Id);

        if (license != null)
        {
            var newStatus = subscription.Status switch
            {
                "active" => "active",
                "past_due" => "past_due",
                "canceled" => "cancelled",
                "unpaid" => "payment_failed",
                "trialing" => "trial",
                _ => license.Status
            };

            license.Status = newStatus;

            if (subscription.EndedAt != default)
                license.ExpiresAt = subscription.EndedAt?.AddDays(3);

            license.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            var evt = new SubscriptionStatusChangedEvent(
                StripeSubscriptionId: subscription.Id,
                Status: newStatus,
                StripeCustomerId: subscription.CustomerId,
                EndedAt: subscription.EndedAt,
                OccurredAt: DateTime.UtcNow
            );
            await _bus.Publish(evt);
            _logger.LogInformation("Published SubscriptionStatusChangedEvent for Subscription {SubscriptionId}", subscription.Id);
        }
    }
}
