namespace ApricotFramework.ObjectStore.S3.Tests;

/// <summary>
/// A stream that supplies some bytes and then fails, like a dropped connection.
/// </summary>
internal sealed class FailingStream(int goodBytes) : Stream
{
    /// <summary>
    /// How many good bytes have been handed out so far.
    /// </summary>
    private int served;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (this.served >= goodBytes)
        {
            throw new IOException("the source stream failed");
        }

        var n = Math.Min(count, goodBytes - this.served);

        Array.Fill(buffer, (byte)7, offset, n);
        this.served += n;

        return n;
    }

    public override void Flush() => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
