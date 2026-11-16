using System.Diagnostics.CodeAnalysis;

namespace ApricotFramework.ObjectStore;

/// <summary>
/// Finds configured stores by name.
/// </summary>
/// <remarks>
/// What application code depends on. A store is created once and shared, so asking for one is cheap
/// and the instances are safe to keep.
/// </remarks>
public interface IObjectStoreFactory
{
    /// <summary>
    /// Gets a store by name.
    /// </summary>
    /// <param name="name">The name the store is configured under.</param>
    /// <returns>The store.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> is null or blank.</exception>
    /// <exception cref="Exceptions.ObjectStoreException">
    /// Thrown with <see cref="Exceptions.ObjectStoreErrorCode.StoreNotFound"/> when nothing is configured
    /// under the name, and with <see cref="Exceptions.ObjectStoreErrorCode.Configuration"/> when the store
    /// is declared but unusable.
    /// </exception>
    IObjectStore GetStore(string name);

    /// <summary>
    /// Gets the store used when a caller names none.
    /// </summary>
    /// <returns>The store.</returns>
    /// <exception cref="Exceptions.ObjectStoreException">Thrown when there is no default store, or it is unusable.</exception>
    IObjectStore GetDefaultStore();

    /// <summary>
    /// Gets a store by name, if one is configured.
    /// </summary>
    /// <param name="name">The name the store is configured under.</param>
    /// <param name="store">The store, when found.</param>
    /// <returns>True when a store is configured under the name.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> is null or blank.</exception>
    /// <exception cref="Exceptions.ObjectStoreConfigurationException">Thrown when the store is declared but unusable.</exception>
    bool TryGetStore(string name, [NotNullWhen(true)] out IObjectStore? store);
}
