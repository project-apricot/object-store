using System.Diagnostics.CodeAnalysis;
using ApricotFramework.ObjectStore.Exceptions;
using ApricotFramework.ObjectStore.Options;

namespace ApricotFramework.ObjectStore.Impl;

/// <summary>
/// Finds stores by name, and keeps the ones it has created.
/// </summary>
/// <remarks>
/// Finds a store's definition — registered sources first, then the options — picks its provider, creates
/// the store, and keeps it, so the vendor client behind it is built once and shared however many callers ask.
/// Settings are read as the factory was given them: a change needs a new factory, which is to say a restart.
/// <para>
/// Usable without a container, so a console or worker host can new one up. Dispose it when done: it owns the
/// stores it created.
/// </para>
/// </remarks>
/// <param name="providers">The providers available to create stores with.</param>
/// <param name="options">The options, or null for none.</param>
/// <param name="sources">The sources to ask before the options, or null for none.</param>
/// <exception cref="ArgumentNullException">Thrown when <paramref name="providers"/> is null.</exception>
/// <exception cref="ArgumentException">Thrown when two providers answer to the same name.</exception>
public class DefaultObjectStoreFactory(IEnumerable<IObjectStoreProvider> providers, ObjectStoreOptions? options = null, IEnumerable<IObjectStoreDefinitionSource>? sources = null) : IObjectStoreFactory, IDisposable
{
    /// <summary>
    /// Guards the cache and disposal.
    /// </summary>
    private readonly Lock gate = new();

    /// <summary>
    /// The providers stores can be created with.
    /// </summary>
    private readonly ObjectStoreProviderRegistry registry = new(providers);

    /// <summary>
    /// The stores declared in the options.
    /// </summary>
    private readonly ObjectStoreOptions declared = options ?? new ObjectStoreOptions();

    /// <summary>
    /// The sources to ask before the options, in the order they are asked.
    /// </summary>
    private readonly IReadOnlyList<IObjectStoreDefinitionSource> definitionSources = [.. sources ?? []];

    /// <summary>
    /// The stores created so far, by the name they were asked for.
    /// </summary>
    private readonly Dictionary<string, IObjectStore> cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Whether the factory has been disposed.
    /// </summary>
    private bool disposed;

    /// <inheritdoc />
    public virtual IObjectStore GetStore(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (this.TryGetStore(name, out var store))
        {
            return store;
        }

        throw new ObjectStoreException(
            ObjectStoreErrorCode.StoreNotFound,
            $"No object store named '{name}' was found. Declare it in configuration or supply it from a definition source.",
            name);
    }

    /// <inheritdoc />
    public virtual IObjectStore GetDefaultStore()
    {
        var configured = this.declared.DefaultStore;

        return this.GetStore(string.IsNullOrWhiteSpace(configured) ? ObjectStoreOptions.FallbackStoreName : configured.Trim());
    }

    /// <inheritdoc />
    public virtual bool TryGetStore(string name, [NotNullWhen(true)] out IObjectStore? store)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        lock (this.gate)
        {
            ObjectDisposedException.ThrowIf(this.disposed, this);

            if (this.cache.TryGetValue(name, out store))
            {
                return true;
            }

            var definition = this.FindDefinition(name);

            if (definition is null)
            {
                return false;
            }

            store = this.CreateStore(definition);
            this.cache[name] = store;

            return true;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        this.Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Finds a store's definition, asking sources before the options.
    /// </summary>
    /// <param name="name">The store name.</param>
    /// <returns>The definition, or null when nothing has it.</returns>
    /// <exception cref="ObjectStoreConfigurationException">Thrown when the entry is declared but unusable.</exception>
    protected virtual ObjectStoreDefinition? FindDefinition(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        foreach (var source in this.definitionSources)
        {
            var definition = source.Find(name);

            if (definition is not null)
            {
                return definition;
            }
        }

        foreach (var (declaredName, entry) in this.declared.Stores)
        {
            // the configured spelling is carried through, not the caller's: a store is named once and
            // that is the name that should reach a log
            if (string.Equals(declaredName, name, StringComparison.OrdinalIgnoreCase))
            {
                return entry.ToDefinition(declaredName);
            }
        }

        return null;
    }

    /// <summary>
    /// Creates a store from a definition, using the provider it names.
    /// </summary>
    /// <param name="definition">The definition.</param>
    /// <returns>The store.</returns>
    /// <exception cref="ObjectStoreConfigurationException">Thrown when no registered provider has that name.</exception>
    protected virtual IObjectStore CreateStore(ObjectStoreDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        var provider = this.registry.Find(definition.Provider);

        if (provider is null)
        {
            var registered = this.registry.GetNames();

            throw new ObjectStoreConfigurationException(
                registered.Count == 0
                    ? $"Object store '{definition.Name}' uses provider '{definition.Provider}', and no provider is registered at all."
                    : $"Object store '{definition.Name}' uses provider '{definition.Provider}', which is not registered. Registered providers: {string.Join(", ", registered)}.");
        }

        return provider.Create(definition);
    }

    /// <summary>
    /// Releases the stores this factory created.
    /// </summary>
    /// <param name="disposing">True when called from <see cref="Dispose()"/>.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!disposing)
        {
            return;
        }

        lock (this.gate)
        {
            if (this.disposed)
            {
                return;
            }

            this.disposed = true;

            foreach (var store in this.cache.Values)
            {
                (store as IDisposable)?.Dispose();
            }

            this.cache.Clear();
        }
    }
}
