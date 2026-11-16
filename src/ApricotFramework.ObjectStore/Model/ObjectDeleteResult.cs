namespace ApricotFramework.ObjectStore.Model;

/// <summary>
/// What came of deleting several objects at once.
/// </summary>
/// <remarks>
/// A batch can half succeed, so failures are reported per key rather than thrown. A key that did not
/// exist counts as deleted.
/// </remarks>
public sealed record ObjectDeleteResult
{
    /// <summary>
    /// Gets the keys that are gone.
    /// </summary>
    public IReadOnlyList<string> Deleted { get; init; } = [];

    /// <summary>
    /// Gets the keys that could not be deleted, and why.
    /// </summary>
    public IReadOnlyList<ObjectDeleteFailure> Failures { get; init; } = [];

    /// <summary>
    /// Gets whether every key was deleted.
    /// </summary>
    public bool Succeeded => this.Failures.Count == 0;
}
