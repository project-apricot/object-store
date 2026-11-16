namespace ApricotFramework.ObjectStore;

/// <summary>
/// A named store to create: which provider backs it, and what that provider needs.
/// </summary>
/// <remarks>
/// One definition per bucket and key prefix. The provider is stateless and receives the definition when
/// it creates the store, so one registered provider serves any number of stores.
/// </remarks>
public sealed record ObjectStoreDefinition
{
    /// <summary>
    /// Gets the name callers address this store by.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the name of the provider that backs the store, such as <c>s3</c>.
    /// </summary>
    public required string Provider { get; init; }

    /// <summary>
    /// Gets the settings the provider reads, such as a bucket and credentials.
    /// </summary>
    public ObjectStoreSettings Settings { get; init; } = ObjectStoreSettings.Empty;
}
