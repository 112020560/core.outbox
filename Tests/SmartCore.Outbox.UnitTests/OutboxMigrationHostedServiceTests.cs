using SmartCore.Outbox.Extensions;

namespace SmartCore.Outbox.UnitTests;

public class OutboxMigrationHostedServiceTests
{
    [Fact]
    public void Options_with_empty_ConnectionString_is_invalid()
    {
        var options = new OutboxOptions { ConnectionString = "", ServiceName = "crm" };
        Assert.True(string.IsNullOrWhiteSpace(options.ConnectionString));
    }

    [Fact]
    public void Options_with_empty_ServiceName_is_invalid()
    {
        var options = new OutboxOptions { ConnectionString = "Host=localhost", ServiceName = "" };
        Assert.True(string.IsNullOrWhiteSpace(options.ServiceName));
    }

    [Fact]
    public void Options_with_both_values_set_is_valid()
    {
        var options = new OutboxOptions
        {
            ConnectionString = "Host=localhost;Database=outbox_db",
            ServiceName = "crm"
        };
        Assert.False(string.IsNullOrWhiteSpace(options.ConnectionString));
        Assert.False(string.IsNullOrWhiteSpace(options.ServiceName));
    }
}
