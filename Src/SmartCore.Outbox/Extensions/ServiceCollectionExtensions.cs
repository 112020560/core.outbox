using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SmartCore.Outbox.Abstractions;
using SmartCore.Outbox.Infrastructure;

namespace SmartCore.Outbox.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSmartOutbox(
        this IServiceCollection services,
        Action<OutboxOptions> configure)
    {
        var options = new OutboxOptions();
        configure(options);

        services.AddDbContext<OutboxDbContext>(db =>
            db.UseNpgsql(options.ConnectionString));

        services.AddScoped<IOutboxWriter, OutboxRepository>();
        services.AddScoped<IIdempotencyGuard, IdempotencyRepository>();

        services.AddSingleton(options);
        services.AddHostedService<OutboxMigrationHostedService>();

        return services;
    }
}
