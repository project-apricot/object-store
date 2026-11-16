using ApricotFramework.ObjectStore.Exceptions;

namespace ApricotFramework.ObjectStore.Tests;

public class ObjectKeyTests
{
    [Theory]
    [InlineData("a")]
    [InlineData("folder/file.txt")]
    [InlineData("a//b")]
    [InlineData("dots.in.name/..hidden")]
    [InlineData("ünïcode/ключ.txt")]
    public void Explain_UsableKey_ReturnsNull(string key)
    {
        Assert.Null(ObjectKey.Explain(key));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/leading")]
    [InlineData("a/../b")]
    [InlineData("..")]
    [InlineData("a/./b")]
    [InlineData("../escape")]
    [InlineData("tail/..")]
    [InlineData("with\u0000nul")]
    [InlineData("with\nnewline")]
    [InlineData("with\u007fdel")]
    public void Explain_UnusableKey_ReturnsReason(string? key)
    {
        Assert.NotNull(ObjectKey.Explain(key));
    }

    [Fact]
    public void Explain_KeyAtByteLimit_IsAccepted()
    {
        Assert.Null(ObjectKey.Explain(new string('a', ObjectKey.MaximumBytes)));
    }

    [Fact]
    public void Explain_KeyOverByteLimit_IsRefused()
    {
        Assert.NotNull(ObjectKey.Explain(new string('a', ObjectKey.MaximumBytes + 1)));
    }

    [Fact]
    public void Explain_LimitIsInBytesNotCharacters()
    {
        // 600 two-byte characters are 1200 bytes
        Assert.NotNull(ObjectKey.Explain(new string('é', 600)));
    }

    [Fact]
    public void Explain_ReservedBytes_ComeOffTheLimit()
    {
        // a prefix of 24 bytes leaves 1000 for the key
        Assert.Null(ObjectKey.Explain(new string('a', 1000), reservedBytes: 24));
        Assert.NotNull(ObjectKey.Explain(new string('a', 1001), reservedBytes: 24));
        Assert.Null(ObjectKey.Explain(new string('a', 1001)));
    }

    [Fact]
    public void Explain_WhenAPrefixTakesTheSpace_SaysSo()
    {
        var reason = ObjectKey.Explain(new string('a', 1001), reservedBytes: 24);

        Assert.Contains("1000", reason, StringComparison.Ordinal);
        Assert.Contains("prefix", reason, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_ReservedBytes_AreCounted()
    {
        var e = Assert.Throws<ObjectStoreException>(() => ObjectKey.Validate(new string('a', 1001), "s", "s3", reservedBytes: 24));

        Assert.Equal(ObjectStoreErrorCode.InvalidKey, e.ErrorCode);
    }

    [Fact]
    public void Validate_UnusableKey_ThrowsInvalidKeyNamingTheStore()
    {
        var e = Assert.Throws<ObjectStoreException>(() => ObjectKey.Validate("../x", "uploads", "s3"));

        Assert.Equal(ObjectStoreErrorCode.InvalidKey, e.ErrorCode);
        Assert.Equal("uploads", e.StoreName);
        Assert.Equal("s3", e.Provider);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("folder/")]
    [InlineData("folder/sub")]
    [InlineData("folder//")]
    public void ExplainPrefix_UsablePrefix_ReturnsNull(string? prefix)
    {
        Assert.Null(ObjectKey.ExplainPrefix(prefix));
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/abs")]
    [InlineData("a/../")]
    [InlineData("../")]
    [InlineData("a\nb/")]
    public void ExplainPrefix_UnusablePrefix_ReturnsReason(string prefix)
    {
        Assert.NotNull(ObjectKey.ExplainPrefix(prefix));
    }
}
