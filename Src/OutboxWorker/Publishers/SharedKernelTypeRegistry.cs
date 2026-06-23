using System.Reflection;

namespace OutboxWorker.Publishers;

internal sealed class SharedKernelTypeRegistry : IMessageTypeRegistry
{
    private readonly IReadOnlyDictionary<string, Type> _types;

    public SharedKernelTypeRegistry()
    {
        var assembly = Assembly.Load("SharedKernel");

        _types = assembly.GetExportedTypes()
            .Where(t => t.Namespace?.StartsWith("SharedKernel.Contracts") == true)
            .GroupBy(t => t.Name)
            .ToDictionary(g => g.Key, g => g.First());
    }

    public Type? Resolve(string eventType) =>
        _types.TryGetValue(eventType, out var type) ? type : null;
}
