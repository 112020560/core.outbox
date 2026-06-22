using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SmartCore.Outbox.Infrastructure;

namespace SmartCore.Outbox.Extensions;

internal sealed class OutboxMigrationHostedService(
    IServiceScopeFactory scopeFactory,
    OutboxOptions options) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.ConnectionString))
            throw new InvalidOperationException("SmartCore.Outbox: ConnectionString must not be empty.");

        if (string.IsNullOrWhiteSpace(options.ServiceName))
            throw new InvalidOperationException("SmartCore.Outbox: ServiceName must not be empty.");

        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OutboxDbContext>();
        await dbContext.Database.MigrateAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
