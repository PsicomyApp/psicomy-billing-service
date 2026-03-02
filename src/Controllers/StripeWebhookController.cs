using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Psicomy.Services.Billing.Data;
using Psicomy.Services.Billing.Infrastructure;
using Psicomy.Services.Billing.Middleware;
using Psicomy.Services.Billing.Models;
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
    private readonly BillingMetrics _metrics;

    public StripeWebhookController(
        Microsoft.Extensions.Options.IOptions<StripeOptions> stripeOptions,
        BillingDbContext context,
        ILogger<StripeWebhookController> logger,
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
    /// Consolidated Stripe webhook endpoint - single handler for all Stripe events.
    /// Idempotent: duplicate events are detected and skipped.
    /// Processes events locally and publishes RabbitMQ events for tenancy-service consumption.
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

            // Idempotency check: skip if already processed
            if (await _context.ProcessedStripeEvents.AnyAsync(e => e.EventId == stripeEvent.Id))
            {
                _logger.LogInformation("Duplicate Stripe event skipped: {EventType} ({EventId})", stripeEvent.Type, stripeEvent.Id);
                return Ok(new { received = true });
            }

            // Process event within a transaction for atomicity
            await using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
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

                    case "customer.subscription.created":
                    case "customer.subscription.updated":
                        await HandleSubscriptionUpdated(stripeEvent);
                        break;

                    case "customer.subscription.deleted":
                        await HandleSubscriptionDeleted(stripeEvent);
                        break;

                    default:
                        _logger.LogInformation("Unhandled Stripe event type: {EventType}", stripeEvent.Type);
                        break;
                }

                // Record the event as processed within the same transaction
                _context.ProcessedStripeEvents.Add(new ProcessedStripeEvent
                {
                    EventId = stripeEvent.Id,
                    EventType = stripeEvent.Type,
                    ProcessedAt = DateTime.UtcNow
                });
                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                _metrics.RecordWebhookProcessed(stripeEvent.Type);
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
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
            license = new TenantLicense
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

        if (!string.IsNullOrEmpty(session.SubscriptionId))
        {
            await _bus.Publish(new SubscriptionStatusChangedEvent(
                StripeSubscriptionId: session.SubscriptionId,
                Status: "active",
                StripeCustomerId: session.CustomerId,
                EndedAt: null,
                OccurredAt: DateTime.UtcNow
            ));
        }
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
            license.PaymentRetryCount = 0;
            license.LastPaymentError = null;
            license.GracePeriodEndsAt = null;

            if (invoice.PeriodEnd != default)
                license.ExpiresAt = invoice.PeriodEnd.AddDays(3);

            string? paymentIntentId = null;
            try
            {
                var payment = invoice.Payments?.FirstOrDefault(p => p.Status == "paid");
                paymentIntentId = payment?.Payment?.PaymentIntentId;
            }
            catch
            {
                // PaymentIntentId might not be available in all Stripe API versions
            }

            // Extract payment method type (card, pix, etc.)
            string? paymentMethodType = null;
            try
            {
                var payment = invoice.Payments?.FirstOrDefault(p => p.Status == "paid");
                var pmTypes = payment?.Payment?.PaymentMethodDetails;
                if (pmTypes?.Card != null) paymentMethodType = "card";
                else if (pmTypes?.Pix != null) paymentMethodType = "pix";
            }
            catch
            {
                // Payment method type extraction is best-effort
            }

            var paymentInvoice = new PaymentInvoice
            {
                Id = Guid.NewGuid(),
                TenantId = license.TenantId,
                LicenseId = license.Id,
                StripeInvoiceId = invoice.Id,
                StripePaymentIntentId = paymentIntentId,
                PaymentMethodType = paymentMethodType,
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

        var subscriptionId = invoice.Lines?.Data?.FirstOrDefault()?.SubscriptionId;
        await _bus.Publish(new PaymentSucceededEvent(
            StripeCustomerId: invoice.CustomerId,
            StripeSubscriptionId: subscriptionId,
            StripeInvoiceId: invoice.Id,
            Amount: invoice.AmountPaid / 100m,
            Currency: invoice.Currency ?? "brl",
            OccurredAt: DateTime.UtcNow
        ));
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
            license.PaymentRetryCount += 1;
            license.LastPaymentError = license.PaymentRetryCount > 1
                ? $"Payment failed after {license.PaymentRetryCount} attempts."
                : "Payment failed on Stripe.";
            license.UpdatedAt = DateTime.UtcNow;

            // Set grace period on first failure
            if (license.GracePeriodEndsAt == null)
            {
                var graceDays = _stripeOptions.GracePeriodDays;
                license.GracePeriodEndsAt = (license.ExpiresAt ?? DateTime.UtcNow).AddDays(graceDays);
            }

            // During grace period: keep active with past_due status
            if (DateTime.UtcNow < license.GracePeriodEndsAt)
            {
                license.Status = "past_due";
                // IsActive remains true - full read+write access
            }
            else
            {
                // Grace period expired: restrict to read-only (suspended)
                license.Status = "suspended";
            }

            await _context.SaveChangesAsync();

            _logger.LogWarning(
                "License {LicenseId} payment failed. Retry: {RetryCount}, GracePeriodEnds: {GracePeriodEnds}, Status: {Status}",
                license.Id, license.PaymentRetryCount, license.GracePeriodEndsAt, license.Status);

            _metrics.RecordPaymentFailure(license.TenantId);
        }

        var subscriptionId = invoice.Lines?.Data?.FirstOrDefault()?.SubscriptionId;
        await _bus.Publish(new PaymentFailedEvent(
            StripeCustomerId: invoice.CustomerId,
            StripeSubscriptionId: subscriptionId,
            StripeInvoiceId: invoice.Id,
            Amount: invoice.AmountDue / 100m,
            Currency: invoice.Currency ?? "brl",
            OccurredAt: DateTime.UtcNow
        ));
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

        await _bus.Publish(new SubscriptionCancelledEvent(
            StripeSubscriptionId: subscription.Id,
            StripeCustomerId: subscription.CustomerId,
            CancelledAt: DateTime.UtcNow,
            OccurredAt: DateTime.UtcNow
        ));
    }

    private async Task HandleSubscriptionUpdated(Event stripeEvent)
    {
        var subscription = stripeEvent.Data.Object as Subscription;
        if (subscription == null) return;

        _logger.LogInformation("Subscription {EventType}: {SubscriptionId}, Status: {Status}",
            stripeEvent.Type, subscription.Id, subscription.Status);

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
        }

        var mappedStatus = subscription.Status switch
        {
            "active" => "active",
            "past_due" => "past_due",
            "canceled" => "cancelled",
            "unpaid" => "payment_failed",
            "trialing" => "trial",
            _ => subscription.Status
        };

        _metrics.RecordSubscriptionChange(mappedStatus);

        await _bus.Publish(new SubscriptionStatusChangedEvent(
            StripeSubscriptionId: subscription.Id,
            Status: mappedStatus,
            StripeCustomerId: subscription.CustomerId,
            EndedAt: subscription.EndedAt,
            OccurredAt: DateTime.UtcNow
        ));
    }
}
