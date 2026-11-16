namespace ApricotFramework.ObjectStore.Model;

/// <summary>
/// A multipart upload in progress.
/// </summary>
/// <remarks>
/// Hold on to it between starting and completing. Until it is completed or aborted the store keeps,
/// and bills for, every part uploaded so far.
/// </remarks>
/// <param name="Key">The key the finished object will have.</param>
/// <param name="UploadId">The identifier the store assigned the upload.</param>
public sealed record MultipartUpload(string Key, string UploadId);
