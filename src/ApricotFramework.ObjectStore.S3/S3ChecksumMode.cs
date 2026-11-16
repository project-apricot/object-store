namespace ApricotFramework.ObjectStore.S3;

/// <summary>
/// When the AWS SDK adds integrity checksums to requests and validates them on responses.
/// </summary>
/// <remarks>
/// Version 4 of the SDK sends a CRC checksum with every upload by default, and many S3-compatible
/// servers reject the trailing-checksum encoding that carries it. Real AWS wants it; most others do not.
/// </remarks>
public enum S3ChecksumMode
{
    /// <summary>
    /// Checksum whenever the operation supports it. The SDK default, and right for AWS.
    /// </summary>
    WhenSupported = 0,

    /// <summary>
    /// Checksum only where the operation demands it. What S3-compatible servers generally need.
    /// </summary>
    WhenRequired = 1
}
