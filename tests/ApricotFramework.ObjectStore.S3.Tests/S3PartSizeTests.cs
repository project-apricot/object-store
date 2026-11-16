using ApricotFramework.ObjectStore.Exceptions;

namespace ApricotFramework.ObjectStore.S3.Tests;

/// <summary>
/// Which part size an upload uses. An object that needs tens of gigabytes to show the difference cannot be
/// uploaded in a test, so this is checked on the arithmetic.
/// </summary>
public class S3PartSizeTests
{
    private const long Mib = 1024 * 1024;

    private const long Gib = 1024 * Mib;

    private static S3ObjectStore Store(string? partSize = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Bucket"] = "bkt",
            ["ServiceUrl"] = "http://storage.internal:9000",
            ["AccessKeyId"] = "id",
            ["SecretAccessKey"] = "secret",
            ["MultipartPartSize"] = partSize,
        };

        return new S3ObjectStore("s", S3StoreSettings.From(new ObjectStoreSettings(settings, "s")));
    }

    [Theory]
    [InlineData(1L)]
    [InlineData(100L * Mib)]
    [InlineData(70L * Gib)]
    public void ObjectsThatFitInTenThousandDefaultParts_UseTheConfiguredSize(long length)
    {
        using var store = Store();

        Assert.Equal(8 * Mib, store.ChoosePartSize("k", length));
    }

    [Theory]
    [InlineData(100L * Gib)]
    [InlineData(1024L * Gib)]
    [InlineData(10000L * 512 * Mib)]
    public void BiggerObjects_GetAPartSizeThatKeepsThemUnderTenThousandParts(long length)
    {
        using var store = Store();

        var size = store.ChoosePartSize("k", length);

        Assert.True(size > 8 * Mib);
        Assert.True(size <= S3StoreSettings.MaximumPartSize);
        Assert.True(size % Mib == 0, "whole mebibytes, so no part is an odd size");
        Assert.True((length + size - 1) / size <= 10000, $"{length} bytes in parts of {size} would need more than 10000 parts");
    }

    [Fact]
    public void TheLargestObjectTheCeilingAllows_IsAboutFourPointNineTebibytes()
    {
        // stated so nobody reads the ceiling as S3's own 5 TiB limit
        Assert.True(10000L * S3StoreSettings.MaximumPartSize < 5L * 1024 * Gib);
        Assert.True(10000L * S3StoreSettings.MaximumPartSize > 4L * 1024 * Gib);
    }

    [Fact]
    public void AnObjectTooBigForAnyPartSize_IsRefusedBeforeAnythingIsSent()
    {
        using var store = Store();

        var e = Assert.Throws<ObjectStoreException>(() => store.ChoosePartSize("k", (10000L * S3StoreSettings.MaximumPartSize) + 1));

        Assert.Equal(ObjectStoreErrorCode.InvalidRequest, e.ErrorCode);
        Assert.Equal("k", e.Key);
    }

    [Fact]
    public void ALargerConfiguredSize_IsNeverShrunk()
    {
        using var store = Store((64 * Mib).ToString(System.Globalization.CultureInfo.InvariantCulture));

        Assert.Equal(64 * Mib, store.ChoosePartSize("k", 100 * Mib));
    }

    [Fact]
    public void WithoutALength_TheConfiguredSizeIsWhereItStarts()
    {
        using var store = Store();

        Assert.Equal(8 * Mib, store.ChoosePartSize("k", null));
    }

    [Theory]
    [InlineData(1, 8L)]
    [InlineData(1000, 8L)]
    [InlineData(1001, 16L)]
    [InlineData(2001, 32L)]
    [InlineData(3001, 64L)]
    [InlineData(4001, 128L)]
    [InlineData(5001, 256L)]
    [InlineData(6001, 512L)]
    [InlineData(10000, 512L)]
    public void WithoutALength_ThePartSizeDoublesEveryThousandParts_UpToTheCeiling(int number, long expectedMib)
    {
        Assert.Equal(expectedMib * Mib, S3ObjectStore.PartSizeAt(8 * Mib, number, growing: true));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5000)]
    [InlineData(10000)]
    public void WithALength_EveryPartIsTheSameSize(int number)
    {
        Assert.Equal(11 * Mib, S3ObjectStore.PartSizeAt(11 * Mib, number, growing: false));
    }

    [Fact]
    public void TheGrowingSizes_AddUpToMoreThanAnyStreamThatFitsInMemoryAnyway()
    {
        long total = 0;

        for (var number = 1; number <= 10000; number++)
        {
            total += S3ObjectStore.PartSizeAt(8 * Mib, number, growing: true);
        }

        // 10000 parts at the growing sizes: well past a terabyte, which is what an unknown-length stream can be
        Assert.True(total > 1024 * Gib);
    }
}
