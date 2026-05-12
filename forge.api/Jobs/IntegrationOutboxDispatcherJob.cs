using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Jobs;

public class IntegrationOutboxDispatcherJob(
    AppDbContext db,
    IEmailService emailService,
    IClock clock,
    ILogger<IntegrationOutboxDispatcherJob> logger)
{
    private const int BatchSize = 25;
    private static readonly TimeSpan[] BackoffSchedule =
    [
        TimeSpan.FromMinutes(1),
        TimeSpan.FromMinutes(2),
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromMinutes(60),
    ];

    public async Task DispatchPendingAsync(CancellationToken ct = default)
    {
        var now = clock.UtcNow;

        var ready = await db.IntegrationOutboxEntries
            .Where(e => (e.Status == OutboxStatus.Pending || e.Status == OutboxStatus.Failed)
                        && (e.NextAttemptAt == null || e.NextAttemptAt <= now))
            .OrderBy(e => e.NextAttemptAt ?? e.CreatedAt)
            .Take(BatchSize)
            .ToListAsync(ct);

        if (ready.Count == 0)
        {
            return;
        }

        logger.LogInformation("Outbox dispatcher picked up {Count} entries", ready.Count);

        foreach (var entry in ready)
        {
            ct.ThrowIfCancellationRequested();

            entry.Status = OutboxStatus.InFlight;
            entry.LastAttemptAt = clock.UtcNow;
            entry.AttemptCount++;
            await db.SaveChangesAsync(ct);

            try
            {
                await DispatchAsync(entry, ct);
                entry.Status = OutboxStatus.Sent;
                entry.SentAt = clock.UtcNow;
                entry.LastError = null;
                entry.NextAttemptAt = null;
                await db.SaveChangesAsync(ct);

                logger.LogInformation(
                    "Outbox sent: provider={Provider} key={Key} id={Id} attempts={Attempts}",
                    entry.Provider, entry.OperationKey, entry.Id, entry.AttemptCount);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                entry.LastError = Truncate(ex.Message, 4000);

                if (entry.AttemptCount >= entry.MaxAttempts)
                {
                    entry.Status = OutboxStatus.DeadLetter;
                    entry.NextAttemptAt = null;
                    logger.LogError(ex,
                        "Outbox dead-lettered after {Attempts} attempts: provider={Provider} key={Key} id={Id}",
                        entry.AttemptCount, entry.Provider, entry.OperationKey, entry.Id);
                }
                else
                {
                    var backoff = BackoffSchedule[Math.Min(entry.AttemptCount - 1, BackoffSchedule.Length - 1)];
                    entry.Status = OutboxStatus.Failed;
                    entry.NextAttemptAt = clock.UtcNow.Add(backoff);
                    logger.LogWarning(ex,
                        "Outbox attempt {Attempts}/{Max} failed, retrying at {Next}: provider={Provider} key={Key} id={Id}",
                        entry.AttemptCount, entry.MaxAttempts, entry.NextAttemptAt,
                        entry.Provider, entry.OperationKey, entry.Id);
                }

                await db.SaveChangesAsync(ct);
            }
        }
    }

    private async Task DispatchAsync(IntegrationOutboxEntry entry, CancellationToken ct)
    {
        switch (entry.Provider)
        {
            case IntegrationProvider.Email:
                var message = JsonSerializer.Deserialize<EmailMessage>(entry.Payload)
                    ?? throw new InvalidOperationException($"Failed to deserialize email payload for entry {entry.Id}");
                await emailService.SendAsync(message, ct);
                break;

            case IntegrationProvider.DocuSeal:
            case IntegrationProvider.QuickBooks:
            case IntegrationProvider.Shipping:
            case IntegrationProvider.Webhook:
            case IntegrationProvider.Sms:
                throw new NotImplementedException(
                    $"Outbox dispatcher does not yet handle {entry.Provider}. " +
                    $"Refactor the provider's call sites to use IIntegrationOutboxService " +
                    $"and add a dispatch branch here.");

            default:
                throw new InvalidOperationException($"Unknown provider {entry.Provider}");
        }
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max];
}
