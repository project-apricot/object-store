namespace ApricotFramework.ObjectStore.Model;

/// <summary>
/// How an object is read.
/// </summary>
public sealed record ObjectGetOptions
{
    /// <summary>
    /// Gets the first byte to read, or null to start at the beginning.
    /// </summary>
    public long? RangeStart { get; init; }

    /// <summary>
    /// Gets the last byte to read, inclusive, or null to read to the end.
    /// </summary>
    public long? RangeEnd { get; init; }
}
