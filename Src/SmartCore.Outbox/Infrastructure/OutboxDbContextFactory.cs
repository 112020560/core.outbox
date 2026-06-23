using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SmartCore.Outbox.Infrastructure;

// Used only by dotnet-ef CLI for migration generation. Not registered in DI.
public class OutboxDbContextFactory : IDesignTimeDbContextFactory<OutboxDbContext>
{
    public OutboxDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<OutboxDbContext>()
            .UseNpgsql("Host=interchange.proxy.rlwy.net;Database=outbox_db;Username=postgres;Password=VIwCMnzKlshSsqCuFgcpzbkpXXqllyFu;Port=30299")
            .Options;

        return new OutboxDbContext(options);
    }
}
