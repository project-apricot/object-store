using System.Text;
using ApricotFramework.ObjectStore.Exceptions;

namespace ApricotFramework.ObjectStore;

/// <summary>
/// Checks that an object key is safe to hand to a store.
/// </summary>
/// <remarks>
/// A store applies its key prefix itself, so a key that could climb out of it must never get that far.
/// <c>.</c> and <c>..</c> segments are refused outright, not just for that reason: an HTTP client
/// normalizes them away, so a presigned URL for <c>a/../b</c> would point at a different object than
/// the one that was signed.
/// </remarks>
public static class ObjectKey
{
    /// <summary>
    /// The longest key, in UTF-8 bytes, that S3 and every S3-compatible server accepts.
    /// </summary>
    public const int MaximumBytes = 1024;

    /// <summary>
    /// Explains why a key is unusable.
    /// </summary>
    /// <param name="key">The key to check.</param>
    /// <param name="reservedBytes">
    /// Bytes of the limit already spoken for, such as a store's key prefix, which is sent along with the key.
    /// </param>
    /// <returns>The reason, or null when the key is fine.</returns>
    public static string? Explain(string? key, int reservedBytes = 0)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return "An object key cannot be empty.";
        }

        if (key[0] == '/')
        {
            return "An object key cannot start with '/'.";
        }

        foreach (var character in key)
        {
            if (char.IsControl(character))
            {
                return "An object key cannot contain control characters.";
            }
        }

        if (Encoding.UTF8.GetByteCount(key) > MaximumBytes - reservedBytes)
        {
            return reservedBytes == 0
                ? $"An object key cannot be longer than {MaximumBytes} bytes in UTF-8."
                : $"An object key cannot be longer than {MaximumBytes - reservedBytes} bytes in UTF-8 in a store whose prefix takes {reservedBytes} of the {MaximumBytes}.";
        }

        foreach (var segment in key.Split('/'))
        {
            if (segment is "." or "..")
            {
                return "An object key cannot contain '.' or '..' segments.";
            }
        }

        return null;
    }

    /// <summary>
    /// Explains why a listing prefix is unusable.
    /// </summary>
    /// <param name="prefix">The prefix to check, where null or empty means everything.</param>
    /// <returns>The reason, or null when the prefix is fine.</returns>
    /// <remarks>
    /// Looser than a key: a prefix may be empty and may end with <c>/</c>.
    /// </remarks>
    public static string? ExplainPrefix(string? prefix)
    {
        if (string.IsNullOrEmpty(prefix))
        {
            return null;
        }

        if (prefix[0] == '/')
        {
            return "A prefix cannot start with '/'.";
        }

        var trimmed = prefix.TrimEnd('/');

        // a prefix of only slashes is caught above, so what is left to check is never empty
        return Explain(trimmed);
    }

    /// <summary>
    /// Throws when a key is unusable.
    /// </summary>
    /// <param name="key">The key to check.</param>
    /// <param name="storeName">The store the key is for, to name in the exception.</param>
    /// <param name="provider">The provider of that store, to name in the exception.</param>
    /// <param name="reservedBytes">
    /// Bytes of the limit already spoken for, such as a store's key prefix, which is sent along with the key.
    /// </param>
    /// <exception cref="ObjectStoreException">
    /// Thrown with <see cref="ObjectStoreErrorCode.InvalidKey"/> when the key is not usable.
    /// </exception>
    public static void Validate(string? key, string? storeName = null, string? provider = null, int reservedBytes = 0)
    {
        var problem = Explain(key, reservedBytes);

        if (problem is not null)
        {
            throw new ObjectStoreException(ObjectStoreErrorCode.InvalidKey, problem, storeName, provider, key);
        }
    }

    /// <summary>
    /// Throws when a listing prefix is unusable.
    /// </summary>
    /// <param name="prefix">The prefix to check.</param>
    /// <param name="storeName">The store the prefix is for, to name in the exception.</param>
    /// <param name="provider">The provider of that store, to name in the exception.</param>
    /// <exception cref="ObjectStoreException">
    /// Thrown with <see cref="ObjectStoreErrorCode.InvalidKey"/> when the prefix is not usable.
    /// </exception>
    public static void ValidatePrefix(string? prefix, string? storeName = null, string? provider = null)
    {
        var problem = ExplainPrefix(prefix);

        if (problem is not null)
        {
            throw new ObjectStoreException(ObjectStoreErrorCode.InvalidKey, problem, storeName, provider, prefix);
        }
    }
}
