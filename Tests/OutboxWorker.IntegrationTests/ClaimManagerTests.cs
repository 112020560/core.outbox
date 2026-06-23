using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OutboxWorker;
using OutboxWorker.Configuration;
using SmartCore.Outbox.Infrastructure;
using SmartCore.Outbox.Models;
using Testcontainers.PostgreSql;

namespace OutboxWorker.IntegrationTests;

public class ClaimManagerTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private OutboxDbContext _db = null!;
    private ClaimManager _claimManager = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var dbOptions = new DbContextOptionsBuilder<OutboxDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

        _db = new OutboxDbContext(dbOptions);
        await _db.Database.MigrateAsync();

        var workerOptions = Options.Create(new OutboxWorkerOptions
        {
            ConnectionString = _postgres.GetConnectionString(),
            BatchSize = 10,
            ClaimTimeoutSeconds = 60
        });

        _claimManager = new ClaimManager(_db, workerOptions);
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    private async Task<Guid> InsertPendingEventAsync(string eventType = "TestEvent")
    {
        var id = Guid.NewGuid();
        await _db.Database.ExecuteSqlRawAsync(
            @"INSERT INTO ""Events"" (""Id"",""ServiceName"",""DeduplicationKey"",""AggregateId"",
              ""AggregateType"",""EventType"",""Payload"",""OccurredAt"",""Status"",""RetryCount"")
              VALUES ({0},'test',{1},{2},'T',{3},'{{}}',NOW(),'Pending',0)",
            id, $"key:{id}", Guid.NewGuid(), eventType);
        return id;
    }

    // 7.2 — ClaimBatchAsync returns up to BatchSize pending events and marks with instance ID
    [Fact]
    public async Task ClaimBatchAsync_claims_pending_events_with_instance_id()
    {
        await InsertPendingEventAsync();
        await InsertPendingEventAsync();

        var claimed = await _claimManager.ClaimBatchAsync();

        Assert.NotEmpty(claimed);
        Assert.All(claimed, e => Assert.Equal("Pending", GetStatus(e.Id)));

        var instanceId = await _db.Database
            .SqlQueryRaw<string>(@"SELECT ""ClaimedBy"" AS ""Value"" FROM ""Events"" WHERE ""Id"" = {0}", claimed[0].Id)
            .FirstAsync();

        Assert.Equal(_claimManager.InstanceId, instanceId);
    }

    [Fact]
    public async Task ClaimBatchAsync_respects_batch_size()
    {
        for (int i = 0; i < 15; i++) await InsertPendingEventAsync();

        var claimed = await _claimManager.ClaimBatchAsync();
        Assert.Equal(10, claimed.Count); // BatchSize = 10
    }

    // 7.3 — ReleaseStaleClaimsAsync releases expired claims, leaves fresh ones
    [Fact]
    public async Task ReleaseStaleClaimsAsync_releases_expired_and_leaves_fresh()
    {
        var staleId = await InsertPendingEventAsync();
        var freshId = await InsertPendingEventAsync();

        // Mark staleId as claimed 90 seconds ago (ClaimTimeout = 60s)
        await _db.Database.ExecuteSqlRawAsync(
            @"UPDATE ""Events"" SET ""ClaimedBy"" = 'old-worker', ""ClaimedAt"" = NOW() - INTERVAL '90 seconds'
              WHERE ""Id"" = {0}", staleId);

        // Mark freshId as claimed 10 seconds ago
        await _db.Database.ExecuteSqlRawAsync(
            @"UPDATE ""Events"" SET ""ClaimedBy"" = 'active-worker', ""ClaimedAt"" = NOW() - INTERVAL '10 seconds'
              WHERE ""Id"" = {0}", freshId);

        await _claimManager.ReleaseStaleClaimsAsync();

        var staleClaimedBy = await _db.Database
            .SqlQueryRaw<string?>(@"SELECT ""ClaimedBy"" AS ""Value"" FROM ""Events"" WHERE ""Id"" = {0}", staleId)
            .FirstOrDefaultAsync();

        var freshClaimedBy = await _db.Database
            .SqlQueryRaw<string>(@"SELECT ""ClaimedBy"" AS ""Value"" FROM ""Events"" WHERE ""Id"" = {0}", freshId)
            .FirstAsync();

        Assert.Null(staleClaimedBy);
        Assert.Equal("active-worker", freshClaimedBy);
    }

    private string GetStatus(Guid id) =>
        _db.Database.SqlQueryRaw<string>(@"SELECT ""Status"" AS ""Value"" FROM ""Events"" WHERE ""Id"" = {0}", id)
            .AsEnumerable().First();
}
