using SmartCore.Outbox.Abstractions;
using SmartCore.Outbox.Models;

namespace SmartCore.Outbox.UnitTests;

public class OutboxWriterTests
{
    [Fact]
    public async Task TryAppendAsync_returns_true_on_success()
    {
        var writer = new FakeOutboxWriter(throws: false);
        var result = await writer.TryAppendAsync(new OutboxEvent { EventType = "Test" });
        Assert.True(result);
    }

    [Fact]
    public async Task TryAppendAsync_returns_false_on_exception()
    {
        var writer = new FakeOutboxWriter(throws: true);
        var result = await writer.TryAppendAsync(new OutboxEvent { EventType = "Test" });
        Assert.False(result);
    }

    private sealed class FakeOutboxWriter(bool throws) : IOutboxWriter
    {
        public Task AppendAsync(OutboxEvent outboxEvent, CancellationToken ct = default)
        {
            if (throws) throw new InvalidOperationException("DB unavailable");
            return Task.CompletedTask;
        }

        public async Task<bool> TryAppendAsync(OutboxEvent outboxEvent, CancellationToken ct = default)
        {
            try
            {
                await AppendAsync(outboxEvent, ct);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
