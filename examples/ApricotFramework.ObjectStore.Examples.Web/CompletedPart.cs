namespace ApricotFramework.ObjectStore.Examples.Web;

/// <summary>
/// One part the client uploaded, as it reports it back.
/// </summary>
/// <param name="PartNumber">The part's position, from 1.</param>
/// <param name="ETag">The ETag the store answered the part's upload with.</param>
internal sealed record CompletedPart(int PartNumber, string ETag);
