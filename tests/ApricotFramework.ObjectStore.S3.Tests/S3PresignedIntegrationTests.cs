using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using ApricotFramework.ObjectStore.Exceptions;
using ApricotFramework.ObjectStore.Model;

namespace ApricotFramework.ObjectStore.S3.Tests;

/// <summary>
/// What a browser or a mobile app would do with what the store hands out: call the URL with plain HTTP and
/// no credentials of its own.
/// </summary>
[Collection(S3ServerDefinition.Name)]
public class S3PresignedIntegrationTests(S3ServerFixture server)
{
    /// <summary>
    /// One client for every request, as an HTTP client is meant to be used.
    /// </summary>
    private static readonly HttpClient Http = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static HttpRequestMessage ToHttp(PresignedRequest signed, byte[]? body = null, Action<HttpRequestMessage>? tweak = null)
    {
        var request = new HttpRequestMessage(new HttpMethod(signed.Method), signed.Url);

        if (body is not null)
        {
            request.Content = new ByteArrayContent(body);
        }

        // sent exactly as given, which is the whole contract of RequiredHeaders
        foreach (var (name, value) in signed.RequiredHeaders)
        {
            if (name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
            {
                request.Content ??= new ByteArrayContent([]);
                request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(value);
            }
            else
            {
                request.Headers.TryAddWithoutValidation(name, value);
            }
        }

        tweak?.Invoke(request);

        return request;
    }

    private static async Task<HttpResponseMessage> SendAsync(PresignedRequest signed, byte[]? body = null, Action<HttpRequestMessage>? tweak = null)
    {
        using var request = ToHttp(signed, body, tweak);

        return await Http.SendAsync(request, Ct);
    }

    private static MultipartFormDataContent Form(PresignedPost post, byte[] file, string? contentTypeOverride = null)
    {
        var form = new MultipartFormDataContent();

        foreach (var (name, value) in post.Fields)
        {
            var text = new StringContent(value);

            // a bare text field has no content type of its own; the default one is noise a strict server may reject
            text.Headers.ContentType = null;
            form.Add(text, name);
        }

        var content = new ByteArrayContent(file);

        if (contentTypeOverride is not null)
        {
            content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentTypeOverride);
        }

        // the file goes last, after every field the policy covers
        form.Add(content, "file", "upload.bin");

        return form;
    }

    [Fact]
    public async Task PresignedUpload_ThenPresignedDownload_RoundTripsWithPlainHttp()
    {
        var (store, _) = await server.CreateStoreAsync();
        using var _s = store;

        var data = Encoding.UTF8.GetBytes("uploaded straight from a client");

        var upload = await store.CreateUploadUrlAsync("direct/a.txt", new PresignedUploadOptions
        {
            ContentType = "text/plain",
            Metadata = new Dictionary<string, string> { ["Owner"] = "browser" },
        }, Ct);

        using (var put = await SendAsync(upload, data))
        {
            Assert.True(put.IsSuccessStatusCode, $"upload answered {(int)put.StatusCode}");
        }

        // the store sees what the client put there, including what was pinned in the signature
        var info = await store.GetInfoAsync("direct/a.txt", Ct);

        Assert.Equal(data.Length, info.Size);
        Assert.Equal("text/plain", info.ContentType);
        Assert.Equal("browser", info.Metadata["owner"]);

        var download = await store.CreateDownloadUrlAsync("direct/a.txt", cancellationToken: Ct);

        using var get = await SendAsync(download);

        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal(data, await get.Content.ReadAsByteArrayAsync(Ct));
    }

    [Fact]
    public async Task PresignedUpload_WithoutTheRequiredHeaders_IsRefused()
    {
        var (store, _) = await server.CreateStoreAsync();
        using var _s = store;

        var upload = await store.CreateUploadUrlAsync("pinned", new PresignedUploadOptions { ContentType = "image/png" }, Ct);

        using var wrongType = await SendAsync(upload, [1, 2, 3], r => r.Content!.Headers.ContentType = MediaTypeHeaderValue.Parse("text/html"));
        using var noType = await SendAsync(new PresignedRequest { Url = upload.Url, Method = "PUT", ExpiresAt = upload.ExpiresAt }, [1, 2, 3]);

        Assert.Equal(HttpStatusCode.Forbidden, wrongType.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, noType.StatusCode);
        Assert.False(await store.ExistsAsync("pinned", Ct));
    }

    [Fact]
    public async Task PresignedUrl_ForAnotherKey_CannotBeRedirected()
    {
        var (store, _) = await server.CreateStoreAsync();
        using var _s = store;

        await store.PutAsync("secret", new MemoryStream([1]), cancellationToken: Ct);
        await store.PutAsync("public", new MemoryStream([2]), cancellationToken: Ct);

        var download = await store.CreateDownloadUrlAsync("public", cancellationToken: Ct);
        var tampered = new UriBuilder(download.Url) { Path = download.Url.AbsolutePath.Replace("public", "secret", StringComparison.Ordinal) }.Uri;

        using var response = await SendAsync(new PresignedRequest { Url = tampered, Method = "GET", ExpiresAt = download.ExpiresAt });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PresignedDownload_ResponseOverrides_ArePassedToTheClient()
    {
        var (store, _) = await server.CreateStoreAsync();
        using var _s = store;

        await store.PutAsync("report", new MemoryStream(Encoding.UTF8.GetBytes("%PDF")), new ObjectPutOptions { ContentType = "application/octet-stream" }, Ct);

        var download = await store.CreateDownloadUrlAsync("report", new PresignedDownloadOptions
        {
            ContentDisposition = "attachment; filename=\"q3.pdf\"",
            ContentType = "application/pdf",
        }, Ct);

        using var response = await SendAsync(download);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal("q3.pdf", response.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
    }

    [Fact]
    public async Task PresignedDownload_OfAMissingObject_Is404_NotAStoreError()
    {
        var (store, _) = await server.CreateStoreAsync();
        using var _s = store;

        // signing never checks the object exists; the URL fails only when it is used
        var download = await store.CreateDownloadUrlAsync("not-there", cancellationToken: Ct);

        using var response = await SendAsync(download);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PresignedUrl_StopsWorkingWhenItExpires()
    {
        var (store, _) = await server.CreateStoreAsync(s =>
        {
            s["PresignedExpiry"] = "00:00:01";
            s["MaxPresignedExpiry"] = "00:00:05";
        });

        using var _s = store;

        await store.PutAsync("k", new MemoryStream([1]), cancellationToken: Ct);

        var download = await store.CreateDownloadUrlAsync("k", cancellationToken: Ct);

        using (var early = await SendAsync(download))
        {
            Assert.Equal(HttpStatusCode.OK, early.StatusCode);
        }

        await Task.Delay(TimeSpan.FromSeconds(3), Ct);

        using var late = await SendAsync(download);

        Assert.Equal(HttpStatusCode.Forbidden, late.StatusCode);
    }

    [Fact]
    public async Task PublicServiceUrl_IsWhatTheClientSeesAndWhatTheSignatureCovers()
    {
        var bucket = await server.CreateBucketAsync();

        // this service reaches the server by one name, while the client in the world outside uses another
        var internalHost = server.Endpoint!.Host == "localhost" ? "127.0.0.1" : "localhost";
        var publicUrl = server.Endpoint.AbsoluteUri;

        using var store = server.CreateStore(bucket, s =>
        {
            s["ServiceUrl"] = $"http://{internalHost}:{server.Endpoint.Port}/";
            s["PublicServiceUrl"] = publicUrl;
        });

        await store.PutAsync("k", new MemoryStream(Encoding.UTF8.GetBytes("via the front door")), cancellationToken: Ct);

        var download = await store.CreateDownloadUrlAsync("k", cancellationToken: Ct);

        Assert.Equal(server.Endpoint.Host, download.Url.Host);
        Assert.NotEqual(internalHost, download.Url.Host);

        using var response = await SendAsync(download);

        Assert.Equal("via the front door", await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task PresignedMultipart_ClientUploadsPartsDirectly_ThenTheServiceCompletes()
    {
        var (store, _) = await server.CreateStoreAsync();
        using var _s = store;

        var one = RandomNumberGenerator.GetBytes(5 * 1024 * 1024);
        var two = RandomNumberGenerator.GetBytes(321);

        var upload = await store.StartMultipartUploadAsync("direct-big", new MultipartUploadOptions { ContentType = "application/x-big" }, Ct);
        var parts = new List<UploadedPart>();

        foreach (var (number, bytes) in new[] { (1, one), (2, two) })
        {
            var signed = await store.CreatePartUploadUrlAsync(upload, number, cancellationToken: Ct);

            using var response = await SendAsync(signed, bytes);

            Assert.True(response.IsSuccessStatusCode, $"part {number} answered {(int)response.StatusCode}");

            // the client reports the ETag the store answered with, which completing the upload needs
            parts.Add(new UploadedPart(number, response.Headers.ETag!.Tag));
        }

        var info = await store.CompleteMultipartUploadAsync(upload, parts, Ct);

        Assert.Equal(one.Length + two.Length, info.Size);
        Assert.Equal("application/x-big", info.ContentType);

        var download = await store.CreateDownloadUrlAsync("direct-big", cancellationToken: Ct);

        using var get = await SendAsync(download);

        Assert.Equal(SHA256.HashData([.. one, .. two]), SHA256.HashData(await get.Content.ReadAsByteArrayAsync(Ct)));
    }

    [Fact]
    public async Task PresignedForm_AcceptsAnUploadWithinTheLimits()
    {
        var (store, _) = await server.CreateStoreAsync(s => s["Prefix"] = "forms");
        using var _s = store;

        var post = await store.CreateUploadFormAsync("avatar.png", new PresignedPostOptions
        {
            ContentType = "image/png",
            MinimumSize = 4,
            MaximumSize = 1024,
            Metadata = new Dictionary<string, string> { ["Owner"] = "form" },
        }, Ct);

        var data = RandomNumberGenerator.GetBytes(100);

        using var response = await Http.PostAsync(post.Url, Form(post, data, "image/png"), Ct);

        Assert.True(response.IsSuccessStatusCode, $"form answered {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(Ct)}");

        var info = await store.GetInfoAsync("avatar.png", Ct);

        Assert.Equal(100, info.Size);
        Assert.Equal("image/png", info.ContentType);
        Assert.Equal("form", info.Metadata["owner"]);
    }

    [Fact]
    public async Task PresignedForm_HoldsTheUploadToItsSizeLimit()
    {
        var (store, _) = await server.CreateStoreAsync();
        using var _s = store;

        var post = await store.CreateUploadFormAsync("limited", new PresignedPostOptions { MinimumSize = 10, MaximumSize = 100 }, Ct);

        using var tooBig = await Http.PostAsync(post.Url, Form(post, new byte[101]), Ct);
        using var tooSmall = await Http.PostAsync(post.Url, Form(post, new byte[5]), Ct);

        Assert.Equal(HttpStatusCode.BadRequest, tooBig.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, tooSmall.StatusCode);
        Assert.False(await store.ExistsAsync("limited", Ct));

        using var justRight = await Http.PostAsync(post.Url, Form(post, new byte[100]), Ct);

        Assert.True(justRight.IsSuccessStatusCode);
        Assert.Equal(100, (await store.GetInfoAsync("limited", Ct)).Size);
    }

    [Fact]
    public async Task PresignedForm_HoldsTheUploadToItsKey()
    {
        var (store, _) = await server.CreateStoreAsync();
        using var _s = store;

        var post = await store.CreateUploadFormAsync("allowed", cancellationToken: Ct);

        // swap the key the form was signed for; the policy pins it
        var forged = new PresignedPost
        {
            Url = post.Url,
            ExpiresAt = post.ExpiresAt,
            Fields = post.Fields.ToDictionary(f => f.Key, f => f.Key == "key" ? "somewhere-else" : f.Value),
        };

        using var response = await Http.PostAsync(forged.Url, Form(forged, [1, 2, 3]), Ct);

        Assert.False(response.IsSuccessStatusCode);
        Assert.False(await store.ExistsAsync("somewhere-else", Ct));
    }

    [Fact]
    public async Task PresignedForm_HoldsTheUploadToItsContentType()
    {
        var (store, _) = await server.CreateStoreAsync();
        using var _s = store;

        var post = await store.CreateUploadFormAsync("typed", new PresignedPostOptions { ContentType = "image/png" }, Ct);

        var forged = new PresignedPost
        {
            Url = post.Url,
            ExpiresAt = post.ExpiresAt,
            Fields = post.Fields.ToDictionary(f => f.Key, f => f.Key == "Content-Type" ? "text/html" : f.Value),
        };

        using var response = await Http.PostAsync(forged.Url, Form(forged, [1, 2, 3]), Ct);

        Assert.False(response.IsSuccessStatusCode);
        Assert.False(await store.ExistsAsync("typed", Ct));
    }

    [Fact]
    public async Task SigningWithWrongCredentials_ProducesAUrlTheServerRejects()
    {
        var bucket = await server.CreateBucketAsync();
        using var store = server.CreateStore(bucket, s => s["SecretAccessKey"] = "not-the-real-secret");

        var download = await store.CreateDownloadUrlAsync("k", cancellationToken: Ct);

        // nothing says so at signing time: the secret is only ever checked by the server
        using var response = await SendAsync(download);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Expiry_BeyondTheStoresMaximum_IsRefused()
    {
        var (store, _) = await server.CreateStoreAsync();
        using var _s = store;

        var e = await Assert.ThrowsAsync<ObjectStoreException>(() => store.CreateDownloadUrlAsync("k", new PresignedDownloadOptions { Expiry = TimeSpan.FromHours(2) }, Ct));

        Assert.Equal(ObjectStoreErrorCode.InvalidRequest, e.ErrorCode);
    }
}
