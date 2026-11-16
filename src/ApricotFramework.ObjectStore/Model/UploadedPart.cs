namespace ApricotFramework.ObjectStore.Model;

/// <summary>
/// One uploaded part of a multipart upload.
/// </summary>
/// <param name="PartNumber">The part's position, from 1.</param>
/// <param name="ETag">The ETag the store assigned the part, which completing the upload must echo back.</param>
/// <param name="Size">The part's size in bytes, or null when the store did not report it.</param>
public sealed record UploadedPart(int PartNumber, string ETag, long? Size = null);
