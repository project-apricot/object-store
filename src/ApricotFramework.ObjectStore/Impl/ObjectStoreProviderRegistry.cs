namespace ApricotFramework.ObjectStore.Impl;

/// <summary>
/// The providers available to create stores with, addressed by name.
/// </summary>
/// <remarks>
/// There are no built-in providers: a host registers the ones it wants, so a store declared against an
/// unregistered provider fails loudly instead of being swallowed by a default that goes nowhere.
/// </remarks>
public class ObjectStoreProviderRegistry
{
    /// <summary>
    /// Gets the providers, keyed by the name they answer to.
    /// </summary>
    protected Dictionary<string, IObjectStoreProvider> Providers { get; }

    /// <summary>
    /// Creates a new instance of the registry.
    /// </summary>
    /// <param name="providers">Every registered provider.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="providers"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when a provider has a blank name, or two answer to the same one.
    /// </exception>
    public ObjectStoreProviderRegistry(IEnumerable<IObjectStoreProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);

        // names are labels, so they are matched the way configuration keys are
        this.Providers = new Dictionary<string, IObjectStoreProvider>(StringComparer.OrdinalIgnoreCase);

        foreach (var provider in providers)
        {
            ArgumentNullException.ThrowIfNull(provider, nameof(providers));

            if (string.IsNullOrWhiteSpace(provider.Name))
            {
                throw new ArgumentException($"The object store provider of type '{provider.GetType().Name}' has a blank name.", nameof(providers));
            }

            // silently keeping the first would make which one builds a store depend on registration order
            if (!this.Providers.TryAdd(provider.Name, provider))
            {
                throw new ArgumentException($"Two object store providers are named '{provider.Name}'.", nameof(providers));
            }
        }
    }

    /// <summary>
    /// Finds the provider a name refers to.
    /// </summary>
    /// <param name="name">The provider name.</param>
    /// <returns>The provider, or null when none answers to that name.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="name"/> is null.</exception>
    public virtual IObjectStoreProvider? Find(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return this.Providers.GetValueOrDefault(name);
    }

    /// <summary>
    /// Gets the names that stores can be created with.
    /// </summary>
    /// <returns>The registered provider names.</returns>
    public virtual IReadOnlyCollection<string> GetNames()
    {
        return this.Providers.Keys;
    }
}
