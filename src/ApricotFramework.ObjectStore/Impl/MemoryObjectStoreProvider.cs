using System.Collections.Concurrent;

namespace ApricotFramework.ObjectStore.Impl;

/// <summary>
/// Creates in-memory stores, for tests and for development hosts with nothing external to talk to.
/// </summary>
/// <remarks>
/// A store keeps its contents for as long as this provider lives. Register one instance and read a store
/// back with <see cref="GetStore"/> to assert on what was written. Nothing registers it for you: a store
/// that forgets everything on restart is the last thing that should appear in a host by default.
/// </remarks>
/// <param name="timeProvider">Where modification times come from, or null for the system clock.</param>
public class MemoryObjectStoreProvider(TimeProvider? timeProvider = null) : IObjectStoreProvider
{
    /// <summary>
    /// The name a store sets as its provider to be kept in memory.
    /// </summary>
    public const string ProviderName = "memory";

    /// <summary>
    /// The stores created so far, by name, so one can be read back to see what was written to it.
    /// </summary>
    private readonly ConcurrentDictionary<string, MemoryObjectStore> stores = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public string Name => ProviderName;

    /// <inheritdoc />
    public virtual IReadOnlyList<string> Validate(ObjectStoreDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        return [];
    }

    /// <inheritdoc />
    public virtual IObjectStore Create(ObjectStoreDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        return this.stores.GetOrAdd(definition.Name, name => new MemoryObjectStore(name, timeProvider));
    }

    /// <summary>
    /// Gets a store this provider has created, to inspect what a test wrote to it.
    /// </summary>
    /// <param name="name">The store name.</param>
    /// <returns>The store, or null when none has been created under that name.</returns>
    public MemoryObjectStore? GetStore(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return this.stores.GetValueOrDefault(name);
    }
}
