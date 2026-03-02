using Microsoft.Extensions.Diagnostics.HealthChecks;
using Stripe;

namespace Psicomy.Services.Billing.Infrastructure.HealthChecks;

public class StripeHealthCheck : IHealthCheck
{
    private readonly ILogger<StripeHealthCheck> _logger;

    public StripeHealthCheck(ILogger<StripeHealthCheck> logger)
    {
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var service = new BalanceService();
            var balance = await service.GetAsync(cancellationToken: cancellationToken);

            return HealthCheckResult.Healthy("Stripe API is reachable");
        }
        catch (StripeException ex)
        {
            _logger.LogWarning(ex, "Stripe health check failed");
            return HealthCheckResult.Degraded("Stripe API unreachable", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Stripe health check error");
            return HealthCheckResult.Unhealthy("Stripe API error", ex);
        }
    }
}
