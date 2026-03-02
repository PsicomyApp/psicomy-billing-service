using Microsoft.EntityFrameworkCore;
using Psicomy.Services.Billing.Data;
using Psicomy.Services.Billing.Models;
using Psicomy.Shared.Kernel.Messaging.Events;
using Rebus.Bus;

namespace Psicomy.Services.Billing.Workers;

public class TrialReminderWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TrialReminderWorker> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromHours(12);

    private static readonly int[] Milestones = [7, 3];

    public TrialReminderWorker(IServiceProvider serviceProvider, ILogger<TrialReminderWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("TrialReminderWorker started. Interval: {Interval}", _interval);

        await Task.Delay(TimeSpan.FromMinutes(3), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CheckExpiringTrialsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "TrialReminderWorker failed to check expiring trials");
            }

            await Task.Delay(_interval, stoppingToken);
        }
    }

    private async Task CheckExpiringTrialsAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        var bus = scope.ServiceProvider.GetRequiredService<IBus>();

        var now = DateTime.UtcNow;
        var maxThreshold = now.AddDays(Milestones[0] + 1); // 7 days + 1 buffer

        var expiringLicenses = await context.TenantLicenses
            .Include(l => l.Plan)
            .Where(l =>
                l.IsActive &&
                l.Status == "trial" &&
                l.TrialEndDate != null &&
                l.TrialEndDate.Value > now &&
                l.TrialEndDate.Value <= maxThreshold)
            .ToListAsync(ct);

        _logger.LogInformation("Found {Count} trial licenses expiring within 7 days", expiringLicenses.Count);

        foreach (var license in expiringLicenses)
        {
            ct.ThrowIfCancellationRequested();

            var daysRemaining = (int)Math.Ceiling((license.TrialEndDate!.Value - now).TotalDays);

            foreach (var milestone in Milestones)
            {
                if (daysRemaining > milestone) continue;

                var alreadySent = await context.SentTrialReminders
                    .AnyAsync(r => r.LicenseId == license.Id && r.MilestoneDay == milestone, ct);

                if (alreadySent) continue;

                var hasPaymentMethod = !string.IsNullOrEmpty(license.StripeCustomerId);
                var planTier = license.Plan?.Tier ?? "Unknown";
                var planPrice = license.Plan?.MonthlyPrice ?? 0m;

                await bus.Publish(new TrialEndingEvent(
                    TenantId: license.TenantId,
                    TrialEndsAt: license.TrialEndDate.Value,
                    PlanTier: planTier,
                    PlanPrice: planPrice,
                    DaysRemaining: daysRemaining,
                    HasPaymentMethod: hasPaymentMethod,
                    OccurredAt: now
                ));

                context.SentTrialReminders.Add(new SentTrialReminder
                {
                    Id = Guid.NewGuid(),
                    LicenseId = license.Id,
                    TenantId = license.TenantId,
                    MilestoneDay = milestone,
                    SentAt = now
                });

                _logger.LogInformation(
                    "Published TrialEndingEvent for tenant {TenantId}: {DaysRemaining} days remaining (milestone {Milestone}d)",
                    license.TenantId, daysRemaining, milestone);

                break; // Only publish the most relevant milestone
            }
        }

        await context.SaveChangesAsync(ct);
    }
}
