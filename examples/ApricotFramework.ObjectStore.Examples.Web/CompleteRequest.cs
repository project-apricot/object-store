namespace ApricotFramework.ObjectStore.Examples.Web;

/// <summary>
/// What the client sends to finish a multipart upload.
/// </summary>
/// <param name="Key">The key the finished object will have.</param>
/// <param name="UploadId">The identifier the store assigned the upload.</param>
/// <param name="Parts">Every part, in ascending part-number order.</param>
internal sealed record CompleteRequest(string Key, string UploadId, IReadOnlyList<CompletedPart> Parts);
