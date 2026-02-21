using Amazon.S3;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Psicomy.Services.Billing.Data;
using Psicomy.Services.Billing.Infrastructure;
using Psicomy.Services.Billing.Middleware;
using Serilog;
using Psicomy.Shared.Kernel.Observability;

const string serviceName = "psicomy-billing-service";

try
{
    var builder = WebApplication.CreateBuilder(args);
    
    // Check if OpenTelemetry should be enabled
    var otelEndpoint = builder.Configuration["OpenTelemetry:OtlpEndpoint"] ?? Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT") ?? "";
    var isDevelopment = builder.Environment.IsDevelopment();
    var otelEnabled = Environment.GetEnvironmentVariable("OTEL_ENABLED")?.ToLowerInvariant() != "false";
    var shouldUseOtel = otelEnabled && !string.IsNullOrEmpty(otelEndpoint) &&
        (isDevelopment || (!otelEndpoint.Contains("localhost") && !otelEndpoint.Contains("127.0.0.1")));

    builder.Host.UseSerilog((context, services, loggerConfiguration) =>
    {
        loggerConfiguration
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()
            .WriteTo.Console()
            .WriteToPsicomyOpenTelemetry(context.Configuration, serviceName);
    });

    if (shouldUseOtel)
    {
        builder.Services.AddPsicomyOpenTelemetry(builder.Configuration, serviceName);
    }
    
    // Add services
    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddHttpContextAccessor();

    // Database
    var connectionString = builder.Configuration.GetConnectionString("BillingDb")
        ?? builder.Configuration.GetConnectionString("DefaultConnection")
        ?? Environment.GetEnvironmentVariable("DATABASE_CONNECTION_STRING");

    builder.Services.AddDbContext<BillingDbContext>(options =>
        options.UseNpgsql(connectionString));

    // Stripe
    builder.Services.AddStripeServices(builder.Configuration);

    // JWT Authentication
    builder.Services.AddJwtAuthentication(builder.Configuration);

    // Storage (MinIO/S3)
    var storageSettings = builder.Configuration.GetSection("Storage").Get<StorageSettings>() ?? new StorageSettings();
    builder.Services.Configure<StorageSettings>(builder.Configuration.GetSection("Storage"));
    builder.Services.AddSingleton<IAmazonS3>(sp =>
    {
        var config = new AmazonS3Config
        {
            ServiceURL = storageSettings.Endpoint,
            ForcePathStyle = true,
            UseHttp = !storageSettings.UseSSL
        };
        return new AmazonS3Client(storageSettings.AccessKey, storageSettings.SecretKey, config);
    });
    builder.Services.AddScoped<IStorageService, S3StorageService>();

    // CORS
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("AllowFrontend", policy =>
        {
            policy.WithOrigins(
                    "http://localhost:5173",
                    "http://1.2.7.0.0.1:5173",
                    "https://psicomy.com.br",
                    "https://www.psicomy.com.br",
                    "https://app.psicomy.com",
                    "https://signoz.psicomy.com.br")
                .AllowAnyMethod()
                .AllowAnyHeader()
                .AllowCredentials();
        });
    });

    // Health checks
    builder.Services.AddHealthChecks()
        .AddNpgSql(connectionString ?? "", name: "database");

    var app = builder.Build();

    // Apply migrations
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        await db.Database.MigrateAsync();
    }

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
    }

    app.UseRouting();
    app.UseCors("AllowFrontend");

    // Tenant resolution middleware
    app.UseMiddleware<TenantResolutionMiddleware>();

    app.UseAuthentication();
    app.UseAuthorization();

    app.MapControllers();

    app.MapHealthChecks("/health/live", new HealthCheckOptions
    {
        Predicate = _ => false
    });

    app.MapHealthChecks("/health/ready", new HealthCheckOptions
    {
        Predicate = registration => registration.Tags.Contains("ready")
    });

    Log.Information("Starting web application");
    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Psicomy.Services.Billing terminated unexpectedly");
    throw;
}
finally
{
    Log.CloseAndFlush();
}
