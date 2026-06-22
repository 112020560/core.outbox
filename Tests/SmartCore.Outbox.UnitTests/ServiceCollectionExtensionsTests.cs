using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SmartCore.Outbox.Abstractions;
using SmartCore.Outbox.Extensions;

namespace SmartCore.Outbox.UnitTests;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddSmartOutbox_registers_IOutboxWriter()
    {
        var services = BuildServices();
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IOutboxWriter));
        Assert.NotNull(descriptor);
    }

    [Fact]
    public void AddSmartOutbox_registers_IIdempotencyGuard()
    {
        var services = BuildServices();
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IIdempotencyGuard));
        Assert.NotNull(descriptor);
    }

    [Fact]
    public void AddSmartOutbox_registers_IHostedService_for_migration()
    {
        var services = BuildServices();
        var descriptor = services.FirstOrDefault(d =>
            d.ServiceType == typeof(IHostedService) &&
            d.ImplementationType?.Name == "OutboxMigrationHostedService");
        Assert.NotNull(descriptor);
    }

    private static IServiceCollection BuildServices()
    {
        var services = new ServiceCollection();
        services.AddSmartOutbox(o =>
        {
            o.ConnectionString = "Host=localhost;Database=outbox_db";
            o.ServiceName = "test";
        });
        return services;
    }
}
