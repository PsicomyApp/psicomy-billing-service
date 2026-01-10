namespace Psicomy.Services.Billing.Middleware;

public class TenantResolutionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<TenantResolutionMiddleware> _logger;

    public TenantResolutionMiddleware(RequestDelegate next, ILogger<TenantResolutionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Try to get tenant from header
        if (context.Request.Headers.TryGetValue("X-Tenant-Code", out var tenantHeader))
        {
            var tenantCode = tenantHeader.ToString().Trim().ToLowerInvariant();
            context.Items["TenantId"] = tenantCode;
            _logger.LogDebug("Tenant resolved from header: {TenantCode}", tenantCode);
        }
        // Fallback: get from JWT claim
        else if (context.User.Identity?.IsAuthenticated == true)
        {
            var tenantClaim = context.User.FindFirst("tenant")?.Value;
            if (!string.IsNullOrEmpty(tenantClaim))
            {
                context.Items["TenantId"] = tenantClaim;
                _logger.LogDebug("Tenant resolved from JWT: {TenantCode}", tenantClaim);
            }
        }

        await _next(context);
    }
}

public static class TenantContextExtensions
{
    public static string? GetTenantId(this HttpContext context)
    {
        return context.Items["TenantId"]?.ToString();
    }
}
