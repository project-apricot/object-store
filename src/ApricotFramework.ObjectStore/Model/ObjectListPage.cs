namespace ApricotFramework.ObjectStore.Model;

/// <summary>
/// One page of a listing.
/// </summary>
public sealed record ObjectListPage
{
    /// <summary>
    /// Gets the objects on this page, in key order.
    /// </summary>
    public IReadOnlyList<ObjectInfo> Items { get; init; } = [];

    /// <summary>
    /// Gets the token to pass to get the next page, or null when this is the last one.
    /// </summary>
    /// <remarks>
    /// Opaque, and only meaningful to the store that issued it.
    /// </remarks>
    public string? NextContinuationToken { get; init; }
}
