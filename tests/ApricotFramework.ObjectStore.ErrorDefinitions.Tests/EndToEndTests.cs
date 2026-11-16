using System.Net;
using System.Text.Json;
using ApricotFramework.ErrorDefinitions.AspNetCore.Extensions;
using ApricotFramework.ObjectStore.ErrorDefinitions.Extensions;
using ApricotFramework.ObjectStore.Exceptions;
using ApricotFramework.ObjectStore.Impl;
using ApricotFramework.ObjectStore.Model;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ApricotFramework.ObjectStore.ErrorDefinitions.Tests;

/// <summary>
/// The whole path a host wires up: a failing store call, the exception handler, and what a client reads.
/// </summary>
public class EndToEndTests
{
    private static async Task<IHost> StartAsync()
    {
        var store = new MemoryObjectStore("internal-store-label");

        var host = await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddErrorDefinitions();
                    services.AddObjectStoreErrorDefinitions();
                })
                .Configure(app =>
                {
                    app.UseExceptionHandler();
                    app.Run(async context =>
                    {
                        var key = context.Request.Query["key"].ToString();

                        switch (context.Request.Path.Value)
                        {
                            case "/get":
                                await store.GetAsync(key, cancellationToken: context.RequestAborted);

                                break;
                            case "/create-twice":
                                await store.PutAsync(key, new MemoryStream([1]), new ObjectPutOptions { IfNoneMatch = "*" }, context.RequestAborted);
                                await store.PutAsync(key, new MemoryStream([1]), new ObjectPutOptions { IfNoneMatch = "*" }, context.RequestAborted);

                                break;
                            case "/unavailable":
                                throw new ObjectStoreException(ObjectStoreErrorCode.Throttled, "SlowDown from http://10.1.2.3:9000", "internal-store-label", "s3", key);
                            case "/denied":
                                throw new ObjectStoreException(ObjectStoreErrorCode.AccessDenied, "InvalidAccessKeyId AKIA-SECRET", "internal-store-label", "s3", key);
                            case "/presign":
                                await store.CreateDownloadUrlAsync(key, cancellationToken: context.RequestAborted);

                                break;
                        }

                        context.Response.StatusCode = 204;
                    });
                }))
            .StartAsync(TestContext.Current.CancellationToken);

        return host;
    }

    private static async Task<(HttpStatusCode Status, JsonElement Body, string Raw)> CallAsync(IHost host, string path)
    {
        using var response = await host.GetTestClient().GetAsync(path, TestContext.Current.CancellationToken);
        var raw = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        return (response.StatusCode, raw.Length > 0 ? JsonDocument.Parse(raw).RootElement.Clone() : default, raw);
    }

    private static string FirstCode(JsonElement body) => body.GetProperty("errors")[0].GetProperty("code").GetString()!;

    [Fact]
    public async Task MissingObject_Is404_WithTheNotFoundCode()
    {
        using var host = await StartAsync();

        var (status, body, _) = await CallAsync(host, "/get?key=nope");

        Assert.Equal(HttpStatusCode.NotFound, status);
        Assert.Equal("OBJECT_NOT_FOUND", FirstCode(body));
        Assert.Equal("not_found", body.GetProperty("errors")[0].GetProperty("kind").GetString());
    }

    [Fact]
    public async Task CreateOnlyConflict_Is409()
    {
        using var host = await StartAsync();

        var (status, body, _) = await CallAsync(host, "/create-twice?key=once");

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal("OBJECT_ALREADY_EXISTS", FirstCode(body));
    }

    [Fact]
    public async Task InvalidKey_Is400()
    {
        using var host = await StartAsync();

        var (status, body, _) = await CallAsync(host, "/get?key=" + Uri.EscapeDataString("../etc/passwd"));

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("OBJECT_KEY_INVALID", FirstCode(body));
    }

    [Fact]
    public async Task ThrottledStore_Is503_AndRetryable()
    {
        using var host = await StartAsync();

        var (status, body, _) = await CallAsync(host, "/unavailable?key=k");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
        Assert.Equal("OBJECT_STORE_UNAVAILABLE", FirstCode(body));
        Assert.True(body.GetProperty("errors")[0].GetProperty("payload").GetProperty("retryable").GetBoolean());
    }

    [Fact]
    public async Task OurOwnCredentialsBeingRefused_Is500_NotA403TheClientCannotFix()
    {
        using var host = await StartAsync();

        var (status, body, _) = await CallAsync(host, "/denied?key=k");

        Assert.Equal(HttpStatusCode.InternalServerError, status);
        Assert.Equal("OBJECT_STORE_FAILED", FirstCode(body));
    }

    [Fact]
    public async Task ProviderWithoutPresigning_Is500()
    {
        using var host = await StartAsync();

        var (status, body, _) = await CallAsync(host, "/presign?key=k");

        Assert.Equal(HttpStatusCode.InternalServerError, status);
        Assert.Equal("OBJECT_STORE_FAILED", FirstCode(body));
    }

    [Theory]
    [InlineData("/get?key=private/user-42/passport.png")]
    [InlineData("/unavailable?key=private/user-42/passport.png")]
    [InlineData("/denied?key=private/user-42/passport.png")]
    public async Task NothingInternalReachesTheResponseBody(string path)
    {
        using var host = await StartAsync();

        var (_, _, raw) = await CallAsync(host, path);

        foreach (var secret in new[] { "internal-store-label", "user-42", "passport", "10.1.2.3", "AKIA-SECRET", "InvalidAccessKeyId", "SlowDown" })
        {
            Assert.DoesNotContain(secret, raw, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task AnUnrelatedException_IsLeftToTheOtherMappers()
    {
        using var host = await new HostBuilder()
            .ConfigureWebHost(web => web
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddErrorDefinitions();
                    services.AddObjectStoreErrorDefinitions();
                    services.MapExceptionToError<TimeoutException>("timeout", "UPSTREAM_TIMED_OUT");
                })
                .Configure(app =>
                {
                    app.UseExceptionHandler();
                    app.Run(_ => throw new TimeoutException());
                }))
            .StartAsync(TestContext.Current.CancellationToken);

        var (status, body, _) = await CallAsync(host, "/");

        Assert.Equal(HttpStatusCode.GatewayTimeout, status);
        Assert.Equal("UPSTREAM_TIMED_OUT", FirstCode(body));
    }
}
