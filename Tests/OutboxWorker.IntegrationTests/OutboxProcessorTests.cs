using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OutboxWorker;
using OutboxWorker.Configuration;
using OutboxWorker.Publishers;
using SmartCore.Outbox.Infrastructure;
using SmartCore.Outbox.Models;
using Testcontainers.PostgreSql;

namespace OutboxWorker.IntegrationTests;

public class OutboxProcessorTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private OutboxDbContext _db = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var options = new DbContextOptionsBuilder<OutboxDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        _db = new OutboxDbContext(options);
        await _db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    private async Task<Guid> InsertPendingAsync(string eventType)
    {
        var id = Guid.NewGuid();
        await _db.Database.ExecuteSqlRawAsync(
            @"INSERT INTO ""Events"" (""Id"",""ServiceName"",""DeduplicationKey"",""AggregateId"",
              ""AggregateType"",""EventType"",""Payload"",""OccurredAt"",""Status"",""RetryCount"")
              VALUES ({0},'test',{1},{2},'T',{3},'{}',NOW(),'Pending',0)",
            id, $"key:{id}", Guid.NewGuid(), eventType);
        return id;
    }

    private OutboxProcessor BuildProcessor(IPublisherFactory factory, int maxRetries = 3, int pollingInterval = 1)
    {
        var workerOptions = Options.Create(new OutboxWorkerOptions
        {
            ConnectionString = _postgres.GetConnectionString(),
            BatchSize = 50,
            ClaimTimeoutSeconds = 60,
            MaxRetries = maxRetries,
            PollingIntervalSeconds = pollingInterval
        });

        var claimManager = new ClaimManager(_db, workerOptions);
        return new OutboxProcessor(
            claimManager, factory, _db, workerOptions,
            NullLogger<OutboxProcessor>.Instance);
    }

    private string GetStatus(Guid id) =>
        _db.Database.SqlQueryRaw<string>(@"SELECT ""Status"" FROM ""Events"" WHERE ""Id"" = {0}", id)
            .AsEnumerable().First();

    // 7.4 — Full end-to-end: Pending → Published (with stub publisher)
    [Fact]
    public async Task ProcessBatch_publishes_event_and_sets_status_Published()
    {
        var eventId = await InsertPendingAsync("CustomerCreated");
        var published = new List<string>();
        var factory = new StubPublisherFactory(eventType => eventType == "CustomerCreated"
            ? new LambdaPublisher(e => { published.Add(e.EventType); return Task.CompletedTask; })
            : null);

        var processor = BuildProcessor(factory);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Run one cycle
        await processor.StartAsync(cts.Token);
        await Task.Delay(200);
        await processor.StopAsync(CancellationToken.None);

        Assert.Contains("CustomerCreated", published);
        Assert.Equal("Published", GetStatus(eventId));
    }

    // 7.5 — No route → Status = Skipped
    [Fact]
    public async Task ProcessBatch_marks_event_Skipped_when_no_publisher_found()
    {
        var eventId = await InsertPendingAsync("UnknownEvent");
        var factory = new StubPublisherFactory(_ => null);

        var processor = BuildProcessor(factory);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await processor.StartAsync(cts.Token);
        await Task.Delay(200);
        await processor.StopAsync(CancellationToken.None);

        Assert.Equal("Skipped", GetStatus(eventId));
    }

    // 7.6 — MaxRetries exceeded → Status = Failed with LastError
    [Fact]
    public async Task ProcessBatch_marks_event_Failed_after_max_retries()
    {
        var eventId = await InsertPendingAsync("FailingEvent");

        // Set RetryCount = MaxRetries already
        await _db.Database.ExecuteSqlRawAsync(
            @"UPDATE ""Events"" SET ""RetryCount"" = 3 WHERE ""Id"" = {0}", eventId);

        var factory = new StubPublisherFactory(_ =>
            new LambdaPublisher(_ => throw new Exception("broker down")));

        var processor = BuildProcessor(factory, maxRetries: 3);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await processor.StartAsync(cts.Token);
        await Task.Delay(300);
        await processor.StopAsync(CancellationToken.None);

        Assert.Equal("Failed", GetStatus(eventId));

        var lastError = await _db.Database
            .SqlQueryRaw<string>(@"SELECT ""LastError"" FROM ""Events"" WHERE ""Id"" = {0}", eventId)
            .FirstAsync();
        Assert.Contains("broker down", lastError);
    }

    // 7.7 — Per-event error isolation: one failure doesn't abort rest of batch
    [Fact]
    public async Task ProcessBatch_isolates_per_event_failures()
    {
        var goodId = await InsertPendingAsync("GoodEvent");
        var badId = await InsertPendingAsync("BadEvent");

        var published = new List<string>();
        var factory = new StubPublisherFactory(eventType => eventType switch
        {
            "GoodEvent" => new LambdaPublisher(e => { published.Add(e.Id.ToString()); return Task.CompletedTask; }),
            "BadEvent"  => new LambdaPublisher(_ => throw new Exception("bad")),
            _           => null
        });

        var processor = BuildProcessor(factory);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await processor.StartAsync(cts.Token);
        await Task.Delay(300);
        await processor.StopAsync(CancellationToken.None);

        Assert.Equal("Published", GetStatus(goodId));
        Assert.NotEqual("Published", GetStatus(badId));
    }

    // ── Stub helpers ────────────────────────────────────────────────────────

    private sealed class StubPublisherFactory(Func<string, IEventPublisher?> resolve) : IPublisherFactory
    {
        public IEventPublisher? GetPublisher(string eventType) => resolve(eventType);
    }

    private sealed class LambdaPublisher(Func<OutboxEvent, Task> action) : IEventPublisher
    {
        public Task PublishAsync(OutboxEvent outboxEvent, CancellationToken ct = default) => action(outboxEvent);
    }
}
