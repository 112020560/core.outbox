using Microsoft.EntityFrameworkCore;
using SmartCore.Outbox.Infrastructure;
using SmartCore.Outbox.Models;
using Testcontainers.PostgreSql;

namespace SmartCore.Outbox.IntegrationTests;

public class OutboxIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private OutboxDbContext _dbContext = null!;
    private OutboxRepository _writer = null!;
    private IdempotencyRepository _idempotency = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var options = new DbContextOptionsBuilder<OutboxDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        _dbContext = new OutboxDbContext(options);
        await _dbContext.Database.MigrateAsync();

        _writer = new OutboxRepository(_dbContext);
        _idempotency = new IdempotencyRepository(_dbContext);
    }

    public async Task DisposeAsync()
    {
        await _dbContext.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    // 8.2 — AppendAsync inserts row with Status = 'Pending' and correct field values
    [Fact]
    public async Task AppendAsync_inserts_pending_event_with_correct_fields()
    {
        var evt = new OutboxEvent
        {
            ServiceName = "crm",
            DeduplicationKey = "CustomerCreated:1",
            AggregateId = Guid.NewGuid(),
            AggregateType = "Customer",
            EventType = "CustomerCreated",
            Payload = """{"customerId":"abc"}"""
        };

        await _writer.AppendAsync(evt);

        var row = await _dbContext.Database
            .SqlQueryRaw<EventRow>(
                """SELECT "Id", "ServiceName", "EventType", "Status" FROM "Events" WHERE "Id" = {0}""",
                evt.Id)
            .FirstOrDefaultAsync();

        Assert.NotNull(row);
        Assert.Equal("crm", row.ServiceName);
        Assert.Equal("CustomerCreated", row.EventType);
        Assert.Equal("Pending", row.Status);
    }

    // 8.3 — Second AppendAsync with same DeduplicationKey does not insert duplicate
    [Fact]
    public async Task AppendAsync_with_duplicate_DeduplicationKey_does_not_throw_or_duplicate()
    {
        var key = $"CustomerCreated:{Guid.NewGuid()}";

        var evt1 = new OutboxEvent
        {
            ServiceName = "crm", DeduplicationKey = key,
            AggregateId = Guid.NewGuid(), AggregateType = "Customer",
            EventType = "CustomerCreated", Payload = "{}"
        };
        var evt2 = evt1 with { Id = Guid.NewGuid() };

        await _writer.AppendAsync(evt1);
        var ex = await Record.ExceptionAsync(() => _writer.AppendAsync(evt2));
        Assert.Null(ex);

        var count = await _dbContext.Database
            .SqlQueryRaw<int>("""SELECT COUNT(*)::int FROM "Events" WHERE "DeduplicationKey" = {0}""", key)
            .FirstAsync();

        Assert.Equal(1, count);
    }

    // 8.4 — TryAppendAsync returns true on success, false when connection is broken
    [Fact]
    public async Task TryAppendAsync_returns_true_on_success()
    {
        var evt = new OutboxEvent
        {
            ServiceName = "crm", DeduplicationKey = $"Test:{Guid.NewGuid()}",
            AggregateId = Guid.NewGuid(), AggregateType = "X",
            EventType = "TestEvent", Payload = "{}"
        };

        var result = await _writer.TryAppendAsync(evt);
        Assert.True(result);
    }

    [Fact]
    public async Task TryAppendAsync_returns_false_when_dbcontext_is_disposed()
    {
        var options = new DbContextOptionsBuilder<OutboxDbContext>()
            .UseNpgsql("Host=localhost;Port=1;Database=x")
            .Options;

        await using var brokenCtx = new OutboxDbContext(options);
        var brokenWriter = new OutboxRepository(brokenCtx);

        var result = await brokenWriter.TryAppendAsync(new OutboxEvent
        {
            ServiceName = "x", DeduplicationKey = "x:1",
            AggregateId = Guid.NewGuid(), AggregateType = "X",
            EventType = "X", Payload = "{}"
        });

        Assert.False(result);
    }

    // 8.5 — AlreadyProcessedAsync returns false before mark, true after MarkAsProcessedAsync
    [Fact]
    public async Task AlreadyProcessedAsync_returns_false_then_true_after_mark()
    {
        var eventId = Guid.NewGuid();
        const string consumer = "EmailNotificationConsumer";

        Assert.False(await _idempotency.AlreadyProcessedAsync(eventId, consumer));

        await _idempotency.MarkAsProcessedAsync(eventId, consumer);

        Assert.True(await _idempotency.AlreadyProcessedAsync(eventId, consumer));
    }

    // 8.6 — Concurrent MarkAsProcessedAsync with same key produces exactly one row
    [Fact]
    public async Task MarkAsProcessedAsync_concurrent_calls_produce_single_row()
    {
        var eventId = Guid.NewGuid();
        const string consumer = "ConcurrentConsumer";

        var tasks = Enumerable.Range(0, 5).Select(_ =>
            _idempotency.MarkAsProcessedAsync(eventId, consumer));

        await Task.WhenAll(tasks);

        var count = await _dbContext.Database
            .SqlQueryRaw<int>(
                """SELECT COUNT(*)::int FROM "ProcessedEvents" WHERE "EventId" = {0} AND "ConsumerName" = {1}""",
                eventId, consumer)
            .FirstAsync();

        Assert.Equal(1, count);
    }

    private record EventRow(Guid Id, string ServiceName, string EventType, string Status);
}
