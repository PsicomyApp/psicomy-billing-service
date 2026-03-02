using Microsoft.Extensions.Diagnostics.HealthChecks;
using Rebus.Bus;

namespace Psicomy.Services.Billing.Infrastructure.HealthChecks;

public class RabbitMqHealthCheck : IHealthCheck
{
    private readonly IBus _bus;
    private readonly ILogger<RabbitMqHealthCheck> _logger;

    public RabbitMqHealthCheck(IBus bus, ILogger<RabbitMqHealthCheck> logger)
    {
        _bus = bus;
        _logger = logger;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var workers = _bus.Advanced.Workers;
            if (workers.Count > 0)
            {
                return Task.FromResult(HealthCheckResult.Healthy("RabbitMQ transport is active"));
            }

            return Task.FromResult(HealthCheckResult.Degraded("RabbitMQ transport has no active workers"));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RabbitMQ health check failed");
            return Task.FromResult(HealthCheckResult.Unhealthy("RabbitMQ transport error", ex));
        }
    }
}
