using ApricotFramework.ErrorDefinitions.AspNetCore.Extensions;
using ApricotFramework.ObjectStore;
using ApricotFramework.ObjectStore.DependencyInjection.Extensions;
using ApricotFramework.ObjectStore.ErrorDefinitions.Extensions;
using ApricotFramework.ObjectStore.Examples.Web;
using ApricotFramework.ObjectStore.Model;
using ApricotFramework.ObjectStore.S3.Extensions;

var builder = WebApplication.CreateBuilder(args);

// the pattern worth copying: one AddObjectStores plus the providers this host is willing to use. No
// provider is registered for you, so a store can never quietly resolve to something you did not choose.
builder.Services.AddObjectStores(builder.Configuration);
builder.Services.AddS3ObjectStoreProvider();

// registers one instance under both its interface and its own type; nothing registers it for you,
// because a store that forgets everything on restart should be a deliberate choice
builder.Services.AddMemoryObjectStoreProvider();

// a failed call becomes problem+json with a code a client can render, and never names a bucket or a key
builder.Services.AddErrorDefinitions();
builder.Services.AddObjectStoreErrorDefinitions();

var app = builder.Build();

app.UseExceptionHandler();

// every endpoint takes ?store=name to pick a store; without it, the default one answers
static IObjectStore Pick(IObjectStoreFactory factory, string? store)
{
    return store is null ? factory.GetDefaultStore() : factory.GetStore(store);
}

// the unkeyed IObjectStore is the default store: the shortest way to a store, for code that only has one
app.MapPut("/files/{**key}", async (HttpRequest request, string key, IObjectStore store, CancellationToken cancellationToken) =>
{
    // the body is streamed, not buffered, and a large one goes up as a multipart upload automatically
    var info = await store.PutAsync(key, request.Body, new ObjectPutOptions { ContentType = request.ContentType }, cancellationToken);

    return Results.Ok(new { info.Key, info.Size, info.ETag });
});

app.MapGet("/files/{**key}", async (HttpContext context, IObjectStoreFactory factory, string key, string? store, CancellationToken cancellationToken) =>
{
    // a missing object throws ObjectNotFoundException, which the error-definitions mapper turns into a 404
    var stored = await Pick(factory, store).GetAsync(key, cancellationToken: cancellationToken);

    // ties the object, and the connection behind it, to the end of the response
    context.Response.RegisterForDispose(stored);

    return Results.Stream(stored.Content, stored.Info.ContentType ?? "application/octet-stream");
});

app.MapDelete("/files/{**key}", async (IObjectStoreFactory factory, string key, string? store, CancellationToken cancellationToken) =>
{
    await Pick(factory, store).DeleteAsync(key, cancellationToken);

    return Results.NoContent();
});

// the key arrives in the query string here, which a web server does not normalize the way it does a path,
// so this is where a hostile key actually reaches the library
app.MapGet("/info", async (IObjectStoreFactory factory, string? store, string key, CancellationToken cancellationToken) =>
{
    var info = await Pick(factory, store).GetInfoAsync(key, cancellationToken);

    return Results.Ok(new { info.Key, info.Size, info.ContentType, info.ETag, info.LastModified });
});

app.MapGet("/files", async (IObjectStoreFactory factory, string? store, string? prefix, CancellationToken cancellationToken) =>
{
    var items = new List<object>();

    // pages are fetched as this is read, so a large listing never sits in memory
    await foreach (var item in Pick(factory, store).ListAsync(prefix, cancellationToken))
    {
        items.Add(new { item.Key, item.Size, item.LastModified });
    }

    return items;
});

// a keyed registration by name: [FromKeyedServices("avatars")] resolves the store configured as "avatars"
app.MapPut("/avatars/{userId}", async ([FromKeyedServices("avatars")] IObjectStore avatars, HttpRequest request, string userId, CancellationToken cancellationToken) =>
{
    var info = await avatars.PutAsync($"{userId}.png", request.Body, new ObjectPutOptions { ContentType = "image/png" }, cancellationToken);

    return Results.Ok(new { info.Key, info.Size });
});

// Presigned URLs. These need an S3 store, since there is nothing to sign for in memory: pick one with
// ?store=minio after uncommenting it in appsettings.json. Against the memory store this answers 500
// OBJECT_STORE_FAILED, which is the honest answer for a service whose own configuration cannot do it.
app.MapPost("/presign/upload", async (IObjectStoreFactory factory, string? store, string key, string? contentType, CancellationToken cancellationToken) =>
{
    var signed = await Pick(factory, store).CreateUploadUrlAsync(key, new PresignedUploadOptions { ContentType = contentType }, cancellationToken);

    // the client must send RequiredHeaders exactly: they are part of what was signed. The URL is a bearer
    // credential until it expires, so this goes to the person it was made for and nowhere else.
    return Results.Ok(new { Url = signed.Url.AbsoluteUri, signed.Method, signed.RequiredHeaders, signed.ExpiresAt });
});

app.MapPost("/presign/download", async (IObjectStoreFactory factory, string? store, string key, string? filename, CancellationToken cancellationToken) =>
{
    var options = new PresignedDownloadOptions
    {
        ContentDisposition = filename is null ? null : $"attachment; filename=\"{filename}\"",
    };

    var signed = await Pick(factory, store).CreateDownloadUrlAsync(key, options, cancellationToken);

    return Results.Ok(new { Url = signed.Url.AbsoluteUri, signed.Method, signed.ExpiresAt });
});

// A form is the only presigned upload the store itself holds to a size and a content type.
app.MapPost("/presign/form", async (IObjectStoreFactory factory, string? store, string key, long maxBytes, string? contentType, CancellationToken cancellationToken) =>
{
    var form = await Pick(factory, store).CreateUploadFormAsync(key, new PresignedPostOptions { MaximumSize = maxBytes, ContentType = contentType }, cancellationToken);

    return Results.Ok(new { Url = form.Url.AbsoluteUri, form.Fields, form.ExpiresAt });
});

// Presigned multipart, for a client uploading something too large for one request: the service starts the
// upload and signs one URL per part, the client sends the parts straight to the store, and the service
// completes it with the ETags the client reports back.
app.MapPost("/multipart/start", async (IObjectStoreFactory factory, string? store, string key, string? contentType, CancellationToken cancellationToken) =>
{
    var upload = await Pick(factory, store).StartMultipartUploadAsync(key, new MultipartUploadOptions { ContentType = contentType }, cancellationToken);

    return Results.Ok(upload);
});

app.MapPost("/multipart/part-url", async (IObjectStoreFactory factory, string? store, string key, string uploadId, int part, CancellationToken cancellationToken) =>
{
    var signed = await Pick(factory, store).CreatePartUploadUrlAsync(new MultipartUpload(key, uploadId), part, cancellationToken: cancellationToken);

    return Results.Ok(new { Url = signed.Url.AbsoluteUri, signed.Method, signed.ExpiresAt });
});

app.MapPost("/multipart/complete", async (IObjectStoreFactory factory, string? store, CompleteRequest body, CancellationToken cancellationToken) =>
{
    var parts = body.Parts.Select(p => new UploadedPart(p.PartNumber, p.ETag));
    var info = await Pick(factory, store).CompleteMultipartUploadAsync(new MultipartUpload(body.Key, body.UploadId), parts, cancellationToken);

    return Results.Ok(new { info.Key, info.Size, info.ETag });
});

app.MapPost("/multipart/abort", async (IObjectStoreFactory factory, string? store, string key, string uploadId, CancellationToken cancellationToken) =>
{
    // an upload nobody finishes is kept, and billed, until it is aborted
    await Pick(factory, store).AbortMultipartUploadAsync(new MultipartUpload(key, uploadId), cancellationToken);

    return Results.NoContent();
});

app.Run();
