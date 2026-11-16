using ApricotFramework.ObjectStore.Exceptions;

namespace ApricotFramework.ObjectStore.S3;

/// <summary>
/// Creates stores backed by AWS S3 or an S3-compatible server.
/// </summary>
/// <remarks>
/// Stateless: everything a store needs arrives on its definition, so one registration serves any number
/// of buckets and endpoints. See <see cref="S3StoreSettings"/> for the settings it reads.
/// </remarks>
public class S3ObjectStoreProvider : IObjectStoreProvider
{
    /// <summary>
    /// The name a store sets as its provider to be backed by S3.
    /// </summary>
    public const string ProviderName = "s3";

    /// <inheritdoc />
    public string Name => ProviderName;

    /// <inheritdoc />
    public virtual IReadOnlyList<string> Validate(ObjectStoreDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        try
        {
            S3StoreSettings.From(definition.Settings);

            return [];
        }
        catch (ObjectStoreConfigurationException e)
        {
            // settings read one at a time, so this reports the first problem; fixing it reveals the next
            return [e.Message];
        }
    }

    /// <inheritdoc />
    public virtual IObjectStore Create(ObjectStoreDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        return new S3ObjectStore(definition.Name, S3StoreSettings.From(definition.Settings));
    }
}
