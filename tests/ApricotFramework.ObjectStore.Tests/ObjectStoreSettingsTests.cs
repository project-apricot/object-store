using ApricotFramework.ObjectStore.Exceptions;

namespace ApricotFramework.ObjectStore.Tests;

public class ObjectStoreSettingsTests
{
    private static ObjectStoreSettings With(params (string Key, string? Value)[] values)
    {
        return new ObjectStoreSettings(values.ToDictionary(v => v.Key, v => v.Value), "uploads");
    }

    [Fact]
    public void Names_AreMatchedWithoutRegardToCase()
    {
        Assert.Equal("x", With(("Bucket", "x")).GetString("bUCKET"));
    }

    [Fact]
    public void GetString_BlankValue_ReturnsFallback()
    {
        Assert.Equal("fb", With(("a", "  ")).GetString("a", "fb"));
    }

    [Fact]
    public void GetRequiredString_Missing_ThrowsNamingStoreAndSetting()
    {
        var e = Assert.Throws<ObjectStoreConfigurationException>(() => With().GetRequiredString("Bucket"));

        Assert.Contains("Bucket", e.Message, StringComparison.Ordinal);
        Assert.Contains("uploads", e.Message, StringComparison.Ordinal);
        Assert.Equal(ObjectStoreErrorCode.Configuration, e.ErrorCode);
    }

    [Fact]
    public void Unreadable_NeverQuotesTheValue()
    {
        var e = Assert.Throws<ObjectStoreConfigurationException>(() => With(("Port", "s3cr3t-value")).GetInt32("Port", 1));

        Assert.DoesNotContain("s3cr3t-value", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GetInt32_And_GetInt64_ParseInvariantly()
    {
        var settings = With(("a", "42"), ("b", "5368709120"));

        Assert.Equal(42, settings.GetInt32("a", 0));
        Assert.Equal(5368709120L, settings.GetInt64("b", 0));
        Assert.Equal(7, settings.GetInt32("missing", 7));
    }

    [Fact]
    public void GetInt32_Overflowing_Throws()
    {
        Assert.Throws<ObjectStoreConfigurationException>(() => With(("a", "5368709120")).GetInt32("a", 0));
    }

    [Theory]
    [InlineData("True", true)]
    [InlineData("false", false)]
    public void GetBoolean_IsCaseInsensitive(string text, bool expected)
    {
        Assert.Equal(expected, With(("a", text)).GetBoolean("a", !expected));
    }

    [Fact]
    public void GetBoolean_NotABoolean_Throws()
    {
        Assert.Throws<ObjectStoreConfigurationException>(() => With(("a", "yes")).GetBoolean("a", false));
    }

    [Fact]
    public void GetTimeSpan_ColonForm_Parses()
    {
        Assert.Equal(TimeSpan.FromSeconds(30), With(("t", "00:00:30")).GetTimeSpan("t", TimeSpan.Zero));
    }

    [Fact]
    public void GetTimeSpan_BareNumber_IsRejectedRatherThanReadAsDays()
    {
        Assert.Throws<ObjectStoreConfigurationException>(() => With(("t", "30")).GetTimeSpan("t", TimeSpan.Zero));
    }

    [Fact]
    public void GetEnum_ByName_IsCaseInsensitive()
    {
        Assert.Equal(SampleMode.Second, With(("m", "second")).GetEnum("m", SampleMode.First));
    }

    [Theory]
    [InlineData("1")]
    [InlineData("+1")]
    [InlineData("-1")]
    [InlineData("Third")]
    [InlineData("99")]
    public void GetEnum_OrdinalOrUnknownName_Throws(string text)
    {
        Assert.Throws<ObjectStoreConfigurationException>(() => With(("m", text)).GetEnum("m", SampleMode.First));
    }

    [Fact]
    public void Constructor_CopiesTheValues()
    {
        var source = new Dictionary<string, string?> { ["a"] = "1" };
        var settings = new ObjectStoreSettings(source);

        source["a"] = "2";

        Assert.Equal("1", settings.GetString("a"));
    }

    [Fact]
    public void GetString_BlankName_Throws()
    {
        Assert.Throws<ArgumentException>(() => With().GetString(" "));
    }
}
