using System.Text;
using System.Text.Json;
using System.Web;
using ApricotFramework.ObjectStore.Exceptions;
using ApricotFramework.ObjectStore.Model;

namespace ApricotFramework.ObjectStore.S3.Tests;

/// <summary>
/// Signing is local arithmetic, so none of this needs a server.
/// </summary>
public class S3PresignedOfflineTests
{
    /// <summary>
    /// The SDK signs against the system clock, so expiry can only be checked to within the moment the test took to run.
    /// </summary>
    private static readonly TimeSpan Tolerance = TimeSpan.FromSeconds(10);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static S3ObjectStore Store(Action<Dictionary<string, string?>>? tweak = null, string? prefix = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["Bucket"] = "bkt",
            ["ServiceUrl"] = "http://storage.internal:9000",
            ["AccessKeyId"] = "AKIDEXAMPLE",
            ["SecretAccessKey"] = "wJalrXUtnFEMI-K7MDENG-bPxRfiCYEXAMPLEKEY",
        };

        if (prefix is not null)
        {
            settings["Prefix"] = prefix;
        }

        tweak?.Invoke(settings);

        return new S3ObjectStore("signer", S3StoreSettings.From(new ObjectStoreSettings(settings, "signer")));
    }

    private static void AssertExpiresIn(TimeSpan expected, DateTimeOffset actual)
    {
        Assert.InRange(actual - DateTimeOffset.UtcNow, expected - Tolerance, expected + Tolerance);
    }

    private static void AssertSeconds(int expected, string actual)
    {
        // the SDK counts down from its own reading of the clock, a moment after ours
        Assert.InRange(int.Parse(actual, System.Globalization.CultureInfo.InvariantCulture), expected - 5, expected);
    }

    private static Dictionary<string, string> Query(Uri url)
    {
        var parsed = HttpUtility.ParseQueryString(url.Query);

        return parsed.AllKeys.Where(k => k is not null).ToDictionary(k => k!, k => parsed[k]!, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Download_IsAGetForTheKeyUnderThePrefix_SignedForTheRequestedTime()
    {
        using var store = Store(prefix: "svc");

        var request = await store.CreateDownloadUrlAsync("docs/a b.txt", new PresignedDownloadOptions { Expiry = TimeSpan.FromMinutes(10) }, Ct);
        var query = Query(request.Url);

        Assert.Equal("GET", request.Method);
        Assert.Equal("storage.internal", request.Url.Host);
        Assert.Equal("/bkt/svc/docs/a%20b.txt", request.Url.AbsolutePath);
        AssertExpiresIn(TimeSpan.FromMinutes(10), request.ExpiresAt);
        AssertSeconds(600, query["X-Amz-Expires"]);
        Assert.Equal("AWS4-HMAC-SHA256", query["X-Amz-Algorithm"]);
        Assert.StartsWith($"AKIDEXAMPLE/{DateTime.UtcNow:yyyyMMdd}/us-east-1/s3/aws4_request", query["X-Amz-Credential"], StringComparison.Ordinal);
        Assert.Matches("^[0-9a-f]{64}$", query["X-Amz-Signature"]);
        Assert.Empty(request.RequiredHeaders);
    }

    [Fact]
    public async Task Download_DefaultExpiry_IsTheStoresDefault()
    {
        using var store = Store(s => s["PresignedExpiry"] = "00:05:00");

        var request = await store.CreateDownloadUrlAsync("k", cancellationToken: Ct);

        AssertExpiresIn(TimeSpan.FromMinutes(5), request.ExpiresAt);
        AssertSeconds(300, Query(request.Url)["X-Amz-Expires"]);
    }

    [Fact]
    public async Task Download_ResponseOverrides_AreSigned()
    {
        using var store = Store();

        var request = await store.CreateDownloadUrlAsync("k", new PresignedDownloadOptions
        {
            ContentDisposition = "attachment; filename=\"report.pdf\"",
            ContentType = "application/pdf",
        }, Ct);
        var query = Query(request.Url);

        Assert.Equal("attachment; filename=\"report.pdf\"", query["response-content-disposition"]);
        Assert.Equal("application/pdf", query["response-content-type"]);
    }

    [Fact]
    public async Task Upload_IsAPut_AndReturnsEveryHeaderTheClientMustSend()
    {
        using var store = Store();

        var request = await store.CreateUploadUrlAsync("k", new PresignedUploadOptions
        {
            ContentType = "image/png",
            Metadata = new Dictionary<string, string> { ["Owner"] = "me" },
        }, Ct);

        Assert.Equal("PUT", request.Method);
        Assert.Equal("image/png", request.RequiredHeaders["Content-Type"]);
        Assert.Equal("me", request.RequiredHeaders["x-amz-meta-owner"]);

        var signed = Query(request.Url)["X-Amz-SignedHeaders"].Split(';');

        Assert.Contains("content-type", signed);
        Assert.Contains("x-amz-meta-owner", signed);
    }

    [Fact]
    public async Task Upload_WithNothingPinned_RequiresNoHeaders()
    {
        using var store = Store();

        var request = await store.CreateUploadUrlAsync("k", cancellationToken: Ct);

        Assert.Empty(request.RequiredHeaders);
        Assert.DoesNotContain("content-type", Query(request.Url)["X-Amz-SignedHeaders"].Split(';'));
    }

    [Fact]
    public async Task PublicServiceUrl_IsWhatTheUrlIsSignedFor_AndHttpsIsKept()
    {
        using var store = Store(s =>
        {
            s["ServiceUrl"] = "http://storage.internal:9000";
            s["PublicServiceUrl"] = "https://files.example.com";
        });

        var request = await store.CreateDownloadUrlAsync("k", cancellationToken: Ct);

        Assert.Equal("https", request.Url.Scheme);
        Assert.Equal("files.example.com", request.Url.Host);
    }

    [Fact]
    public async Task Part_Url_CarriesTheUploadAndPartNumber()
    {
        using var store = Store();

        var request = await store.CreatePartUploadUrlAsync(new MultipartUpload("k", "upload-1"), 3, TimeSpan.FromMinutes(5), Ct);
        var query = Query(request.Url);

        Assert.Equal("PUT", request.Method);
        Assert.Equal("upload-1", query["uploadId"]);
        Assert.Equal("3", query["partNumber"]);
        AssertExpiresIn(TimeSpan.FromMinutes(5), request.ExpiresAt);
    }

    [Theory]
    [InlineData("00:00:00")]
    [InlineData("-00:01:00")]
    [InlineData("01:00:01")]
    [InlineData("7.00:00:00")]
    public async Task Expiry_OutsideWhatTheStoreAllows_IsRefusedNotQuietlyShortened(string expiry)
    {
        using var store = Store();

        var e = await Assert.ThrowsAsync<ObjectStoreException>(() => store.CreateDownloadUrlAsync("k", new PresignedDownloadOptions { Expiry = TimeSpan.Parse(expiry, System.Globalization.CultureInfo.InvariantCulture) }, Ct));

        Assert.Equal(ObjectStoreErrorCode.InvalidRequest, e.ErrorCode);

        await Assert.ThrowsAsync<ObjectStoreException>(() => store.CreateUploadUrlAsync("k", new PresignedUploadOptions { Expiry = TimeSpan.Parse(expiry, System.Globalization.CultureInfo.InvariantCulture) }, Ct));
        await Assert.ThrowsAsync<ObjectStoreException>(() => store.CreateUploadFormAsync("k", new PresignedPostOptions { Expiry = TimeSpan.Parse(expiry, System.Globalization.CultureInfo.InvariantCulture) }, Ct));
    }

    [Fact]
    public async Task Expiry_UpToTheConfiguredMaximum_IsAccepted()
    {
        using var store = Store(s => s["MaxPresignedExpiry"] = "7.00:00:00");

        var request = await store.CreateDownloadUrlAsync("k", new PresignedDownloadOptions { Expiry = TimeSpan.FromDays(7) }, Ct);

        AssertSeconds(604800, Query(request.Url)["X-Amz-Expires"]);
    }

    [Theory]
    [InlineData("../x")]
    [InlineData("/x")]
    [InlineData("")]
    public async Task InvalidKeys_AreRefusedBeforeAnythingIsSigned(string key)
    {
        using var store = Store();

        await Assert.ThrowsAsync<ObjectStoreException>(() => store.CreateDownloadUrlAsync(key, cancellationToken: Ct));
        await Assert.ThrowsAsync<ObjectStoreException>(() => store.CreateUploadUrlAsync(key, cancellationToken: Ct));
        await Assert.ThrowsAsync<ObjectStoreException>(() => store.CreateUploadFormAsync(key, cancellationToken: Ct));
    }

    [Fact]
    public async Task Part_NumberOutOfRange_IsRefused()
    {
        using var store = Store();

        await Assert.ThrowsAsync<ObjectStoreException>(() => store.CreatePartUploadUrlAsync(new MultipartUpload("k", "u"), 0, cancellationToken: Ct));
        await Assert.ThrowsAsync<ObjectStoreException>(() => store.CreatePartUploadUrlAsync(new MultipartUpload("k", "u"), 10001, cancellationToken: Ct));
    }

    [Fact]
    public async Task Form_PostsToTheBucket_WithAPolicyThatPinsTheKeyAndTheLimits()
    {
        using var store = Store(prefix: "svc");

        var form = await store.CreateUploadFormAsync("up/photo.jpg", new PresignedPostOptions
        {
            Expiry = TimeSpan.FromMinutes(5),
            ContentType = "image/jpeg",
            MinimumSize = 10,
            MaximumSize = 1_000_000,
            Metadata = new Dictionary<string, string> { ["Owner"] = "me" },
        }, Ct);

        Assert.Equal("http://storage.internal:9000/bkt", form.Url.AbsoluteUri);
        AssertExpiresIn(TimeSpan.FromMinutes(5), form.ExpiresAt);
        Assert.Equal("svc/up/photo.jpg", form.Fields["key"]);
        Assert.Equal("image/jpeg", form.Fields["Content-Type"]);
        Assert.Equal("me", form.Fields["x-amz-meta-owner"]);
        Assert.Equal("AWS4-HMAC-SHA256", form.Fields["x-amz-algorithm"]);
        Assert.StartsWith("AKIDEXAMPLE/", form.Fields["x-amz-credential"], StringComparison.Ordinal);
        Assert.EndsWith("/us-east-1/s3/aws4_request", form.Fields["x-amz-credential"], StringComparison.Ordinal);
        Assert.Matches("^[0-9a-f]{64}$", form.Fields["x-amz-signature"]);

        using var policy = JsonDocument.Parse(Convert.FromBase64String(form.Fields["policy"]));
        var text = policy.RootElement.GetRawText();

        Assert.True(DateTimeOffset.Parse(policy.RootElement.GetProperty("expiration").GetString()!, System.Globalization.CultureInfo.InvariantCulture) > DateTimeOffset.UtcNow.AddMinutes(4));
        Assert.Contains("\"bucket\":\"bkt\"", text, StringComparison.Ordinal);
        Assert.Contains("\"svc/up/photo.jpg\"", text, StringComparison.Ordinal);
        Assert.Contains("[\"content-length-range\",10,1000000]", text, StringComparison.Ordinal);
        Assert.Contains("\"image/jpeg\"", text, StringComparison.Ordinal);

    }

    [Fact]
    public async Task Form_MaximumOnly_StillGivesTheServerBothEnds()
    {
        using var store = Store();

        var form = await store.CreateUploadFormAsync("k", new PresignedPostOptions { MaximumSize = 500 }, Ct);
        var policy = Encoding.UTF8.GetString(Convert.FromBase64String(form.Fields["policy"]));

        Assert.Contains("[\"content-length-range\",0,500]", policy, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Form_NoLimits_HasNoSizeCondition()
    {
        using var store = Store();

        var form = await store.CreateUploadFormAsync("k", cancellationToken: Ct);

        Assert.DoesNotContain("content-length-range", Encoding.UTF8.GetString(Convert.FromBase64String(form.Fields["policy"])), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Form_InvertedOrNegativeLimits_AreRefused()
    {
        using var store = Store();

        await Assert.ThrowsAsync<ObjectStoreException>(() => store.CreateUploadFormAsync("k", new PresignedPostOptions { MinimumSize = 10, MaximumSize = 5 }, Ct));
        await Assert.ThrowsAsync<ObjectStoreException>(() => store.CreateUploadFormAsync("k", new PresignedPostOptions { MaximumSize = -1 }, Ct));
    }

    [Fact]
    public async Task Form_WithSessionCredentials_IncludesTheSecurityToken()
    {
        using var store = Store(s => s["SessionToken"] = "session-token-value");

        var form = await store.CreateUploadFormAsync("k", cancellationToken: Ct);

        Assert.Equal("session-token-value", form.Fields["x-amz-security-token"]);
    }

    [Fact]
    public async Task Form_Url_ForAws_IsTheBucketHost()
    {
        using var virtualHosted = Store(s =>
        {
            s.Remove("ServiceUrl");
            s["Region"] = "eu-west-1";
        });

        using var pathStyle = Store(s =>
        {
            s.Remove("ServiceUrl");
            s["Region"] = "eu-west-1";
            s["ForcePathStyle"] = "true";
        });

        Assert.Equal("https://bkt.s3.eu-west-1.amazonaws.com/", (await virtualHosted.CreateUploadFormAsync("k", cancellationToken: Ct)).Url.AbsoluteUri);
        Assert.Equal("https://s3.eu-west-1.amazonaws.com/bkt", (await pathStyle.CreateUploadFormAsync("k", cancellationToken: Ct)).Url.AbsoluteUri);
    }

    [Fact]
    public async Task NoRegionAnywhere_IsAConfigurationError_NotARawSdkException()
    {
        // without this the SDK waits for the instance metadata service to answer before giving up
        var before = Environment.GetEnvironmentVariable("AWS_EC2_METADATA_DISABLED");

        Environment.SetEnvironmentVariable("AWS_EC2_METADATA_DISABLED", "true");

        try
        {
            // whether the SDK can find a region elsewhere depends on the machine, so building the store may
            // succeed, but when anything fails it has to be an error that says what to set
            var failure = await Record.ExceptionAsync(async () =>
            {
                using var store = Store(s =>
                {
                    s.Remove("ServiceUrl");
                    s.Remove("Region");
                });

                await store.CreateUploadFormAsync("k", cancellationToken: Ct);
            });

            Assert.True(failure is null or ObjectStoreConfigurationException, $"unexpected {failure?.GetType().Name}: {failure?.Message}");

            if (failure is not null)
            {
                Assert.Contains("Region", failure.Message, StringComparison.Ordinal);
                Assert.IsAssignableFrom<Amazon.Runtime.AmazonClientException>(failure.InnerException ?? failure);
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("AWS_EC2_METADATA_DISABLED", before);
        }
    }

    [Fact]
    public async Task Form_Url_UsesThePublicEndpoint()
    {
        using var store = Store(s => s["PublicServiceUrl"] = "https://files.example.com/");

        Assert.Equal("https://files.example.com/bkt", (await store.CreateUploadFormAsync("k", cancellationToken: Ct)).Url.AbsoluteUri);
    }

    [Fact]
    public async Task ToString_NeverPrintsTheBearerUrl()
    {
        using var store = Store();

        var request = await store.CreateDownloadUrlAsync("k", cancellationToken: Ct);
        var form = await store.CreateUploadFormAsync("k", cancellationToken: Ct);

        Assert.DoesNotContain("X-Amz-Signature", request.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("storage.internal", request.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(form.Fields["x-amz-signature"], form.ToString(), StringComparison.Ordinal);
    }
}
