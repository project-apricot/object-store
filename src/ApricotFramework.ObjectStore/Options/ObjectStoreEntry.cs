using ApricotFramework.ObjectStore.Exceptions;

namespace ApricotFramework.ObjectStore.Options;

/// <summary>
/// One store as it is declared in configuration.
/// </summary>
/// <remarks>
/// Shaped for the configuration binder — mutable, everything optional — and mapped onto the immutable
/// <see cref="ObjectStoreDefinition"/> by <see cref="ToDefinition"/>. Kept separate from it so the
/// domain type is not held to what a binder can populate.
/// </remarks>
public class ObjectStoreEntry
{
    /// <summary>
    /// Gets or sets the name of the provider that backs the store, such as <c>s3</c>.
    /// </summary>
    public string? Provider { get; set; }

    /// <summary>
    /// Gets the settings the provider reads, such as a bucket and credentials.
    /// </summary>
    /// <remarks>
    /// Get-only, which is the shape the configuration binder populates. Values are read as text
    /// whatever they look like in the file, so a JSON number arrives as <c>"5"</c>. The section must be
    /// flat: the binder drops a nested object under it without complaint. Credentials belong in the
    /// environment or a secrets manager, not in the file.
    /// </remarks>
    public IDictionary<string, string?> Settings { get; } = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Maps this entry onto a definition.
    /// </summary>
    /// <param name="name">The name the entry is keyed by.</param>
    /// <returns>The definition.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="name"/> is null or blank.</exception>
    /// <exception cref="ObjectStoreConfigurationException">Thrown when the entry names no provider.</exception>
    public virtual ObjectStoreDefinition ToDefinition(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (string.IsNullOrWhiteSpace(this.Provider))
        {
            throw new ObjectStoreConfigurationException($"Object store '{name}' does not name a provider.");
        }

        return new ObjectStoreDefinition
        {
            Name = name,
            Provider = this.Provider.Trim(),
            Settings = new ObjectStoreSettings(new Dictionary<string, string?>(this.Settings, StringComparer.OrdinalIgnoreCase), name),
        };
    }
}
