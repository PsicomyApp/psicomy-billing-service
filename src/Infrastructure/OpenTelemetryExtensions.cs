using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Psicomy.Services.Billing.Infrastructure;

public static class OpenTelemetryExtensions
{
    /// <summary>
    /// Configures OpenTelemetry with distributed tracing for the application.
    /// Includes instrumentation for ASP.NET Core, HTTP Client, and Entity Framework Core.
    /// Exports telemetry data using OTLP protocol to the configured endpoint.
    /// </summary>
    /// <param name="services">The service collection to add OpenTelemetry to.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddOpenTelemetryConfiguration(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var serviceName = configuration["OpenTelemetry:ServiceName"] ?? "psicomy-billing-service";
        var serviceVersion = configuration["OpenTelemetry:ServiceVersion"] ?? "1.0.0";
        var otlpEndpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT")
            ?? configuration["OpenTelemetry:OtlpEndpoint"]
            ?? "http://localhost:4317";

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(serviceName: serviceName, serviceVersion: serviceVersion))
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddEntityFrameworkCoreInstrumentation()
                .AddOtlpExporter(options =>
                {
                    if (!Uri.TryCreate(otlpEndpoint, UriKind.Absolute, out var endpoint))
                    {
                        throw new InvalidOperationException(
                            $"Invalid OTLP endpoint configured: '{otlpEndpoint}'. " +
                            "Please configure a valid URI in appsettings.json (OpenTelemetry:OtlpEndpoint) " +
                            "or set the OTEL_EXPORTER_OTLP_ENDPOINT environment variable.");
                    }
                    options.Endpoint = endpoint;
                }));

        return services;
    }
}
