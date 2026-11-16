namespace ApricotFramework.ObjectStore.S3.Impl;

/// <summary>
/// Marks a failure reading the caller's own stream, so it is not mistaken for a failure of the store.
/// </summary>
/// <remarks>
/// A dropped connection upstream of the upload says nothing about the store, and reporting it as
/// unavailable would invite a retry that cannot work. It is unwrapped and rethrown as it was.
/// </remarks>
/// <param name="innerException">The failure of the caller's stream.</param>
internal sealed class ContentReadException(Exception innerException)
    : Exception("The content stream failed while being read.", innerException);
