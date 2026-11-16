using ApricotFramework.ObjectStore.Exceptions;

namespace ApricotFramework.ObjectStore.Model;

/// <summary>
/// One key a batch delete could not remove.
/// </summary>
/// <param name="Key">The key.</param>
/// <param name="ErrorCode">Why it failed.</param>
/// <param name="Message">The store's description of the failure, for a log.</param>
public sealed record ObjectDeleteFailure(string Key, ObjectStoreErrorCode ErrorCode, string? Message);
