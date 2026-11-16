using ApricotFramework.ObjectStore.Exceptions;

namespace ApricotFramework.ObjectStore.S3.Tests;

public class S3StoreSettingsTests
{
    private static ObjectStoreSettings With(params (string Key, string? Value)[] values)
    {
        return new ObjectStoreSettings(values.ToDictionary(v => v.Key, v => v.Value), "uploads");
    }

    private static S3StoreSettings Read(params (string Key, string? Value)[] values)
    {
        return S3StoreSettings.From(With([("Bucket", "b"), .. values]));
    }

    [Fact]
    public void Defaults_ForAws()
    {
        var s = Read();

        Assert.Equal("b", s.Bucket);
        Assert.Equal(string.Empty, s.Prefix);
        Assert.Null(s.Region);
        Assert.Null(s.ServiceUrl);
        Assert.False(s.ForcePathStyle);
        Assert.Null(s.AccessKeyId);
        Assert.Equal(S3ChecksumMode.WhenSupported, s.ChecksumMode);
        Assert.Equal(TimeSpan.FromMinutes(2), s.Timeout);
        Assert.Equal(TimeSpan.FromMinutes(15), s.PresignedExpiry);
        Assert.Equal(TimeSpan.FromHours(1), s.MaxPresignedExpiry);
        Assert.Equal(8L * 1024 * 1024, s.MultipartPartSize);
        Assert.Null(s.SigningRegion);
    }

    [Fact]
    public void CustomEndpoint_ChangesTheDefaultsToWhatCompatibleServersNeed()
    {
        var s = Read(("ServiceUrl", "http://localhost:9000"));

        Assert.True(s.ForcePathStyle);
        Assert.Equal(S3ChecksumMode.WhenRequired, s.ChecksumMode);
        Assert.Equal("us-east-1", s.SigningRegion);
    }

    [Fact]
    public void CustomEndpoint_ExplicitSettingsStillWin()
    {
        var s = Read(("ServiceUrl", "https://s3.example.com"), ("ForcePathStyle", "false"), ("ChecksumMode", "WhenSupported"), ("Region", "eu-west-1"));

        Assert.False(s.ForcePathStyle);
        Assert.Equal(S3ChecksumMode.WhenSupported, s.ChecksumMode);
        Assert.Equal("eu-west-1", s.SigningRegion);
    }

    [Fact]
    public void Bucket_IsRequired()
    {
        Assert.Throws<ObjectStoreConfigurationException>(() => S3StoreSettings.From(With()));
    }

    [Theory]
    [InlineData("a/b")]
    [InlineData("a b")]
    public void Bucket_WithSlashOrWhitespace_IsRefused(string bucket)
    {
        Assert.Throws<ObjectStoreConfigurationException>(() => S3StoreSettings.From(With(("Bucket", bucket))));
    }

    [Theory]
    [InlineData("avatars", "avatars/")]
    [InlineData("avatars/", "avatars/")]
    [InlineData("  svc/a  ", "svc/a/")]
    [InlineData("", "")]
    public void Prefix_IsNormalizedToAFolder(string configured, string expected)
    {
        Assert.Equal(expected, Read(("Prefix", configured)).Prefix);
    }

    [Theory]
    [InlineData("/abs")]
    [InlineData("a/../b")]
    [InlineData("..")]
    public void Prefix_ThatCouldEscape_IsRefused(string prefix)
    {
        Assert.Throws<ObjectStoreConfigurationException>(() => Read(("Prefix", prefix)));
    }

    [Theory]
    [InlineData("AccessKeyId")]
    [InlineData("SecretAccessKey")]
    public void HalfAKeyPair_IsRefused_NotSilentlyIgnored(string only)
    {
        var e = Assert.Throws<ObjectStoreConfigurationException>(() => Read((only, "x")));

        Assert.Contains("together", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SessionToken_WithoutKeys_IsRefused()
    {
        Assert.Throws<ObjectStoreConfigurationException>(() => Read(("SessionToken", "t")));
    }

    [Fact]
    public void KeyPair_WithToken_IsRead()
    {
        var s = Read(("AccessKeyId", "id"), ("SecretAccessKey", "secret"), ("SessionToken", "tok"));

        Assert.Equal("id", s.AccessKeyId);
        Assert.Equal("secret", s.SecretAccessKey);
        Assert.Equal("tok", s.SessionToken);
    }

    [Fact]
    public void ErrorMessages_NeverQuoteACredential()
    {
        var e = Assert.Throws<ObjectStoreConfigurationException>(() => Read(("AccessKeyId", "AKIA-LEAK-ME")));

        Assert.DoesNotContain("AKIA-LEAK-ME", e.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ServiceUrl", "not a url")]
    [InlineData("ServiceUrl", "ftp://host")]
    [InlineData("ServiceUrl", "/relative")]
    public void ServiceUrl_MustBeAbsoluteHttp(string name, string value)
    {
        Assert.Throws<ObjectStoreConfigurationException>(() => Read((name, value)));
    }

    [Fact]
    public void PublicServiceUrl_WithoutServiceUrl_IsRefused()
    {
        Assert.Throws<ObjectStoreConfigurationException>(() => Read(("PublicServiceUrl", "https://files.example.com")));
    }

    [Fact]
    public void PublicServiceUrl_IsRead()
    {
        var s = Read(("ServiceUrl", "http://minio:9000"), ("PublicServiceUrl", "https://files.example.com"));

        Assert.Equal("https://files.example.com/", s.PublicServiceUrl!.AbsoluteUri);
    }

    [Theory]
    [InlineData("Timeout", "00:00:00")]
    [InlineData("Timeout", "-00:00:05")]
    [InlineData("Timeout", "30")]
    [InlineData("MaxPresignedExpiry", "00:00:00")]
    [InlineData("MaxPresignedExpiry", "7.00:00:01")]
    [InlineData("PresignedExpiry", "00:00:00")]
    [InlineData("MultipartPartSize", "5242879")]
    [InlineData("MultipartPartSize", "536870913")]
    [InlineData("MultipartPartSize", "5368709120")]
    [InlineData("MultipartPartSize", "lots")]
    [InlineData("ChecksumMode", "1")]
    [InlineData("ChecksumMode", "Sometimes")]
    public void OutOfRangeOrUnreadable_IsRefused(string name, string value)
    {
        Assert.Throws<ObjectStoreConfigurationException>(() => Read((name, value)));
    }

    [Fact]
    public void PresignedExpiry_CannotExceedTheMaximum()
    {
        Assert.Throws<ObjectStoreConfigurationException>(() => Read(("PresignedExpiry", "02:00:00"), ("MaxPresignedExpiry", "01:00:00")));
    }

    [Fact]
    public void MaxPresignedExpiry_AtSevenDays_IsAccepted()
    {
        Assert.Equal(TimeSpan.FromDays(7), Read(("MaxPresignedExpiry", "7.00:00:00"), ("PresignedExpiry", "01:00:00")).MaxPresignedExpiry);
    }

    [Fact]
    public void ConnectionKey_IsEqualForTheSameServerAndIdentity_AndDifferentOtherwise()
    {
        var a = Read(("ServiceUrl", "http://h:9000"), ("AccessKeyId", "id"), ("SecretAccessKey", "s"));
        var sameOtherBucket = S3StoreSettings.From(With(("Bucket", "other"), ("ServiceUrl", "http://h:9000"), ("AccessKeyId", "id"), ("SecretAccessKey", "s")));
        var otherIdentity = Read(("ServiceUrl", "http://h:9000"), ("AccessKeyId", "id2"), ("SecretAccessKey", "s"));
        var otherHost = Read(("ServiceUrl", "http://h2:9000"), ("AccessKeyId", "id"), ("SecretAccessKey", "s"));

        Assert.Equal(a.ConnectionKey, sameOtherBucket.ConnectionKey);
        Assert.NotEqual(a.ConnectionKey, otherIdentity.ConnectionKey);
        Assert.NotEqual(a.ConnectionKey, otherHost.ConnectionKey);
    }

    [Fact]
    public void MultipartPartSize_AtTheCeiling_IsAccepted_BecauseAPartIsHeldInMemory()
    {
        Assert.Equal(512L * 1024 * 1024, Read(("MultipartPartSize", "536870912")).MultipartPartSize);
        Assert.Equal(S3StoreSettings.MaximumPartSize, 512L * 1024 * 1024);
    }

    [Fact]
    public void Prefix_ThatLeavesNoRoomForAKey_IsRefused()
    {
        Assert.Throws<ObjectStoreConfigurationException>(() => Read(("Prefix", new string('p', 1024))));
        Assert.Equal(1000 + 1, Read(("Prefix", new string('p', 1000))).PrefixBytes);
    }

    [Fact]
    public void PrefixBytes_CountsUtf8Bytes_NotCharacters()
    {
        // "é/" is three bytes
        Assert.Equal(3, Read(("Prefix", "é")).PrefixBytes);
        Assert.Equal(0, Read().PrefixBytes);
    }

    [Fact]
    public void ConnectionKey_IsExact_SinceAccessKeysAreCaseSensitive()
    {
        var upper = Read(("ServiceUrl", "http://h:9000"), ("AccessKeyId", "AKIAEXAMPLE"), ("SecretAccessKey", "s"));
        var lower = Read(("ServiceUrl", "http://h:9000"), ("AccessKeyId", "akiaexample"), ("SecretAccessKey", "s"));

        Assert.NotEqual(upper.ConnectionKey, lower.ConnectionKey);
    }

    [Fact]
    public void ConnectionKey_SeparatesIdentitiesThatShareAnAccessKeyButNotWhatSignsForIt()
    {
        var basis = Read(("ServiceUrl", "http://h:9000"), ("AccessKeyId", "id"), ("SecretAccessKey", "one"));
        var rotated = Read(("ServiceUrl", "http://h:9000"), ("AccessKeyId", "id"), ("SecretAccessKey", "two"));
        var withToken = Read(("ServiceUrl", "http://h:9000"), ("AccessKeyId", "id"), ("SecretAccessKey", "one"), ("SessionToken", "t"));
        var same = Read(("ServiceUrl", "http://h:9000"), ("AccessKeyId", "id"), ("SecretAccessKey", "one"));

        Assert.NotEqual(basis.ConnectionKey, rotated.ConnectionKey);
        Assert.NotEqual(basis.ConnectionKey, withToken.ConnectionKey);
        Assert.Equal(basis.ConnectionKey, same.ConnectionKey);
    }

    [Fact]
    public void ConnectionKey_NeverContainsTheSecretOrTheToken()
    {
        var key = Read(("AccessKeyId", "id"), ("SecretAccessKey", "plain-secret-value"), ("SessionToken", "plain-token-value")).ConnectionKey;

        Assert.DoesNotContain("plain-secret-value", key, StringComparison.Ordinal);
        Assert.DoesNotContain("plain-token-value", key, StringComparison.Ordinal);
    }

    [Fact]
    public void ConnectionKey_NeverContainsTheSecret()
    {
        Assert.DoesNotContain("TOPSECRET", Read(("AccessKeyId", "id"), ("SecretAccessKey", "topsecret")).ConnectionKey, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void From_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => S3StoreSettings.From(null!));
    }
}
