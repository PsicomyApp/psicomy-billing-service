using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Psicomy.Services.Billing.Infrastructure;

public static class OpenTelemetryExtensions
{
    public static IServiceCollection AddOpenTelemetryConfiguration(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var serviceName = configuration["OpenTelemetry:ServiceName"] ?? "psicomy-billing-service";
        var serviceVersion = configuration["OpenTelemetry:ServiceVersion"] ?? "1.0.0";
        var otlpEndpoint = configuration["OpenTelemetry:OtlpEndpoint"] 
            ?? Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT")
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
                    options.Endpoint = new Uri(otlpEndpoint);
                }));

        return services;
    }
}
