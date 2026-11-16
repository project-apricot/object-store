namespace ApricotFramework.ObjectStore;

/// <summary>
/// Supplies store definitions from somewhere other than configuration.
/// </summary>
/// <remarks>
/// This is the seam for definitions held in a database, a secrets manager, or another service. It says
/// nothing about storage or how a credential is protected, all of which stay the implementation's
/// business, including decrypting anything it stores encrypted.
/// <para>
/// Synchronous, because a store is created once and then shared, so this is asked once per store and
/// not once per operation. A source that talks to the network should keep its own cache.
/// </para>
/// </remarks>
public interface IObjectStoreDefinitionSource
{
    /// <summary>
    /// Finds a definition by name.
    /// </summary>
    /// <param name="name">The store name to look for.</param>
    /// <returns>The definition, or null when this source does not have it.</returns>
    ObjectStoreDefinition? Find(string name);
}
