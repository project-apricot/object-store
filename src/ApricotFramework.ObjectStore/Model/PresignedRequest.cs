namespace ApricotFramework.ObjectStore.Model;

/// <summary>
/// A URL that lets a holder perform one operation on one object without credentials of their own.
/// </summary>
/// <remarks>
/// For a log, not a response body, unless it is going to the person it was made for: the URL is a bearer
/// credential until it expires, so <see cref="ToString"/> does not print it.
/// </remarks>
public sealed record PresignedRequest
{
    /// <summary>
    /// Gets the URL to call.
    /// </summary>
    public required Uri Url { get; init; }

    /// <summary>
    /// Gets the HTTP method the URL was signed for.
    /// </summary>
    public required string Method { get; init; }

    /// <summary>
    /// Gets the headers the caller must send exactly as given, because they were part of what was
    /// signed. Empty when there are none.
    /// </summary>
    public IReadOnlyDictionary<string, string> RequiredHeaders { get; init; } = ObjectInfo.EmptyMetadata;

    /// <summary>
    /// Gets when the URL stops working.
    /// </summary>
    public required DateTimeOffset ExpiresAt { get; init; }

    /// <inheritdoc />
    public override string ToString()
    {
        return $"Presigned {this.Method} request, expires {this.ExpiresAt:O}.";
    }
}
