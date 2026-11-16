using ApricotFramework.ObjectStore.Model;

namespace ApricotFramework.ObjectStore.Impl;

/// <summary>
/// Copies an object between stores by reading it from one and writing it to the other.
/// </summary>
/// <remarks>
/// The fallback for two stores that cannot copy server-side, such as ones on different connections or
/// different providers. Every byte passes through this process.
/// </remarks>
public static class ObjectStoreCopier
{
    /// <summary>
    /// Copies an object by streaming it.
    /// </summary>
    /// <param name="source">The store to copy from.</param>
    /// <param name="sourceKey">The key to copy from.</param>
    /// <param name="destination">The store to copy into.</param>
    /// <param name="destinationKey">The key to copy to.</param>
    /// <param name="options">How to copy, or null to keep the source's content type and metadata.</param>
    /// <param name="cancellationToken">Cancels the copy.</param>
    /// <returns>What the destination now holds under the destination key.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="source"/> or <paramref name="destination"/> is null.</exception>
    public static async Task<ObjectInfo> CopyByStreamingAsync(
        IObjectStore source,
        string sourceKey,
        IObjectStore destination,
        string destinationKey,
        ObjectCopyOptions? options,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);

        var stored = await source.GetAsync(sourceKey, null, cancellationToken).ConfigureAwait(false);

        await using (stored.ConfigureAwait(false))
        {
            var put = options is { ReplacesMetadata: true } replacement
                ? new ObjectPutOptions
                {
                    ContentType = replacement.ContentType,
                    ContentDisposition = replacement.ContentDisposition,
                    CacheControl = replacement.CacheControl,
                    Metadata = replacement.Metadata,
                }
                : new ObjectPutOptions
                {
                    ContentType = stored.Info.ContentType,
                    ContentDisposition = stored.Info.ContentDisposition,
                    CacheControl = stored.Info.CacheControl,
                    Metadata = stored.Info.Metadata,
                };

            return await destination.PutAsync(destinationKey, stored.Content, put, cancellationToken).ConfigureAwait(false);
        }
    }
}
