using Microsoft.EntityFrameworkCore;
using SmartCore.Outbox.Abstractions;
using SmartCore.Outbox.Models;

namespace SmartCore.Outbox.Infrastructure;

internal sealed class OutboxRepository(OutboxDbContext dbContext) : IOutboxWriter
{
    public async Task AppendAsync(OutboxEvent outboxEvent, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO "Events" (
                "Id", "ServiceName", "DeduplicationKey", "AggregateId", "AggregateType",
                "EventType", "Payload", "OccurredAt", "Status", "RetryCount"
            ) VALUES (
                {0}, {1}, {2}, {3}, {4}, {5}, {6}::jsonb, {7}, 'Pending', 0
            )
            ON CONFLICT ("DeduplicationKey") DO NOTHING
            """;

        await dbContext.Database.ExecuteSqlRawAsync(sql,
            new object[]
            {
                outboxEvent.Id,
                outboxEvent.ServiceName,
                outboxEvent.DeduplicationKey,
                outboxEvent.AggregateId,
                outboxEvent.AggregateType,
                outboxEvent.EventType,
                outboxEvent.Payload,
                outboxEvent.OccurredAt
            },
            ct);
    }

    public async Task<bool> TryAppendAsync(OutboxEvent outboxEvent, CancellationToken ct = default)
    {
        try
        {
            await AppendAsync(outboxEvent, ct);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
