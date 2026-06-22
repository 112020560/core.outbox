using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OutboxWorker.Configuration;
using OutboxWorker.Publishers;
using SmartCore.Outbox.Infrastructure;
using SmartCore.Outbox.Models;

namespace OutboxWorker;

internal sealed class OutboxProcessor(
    ClaimManager claimManager,
    IPublisherFactory publisherFactory,
    OutboxDbContext dbContext,
    IOptions<OutboxWorkerOptions> options,
    ILogger<OutboxProcessor> logger) : BackgroundService
{
    private readonly OutboxWorkerOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("OutboxProcessor started. InstanceId={InstanceId}", claimManager.InstanceId);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ReleaseStaleClaimsAsync(stoppingToken);
                await ProcessBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unhandled error in OutboxProcessor cycle");
            }

            await Task.Delay(TimeSpan.FromSeconds(_options.PollingIntervalSeconds), stoppingToken);
        }

        logger.LogInformation("OutboxProcessor stopped.");
    }

    private async Task ReleaseStaleClaimsAsync(CancellationToken ct)
    {
        await claimManager.ReleaseStaleClaimsAsync(ct);
    }

    private async Task ProcessBatchAsync(CancellationToken ct)
    {
        var events = await claimManager.ClaimBatchAsync(ct);

        if (events.Count == 0) return;

        logger.LogInformation("Claimed {Count} events", events.Count);

        foreach (var evt in events)
        {
            try
            {
                await ProcessEventAsync(evt, ct);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to process event {EventId} ({EventType})", evt.Id, evt.EventType);
                await HandleRetryOrFailAsync(evt, ex.Message, ct);
            }
        }
    }

    private async Task ProcessEventAsync(OutboxEvent evt, CancellationToken ct)
    {
        var publisher = publisherFactory.GetPublisher(evt.EventType);

        if (publisher is null)
        {
            logger.LogWarning("No publisher configured for EventType={EventType}. Marking Skipped.", evt.EventType);
            await UpdateStatusAsync(evt.Id, "Skipped", publishedAt: null, clearClaim: true, ct: ct);
            return;
        }

        await publisher.PublishAsync(evt, ct);

        logger.LogInformation("Published event {EventId} ({EventType})", evt.Id, evt.EventType);
        await UpdateStatusAsync(evt.Id, "Published", publishedAt: DateTimeOffset.UtcNow, clearClaim: true, ct: ct);
    }

    private async Task HandleRetryOrFailAsync(OutboxEvent evt, string error, CancellationToken ct)
    {
        // Read current RetryCount from DB
        var retryCount = await dbContext.Database
            .SqlQueryRaw<int>(@"SELECT ""RetryCount""::int FROM ""Events"" WHERE ""Id"" = {0}", evt.Id)
            .FirstOrDefaultAsync(ct);

        if (retryCount >= _options.MaxRetries)
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                @"UPDATE ""Events"" SET ""Status"" = 'Failed', ""LastError"" = {0},
                  ""ClaimedBy"" = NULL, ""ClaimedAt"" = NULL
                  WHERE ""Id"" = {1}",
                error, evt.Id, ct);
        }
        else
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                @"UPDATE ""Events"" SET ""RetryCount"" = ""RetryCount"" + 1, ""LastError"" = {0},
                  ""ClaimedBy"" = NULL, ""ClaimedAt"" = NULL
                  WHERE ""Id"" = {1}",
                error, evt.Id, ct);
        }
    }

    private async Task UpdateStatusAsync(
        Guid eventId, string status, DateTimeOffset? publishedAt, bool clearClaim, CancellationToken ct)
    {
        if (publishedAt.HasValue)
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                @"UPDATE ""Events"" SET ""Status"" = {0}, ""PublishedAt"" = {1},
                  ""ClaimedBy"" = NULL, ""ClaimedAt"" = NULL WHERE ""Id"" = {2}",
                status, publishedAt.Value, eventId, ct);
        }
        else
        {
            await dbContext.Database.ExecuteSqlRawAsync(
                @"UPDATE ""Events"" SET ""Status"" = {0},
                  ""ClaimedBy"" = NULL, ""ClaimedAt"" = NULL WHERE ""Id"" = {1}",
                status, eventId, ct);
        }
    }
}
