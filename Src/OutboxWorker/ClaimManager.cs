using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OutboxWorker.Configuration;
using SmartCore.Outbox.Infrastructure;
using SmartCore.Outbox.Models;

namespace OutboxWorker;

internal sealed class ClaimManager
{
    private readonly OutboxDbContext _db;
    private readonly OutboxWorkerOptions _options;

    public string InstanceId { get; } = $"{Environment.MachineName}:{Guid.NewGuid()}";

    public ClaimManager(OutboxDbContext db, IOptions<OutboxWorkerOptions> options)
    {
        _db = db;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<OutboxEvent>> ClaimBatchAsync(CancellationToken ct = default)
    {
        // timeout is a safe integer from config — embedded directly; instanceId and batchSize are parameterized
        var sql = @$"
            UPDATE ""Events""
            SET ""ClaimedBy"" = {{0}}, ""ClaimedAt"" = NOW() AT TIME ZONE 'UTC'
            WHERE ""Id"" IN (
                SELECT ""Id"" FROM ""Events""
                WHERE ""Status"" = 'Pending'
                  AND (""ClaimedAt"" IS NULL OR ""ClaimedAt"" < NOW() AT TIME ZONE 'UTC' - INTERVAL '{_options.ClaimTimeoutSeconds} seconds')
                ORDER BY ""OccurredAt""
                LIMIT {{1}}
                FOR UPDATE SKIP LOCKED
            )
            RETURNING ""Id"", ""ServiceName"", ""DeduplicationKey"", ""AggregateId"", ""AggregateType"",
                      ""EventType"", ""Payload"", ""OccurredAt"",
                      ""Status"", ""ClaimedBy"", ""ClaimedAt"", ""PublishedAt"", ""RetryCount"", ""LastError""";

        return await _db.Events
            .FromSqlRaw(sql, InstanceId, _options.BatchSize)
            .AsNoTracking()
            .ToListAsync(ct);
    }

    public async Task ReleaseStaleClaimsAsync(CancellationToken ct = default)
    {
        var sql = @$"
            UPDATE ""Events""
            SET ""ClaimedBy"" = NULL, ""ClaimedAt"" = NULL
            WHERE ""Status"" = 'Pending'
              AND ""ClaimedAt"" IS NOT NULL
              AND ""ClaimedAt"" < NOW() AT TIME ZONE 'UTC' - INTERVAL '{_options.ClaimTimeoutSeconds} seconds'";

        await _db.Database.ExecuteSqlRawAsync(sql, Array.Empty<object>(), ct);
    }
}
