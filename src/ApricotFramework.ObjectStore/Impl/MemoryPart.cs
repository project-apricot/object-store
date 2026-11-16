namespace ApricotFramework.ObjectStore.Impl;

/// <summary>
/// One part of a multipart upload held by <see cref="MemoryObjectStore"/>.
/// </summary>
/// <param name="Data">The part's content.</param>
/// <param name="ETag">The ETag assigned to the part.</param>
internal sealed record MemoryPart(byte[] Data, string ETag);
