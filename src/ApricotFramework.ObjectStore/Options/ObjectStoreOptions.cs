namespace ApricotFramework.ObjectStore.Options;

/// <summary>
/// The stores a host declares, and which one to use when a caller names none.
/// </summary>
public class ObjectStoreOptions
{
    /// <summary>
    /// The store name a lookup falls back to when nothing names one.
    /// </summary>
    public const string FallbackStoreName = "default";

    /// <summary>
    /// Gets or sets the store used when a caller does not name one.
    /// </summary>
    /// <remarks>
    /// Left unset, a lookup looks for a store called <see cref="FallbackStoreName"/>.
    /// </remarks>
    public string? DefaultStore { get; set; }

    /// <summary>
    /// Gets the stores, keyed by the name callers address them with.
    /// </summary>
    /// <remarks>
    /// Get-only, which is the shape the configuration binder populates. Any setting can be overridden
    /// from the environment: <c>ObjectStore__Stores__uploads__Settings__Bucket</c>.
    /// </remarks>
    public IDictionary<string, ObjectStoreEntry> Stores { get; } = new Dictionary<string, ObjectStoreEntry>(StringComparer.OrdinalIgnoreCase);
}
