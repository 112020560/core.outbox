using SmartCore.Outbox.Models;

namespace SmartCore.Outbox.UnitTests;

public class OutboxEventTests
{
    [Fact]
    public void Id_defaults_to_non_empty_guid()
    {
        var evt = new OutboxEvent();
        Assert.NotEqual(Guid.Empty, evt.Id);
    }

    [Fact]
    public void OccurredAt_defaults_to_utc_now()
    {
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        var evt = new OutboxEvent();
        var after = DateTimeOffset.UtcNow.AddSeconds(1);

        Assert.True(evt.OccurredAt >= before && evt.OccurredAt <= after);
    }

    [Fact]
    public void Two_events_created_consecutively_have_different_ids()
    {
        var evt1 = new OutboxEvent();
        var evt2 = new OutboxEvent();
        Assert.NotEqual(evt1.Id, evt2.Id);
    }

    [Fact]
    public void Explicit_values_are_preserved()
    {
        var id = Guid.NewGuid();
        var ts = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var evt = new OutboxEvent
        {
            Id = id,
            ServiceName = "crm",
            EventType = "CustomerCreated",
            OccurredAt = ts
        };

        Assert.Equal(id, evt.Id);
        Assert.Equal("crm", evt.ServiceName);
        Assert.Equal("CustomerCreated", evt.EventType);
        Assert.Equal(ts, evt.OccurredAt);
    }
}
