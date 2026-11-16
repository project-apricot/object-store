# ApricotFramework.ObjectStore

[![NuGet](https://img.shields.io/nuget/v/ApricotFramework.ObjectStore.svg?label=ApricotFramework.ObjectStore)](https://www.nuget.org/packages/ApricotFramework.ObjectStore/)
[![NuGet](https://img.shields.io/nuget/v/ApricotFramework.ObjectStore.S3.svg?label=ApricotFramework.ObjectStore.S3)](https://www.nuget.org/packages/ApricotFramework.ObjectStore.S3/)
[![NuGet](https://img.shields.io/nuget/v/ApricotFramework.ObjectStore.DependencyInjection.svg?label=ApricotFramework.ObjectStore.DependencyInjection)](https://www.nuget.org/packages/ApricotFramework.ObjectStore.DependencyInjection/)
[![NuGet](https://img.shields.io/nuget/v/ApricotFramework.ObjectStore.ErrorDefinitions.svg?label=ApricotFramework.ObjectStore.ErrorDefinitions)](https://www.nuget.org/packages/ApricotFramework.ObjectStore.ErrorDefinitions/)
[![CI](https://github.com/project-apricot/object-store/actions/workflows/ci.yml/badge.svg)](https://github.com/project-apricot/object-store/actions/workflows/ci.yml)
[![License](https://img.shields.io/badge/license-Apache--2.0-blue.svg)](https://github.com/project-apricot/object-store/blob/main/LICENSE)

Object storage from .NET, configured the way a connection string is: named stores in one settings
section, each binding one bucket and a key prefix. AWS S3 and any S3-compatible server work today, and a
provider is a package, so adding another changes nothing about the stores you already have.

Callers address objects by key alone. The bucket, the endpoint, the prefix and the credentials stay in
configuration, so moving a store to another bucket is a settings change.

`ApricotFramework.ObjectStore` is the **zero-dependency** core, and nothing here needs ASP.NET Core — the
integration package depends only on `Microsoft.Extensions.*`, so a worker host can use it too.

## Install

```bash
dotnet add package ApricotFramework.ObjectStore
dotnet add package ApricotFramework.ObjectStore.S3
dotnet add package ApricotFramework.ObjectStore.DependencyInjection
dotnet add package ApricotFramework.ObjectStore.ErrorDefinitions   # optional
```

## Usage

```csharp
builder.Services.AddObjectStores(builder.Configuration);
builder.Services.AddS3ObjectStoreProvider();
```

```jsonc
"ObjectStore": {
  "DefaultStore": "uploads",
  "Stores": {
    "uploads": {
      "Provider": "s3",
      "Settings": { "Bucket": "acme-uploads", "Region": "eu-west-1", "Prefix": "svc-a" }
    },
    "local": {
      "Provider": "s3",
      "Settings": { "Bucket": "dev", "ServiceUrl": "http://localhost:9000" }
      // Credentials come from the environment, never from this file:
      // ObjectStore__Stores__local__Settings__AccessKeyId
      // ObjectStore__Stores__local__Settings__SecretAccessKey
    }
  }
}
```

Any setting can be overridden from the environment (`ObjectStore__Stores__uploads__Settings__Bucket`). With
no keys set, a store uses the AWS default credential chain, so a role or workload identity needs no
configuration at all.

```csharp
// the default store
app.MapPut("/files/{**key}", (IObjectStore store, string key, Stream body, CancellationToken ct) =>
    store.PutAsync(key, body, new ObjectPutOptions { ContentType = "text/plain" }, ct));

// a named store
app.MapGet("/avatar", ([FromKeyedServices("avatars")] IObjectStore avatars) => ...);

// or by name at runtime
var store = factory.GetStore("uploads");
```

A browser or a mobile app can upload and download without credentials or a trip through your service:

```csharp
var upload = await store.CreateUploadUrlAsync("reports/q3.pdf", new PresignedUploadOptions { ContentType = "application/pdf" });
// hand upload.Url and upload.RequiredHeaders to the client; it sends them exactly as given

var form = await store.CreateUploadFormAsync("avatars/42.png", new PresignedPostOptions { MaximumSize = 1_000_000, ContentType = "image/png" });
// the only presigned upload the store itself holds to a size
```

Failures are exceptions of this library's own, with the vendor's error kept inside:

```csharp
try { await store.GetAsync("missing.txt"); }
catch (ObjectNotFoundException) { /* ... */ }
catch (ObjectStoreException e) when (e.IsTransient()) { /* throttled, unreachable or timed out: retry */ }
```

With `ApricotFramework.ErrorDefinitions` in the host, `services.AddObjectStoreErrorDefinitions()` reports
them as RFC 9457 problem+json — a missing object is a 404, a throttled store a 503 — without naming a
bucket, an endpoint or a key.

Full documentation: <https://projectapricot.dev/docs/object-store>
