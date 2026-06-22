using Microsoft.EntityFrameworkCore;
using SmartCore.Outbox.Abstractions;

namespace SmartCore.Outbox.Infrastructure;

internal sealed class IdempotencyRepository(OutboxDbContext dbContext) : IIdempotencyGuard
{
    public async Task<bool> AlreadyProcessedAsync(Guid eventId, string consumerName, CancellationToken ct = default)
    {
        return await dbContext.ProcessedEvents
            .AnyAsync(p => p.EventId == eventId && p.ConsumerName == consumerName, ct);
    }

    public async Task MarkAsProcessedAsync(Guid eventId, string consumerName, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO "ProcessedEvents" ("EventId", "ConsumerName", "ProcessedAt")
            VALUES ({0}, {1}, {2})
            ON CONFLICT ("EventId", "ConsumerName") DO NOTHING
            """;

        await dbContext.Database.ExecuteSqlRawAsync(sql, eventId, consumerName, DateTimeOffset.UtcNow, ct);
    }
}
