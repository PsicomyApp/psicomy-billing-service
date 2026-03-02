using System.Diagnostics.Metrics;

namespace Psicomy.Services.Billing.Infrastructure;

public class BillingMetrics
{
    public const string MeterName = "Psicomy.Billing";

    private readonly Counter<long> _checkoutSessionsCreated;
    private readonly Counter<long> _webhooksProcessed;
    private readonly Counter<long> _paymentFailures;
    private readonly Counter<long> _subscriptionChanges;

    public BillingMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);

        _checkoutSessionsCreated = meter.CreateCounter<long>(
            "billing.checkout_sessions_created",
            description: "Number of Stripe checkout sessions created");

        _webhooksProcessed = meter.CreateCounter<long>(
            "billing.webhooks_processed",
            description: "Number of Stripe webhooks processed");

        _paymentFailures = meter.CreateCounter<long>(
            "billing.payment_failures",
            description: "Number of payment failures");

        _subscriptionChanges = meter.CreateCounter<long>(
            "billing.subscription_changes",
            description: "Number of subscription state changes");
    }

    public void RecordCheckoutSessionCreated(string planTier)
    {
        _checkoutSessionsCreated.Add(1, new KeyValuePair<string, object?>("plan_tier", planTier));
    }

    public void RecordWebhookProcessed(string eventType)
    {
        _webhooksProcessed.Add(1, new KeyValuePair<string, object?>("event_type", eventType));
    }

    public void RecordPaymentFailure(string? tenantId = null)
    {
        _paymentFailures.Add(1, new KeyValuePair<string, object?>("tenant_id", tenantId ?? "unknown"));
    }

    public void RecordSubscriptionChange(string newStatus)
    {
        _subscriptionChanges.Add(1, new KeyValuePair<string, object?>("status", newStatus));
    }
}
