using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace K9o;

/// <summary>
/// <c>client.Storage</c> buckets and objects: per-project logical buckets and the objects in them,
/// against <c>/@&lt;projectSlug&gt;/storage/buckets…</c>. Flat methods here; <c>Storage.From(bucket)</c> returns a
/// handle for object operations.
///
/// Bucket names are logical: the platform resolves them to a physical store. Signed URLs require an explicit
/// <c>ExpiresIn</c> (1 to 604800 seconds, no default). Not offered, by design: a signed PUT (the gateway has no
/// route for it; uploads go through the multipart <see cref="StorageBucketHandle.UploadAsync(string, byte[], UploadOptions?, CancellationToken)"/>) and copy.
/// Errors pass through unchanged as <see cref="KError"/>. Every operation requires a bound <c>ProjectSlug</c>.
/// </summary>
public sealed class Storage
{
    private readonly K9oClient _client;
    internal Storage(K9oClient client) { _client = client; }

    /// <summary>List the project's buckets. <c>GET /@slug/storage/buckets</c>.</summary>
    public Task<Envelope<List<StorageBucket>>> ListBucketsAsync(CancellationToken ct = default) =>
        HttpTransport.RequestAsync<List<StorageBucket>>(_client.Config, HttpMethod.Get, BucketsPath(_client.Config), null, null, ct);

    /// <summary>Read one bucket. <c>GET …/buckets/{name}</c>.</summary>
    public Task<Envelope<StorageBucket>> GetBucketAsync(string name, CancellationToken ct = default) =>
        HttpTransport.RequestAsync<StorageBucket>(_client.Config, HttpMethod.Get, BucketPath(_client.Config, name), null, null, ct);

    /// <summary>
    /// Create a bucket. <c>POST …/buckets</c> with <c>{ name, ...options }</c>. A name outside 3 to 63 lowercase
    /// letters, digits and hyphens is refused by the gateway (<c>K9O_INVALID_BUCKET_NAME</c>).
    /// </summary>
    public Task<Envelope<StorageBucket>> CreateBucketAsync(string name, CreateBucketOptions? options = null, CancellationToken ct = default)
    {
        var path = BucketsPath(_client.Config);
        var o = options ?? new CreateBucketOptions();
        var body = new CreateBucketBody
        {
            Name = name,
            Public = o.Public,
            FileSizeLimit = o.FileSizeLimit,
            AllowedMimeTypes = o.AllowedMimeTypes,
            Versioning = o.Versioning,
            DeleteAfterDays = o.DeleteAfterDays,
            ByosBindingId = o.ByosBindingId,
        };
        return HttpTransport.RequestAsync<StorageBucket>(_client.Config, HttpMethod.Post, path, body, null, ct);
    }

    /// <summary>
    /// Change a bucket. <c>PATCH …/buckets/{name}</c>. Only the keys in <paramref name="patch"/> are sent; a
    /// <c>null</c> value clears a limit (<c>fileSizeLimit</c>, <c>deleteAfterDays</c>, <c>byosBindingId</c>) and
    /// <c>allowedMimeTypes: null</c> allows any type. Keys use the wire names (camelCase).
    /// </summary>
    public Task<Envelope<StorageBucket>> UpdateBucketAsync(string name, IReadOnlyDictionary<string, object?> patch, CancellationToken ct = default) =>
        HttpTransport.RequestAsync<StorageBucket>(_client.Config, HttpMethod.Patch, BucketPath(_client.Config, name), patch, null, ct);

    /// <summary>
    /// Delete a bucket. <c>DELETE …/buckets/{name}</c>. A non-empty bucket is refused (409 <c>bucket_not_empty</c>)
    /// unless <paramref name="empty"/> is true, which sends <c>?empty=true</c> and purges its objects first.
    /// </summary>
    public Task<Envelope<DeletedBucket>> DeleteBucketAsync(string name, bool empty = false, CancellationToken ct = default) =>
        HttpTransport.RequestAsync<DeletedBucket>(_client.Config, HttpMethod.Delete, BucketPath(_client.Config, name), null,
            empty ? new Dictionary<string, string> { ["empty"] = "true" } : null, ct);

    /// <summary>A handle on one bucket for object operations: <c>client.Storage.From("avatars").UploadAsync(...)</c>.</summary>
    public StorageBucketHandle From(string bucket) => new(_client, bucket);

    internal static string BucketsPath(HttpClientConfig config, string suffix = "") =>
        UrlGrammar.ProjectPath(Studio.RequireProjectSlug(config, "storage"), "storage/buckets" + suffix);

    internal static string BucketPath(HttpClientConfig config, string name, string suffix = "")
    {
        // Slug guard first, so a missing project is reported before a bad name.
        Studio.RequireProjectSlug(config, "storage");
        if (string.IsNullOrEmpty(name)) throw InvalidField("bucket", "bucket name must not be empty", "empty");
        // `.` and `..` survive percent-encoding and are collapsed as dot segments by Uri BEFORE the request leaves, so a
        // bucket named `..` would address a different route. No bucket can have that name (3 to 63 chars): refuse it.
        if (name == "." || name == "..")
            throw InvalidField("bucket", "bucket name must not be a dot segment", "dot_segment");
        return BucketsPath(config, "/" + Uri.EscapeDataString(name) + suffix);
    }

    internal static KError InvalidField(string field, string message, string reason, string? recommendation = null) =>
        new("K9O_INVALID_FIELD_VALUE", message,
            new Dictionary<string, object?> { ["field"] = field, ["reason"] = reason }, recommendation, null, null);

    private sealed class CreateBucketBody
    {
        [JsonPropertyName("name")] public string Name { get; init; } = "";
        [JsonPropertyName("public")] public bool? Public { get; init; }
        [JsonPropertyName("fileSizeLimit")] public long? FileSizeLimit { get; init; }
        [JsonPropertyName("allowedMimeTypes")] public List<string>? AllowedMimeTypes { get; init; }
        [JsonPropertyName("versioning")] public bool? Versioning { get; init; }
        [JsonPropertyName("deleteAfterDays")] public int? DeleteAfterDays { get; init; }
        [JsonPropertyName("byosBindingId")] public string? ByosBindingId { get; init; }
    }
}

/// <summary>A handle on one bucket (<see cref="Storage.From"/>).</summary>
public sealed class StorageBucketHandle
{
    /// <summary>The largest <c>ExpiresIn</c> a signed URL may carry (7 days).</summary>
    public const long MaxExpiresIn = 604_800;

    private readonly K9oClient _client;
    private readonly string _bucket;
    internal StorageBucketHandle(K9oClient client, string bucket) { _client = client; _bucket = bucket; }

    /// <summary>
    /// Upload an object. <c>POST …/objects</c> as <c>multipart/form-data</c> with parts <c>file</c>, <c>path</c>,
    /// optional <c>contentType</c>, <c>upsert</c> and <c>metadata</c>. A string is sent as UTF-8.
    /// </summary>
    public Task<Envelope<StorageObject>> UploadAsync(string path, string data, UploadOptions? options = null, CancellationToken ct = default) =>
        UploadCoreAsync(path, new ByteArrayContent(Encoding.UTF8.GetBytes(data)), options, ct);

    /// <summary>Upload raw bytes, sent as given.</summary>
    public Task<Envelope<StorageObject>> UploadAsync(string path, byte[] data, UploadOptions? options = null, CancellationToken ct = default) =>
        UploadCoreAsync(path, new ByteArrayContent(data), options, ct);

    /// <summary>Upload a stream, read from its current position. The stream is not disposed.</summary>
    public Task<Envelope<StorageObject>> UploadAsync(string path, Stream data, UploadOptions? options = null, CancellationToken ct = default) =>
        UploadCoreAsync(path, new StreamContent(data), options, ct);

    private async Task<Envelope<StorageObject>> UploadCoreAsync(string path, HttpContent file, UploadOptions? options, CancellationToken ct)
    {
        var cfg = _client.Config;
        var url = Storage.BucketPath(cfg, _bucket, "/objects");
        RequireObjectPath(path);
        var o = options ?? new UploadOptions();
        using var form = new MultipartFormDataContent();
        if (o.ContentType != null) file.Headers.ContentType = MediaTypeHeaderValue.Parse(o.ContentType);
        var slash = path.LastIndexOf('/');
        var leaf = path[(slash + 1)..];
        form.Add(file, "file", leaf.Length == 0 ? "file" : leaf);
        AddText(form, "path", path);
        if (o.ContentType != null) AddText(form, "contentType", o.ContentType);
        AddText(form, "upsert", o.Upsert ? "true" : "false");
        if (o.Metadata != null) AddText(form, "metadata", JsonSerializer.Serialize(o.Metadata));
        // The multipart content is passed through as-is: the transport must not replace it with JSON.
        return await HttpTransport.RequestAsync<StorageObject>(cfg, HttpMethod.Post, url, form, null, ct).ConfigureAwait(false);
    }

    private static void AddText(MultipartFormDataContent form, string name, string value)
    {
        var part = new StringContent(value, Encoding.UTF8);
        part.Headers.ContentType = null;
        form.Add(part, name);
    }

    /// <summary>
    /// A pre-authenticated GET URL for one object. <c>POST …/objects/sign</c>. <c>ExpiresIn</c> is required, an
    /// integer from 1 to 604800 seconds, and is checked before any request is made.
    /// </summary>
    public Task<Envelope<SignedUrl>> CreateSignedUrlAsync(string path, SignedUrlOptions options, CancellationToken ct = default)
    {
        var url = Storage.BucketPath(_client.Config, _bucket, "/objects/sign");
        RequireObjectPath(path);
        if (options == null || options.ExpiresIn < 1 || options.ExpiresIn > MaxExpiresIn)
            throw Storage.InvalidField("expiresIn",
                $"expiresIn is required and must be an integer from 1 to {MaxExpiresIn} seconds", "out_of_range",
                "Pass an explicit ExpiresIn; signed URLs have no default lifetime.");
        var body = new SignBody { Path = path, ExpiresIn = options.ExpiresIn, Download = options.Download };
        return HttpTransport.RequestAsync<SignedUrl>(_client.Config, HttpMethod.Post, url, body, null, ct);
    }

    /// <summary>List objects, optionally under a prefix. <c>GET …/objects?prefix=</c>.</summary>
    public Task<Envelope<List<StorageObject>>> ListAsync(string? prefix = null, CancellationToken ct = default) =>
        HttpTransport.RequestAsync<List<StorageObject>>(_client.Config, HttpMethod.Get,
            Storage.BucketPath(_client.Config, _bucket, "/objects"), null,
            prefix != null ? new Dictionary<string, string> { ["prefix"] = prefix } : null, ct);

    /// <summary>Delete one object. <c>DELETE …/objects?path=</c>. The path is sent as given (the server decides).</summary>
    public Task<Envelope<RemovedObject>> RemoveAsync(string path, CancellationToken ct = default)
    {
        var url = Storage.BucketPath(_client.Config, _bucket, "/objects");
        RequireObjectPath(path);
        return HttpTransport.RequestAsync<RemovedObject>(_client.Config, HttpMethod.Delete, url, null,
            new Dictionary<string, string> { ["path"] = path }, ct);
    }

    private static void RequireObjectPath(string path)
    {
        if (string.IsNullOrEmpty(path)) throw Storage.InvalidField("path", "object path must not be empty", "empty");
    }

    private sealed class SignBody
    {
        [JsonPropertyName("path")] public string Path { get; init; } = "";
        [JsonPropertyName("expiresIn")] public long ExpiresIn { get; init; }
        [JsonPropertyName("download")] public bool? Download { get; init; }
    }
}

/// <summary>A bucket.</summary>
public sealed class StorageBucket
{
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    /// <summary>True removes the authentication requirement on GET; per-object access policies still apply.</summary>
    [JsonPropertyName("public")] public bool Public { get; init; }
    /// <summary>Bytes; null = no limit.</summary>
    [JsonPropertyName("fileSizeLimit")] public long? FileSizeLimit { get; init; }
    [JsonPropertyName("allowedMimeTypes")] public List<string> AllowedMimeTypes { get; init; } = new();
    [JsonPropertyName("versioning")] public bool Versioning { get; init; }
    [JsonPropertyName("deleteAfterDays")] public int? DeleteAfterDays { get; init; }
    [JsonPropertyName("createdAt")] public string CreatedAt { get; init; } = "";
    [JsonPropertyName("updatedAt")] public string UpdatedAt { get; init; } = "";
    [JsonPropertyName("system")] public bool System { get; init; }
    [JsonPropertyName("byosBindingId")] public string? ByosBindingId { get; init; }
}

/// <summary>Options for <see cref="Storage.CreateBucketAsync"/>; unset fields are omitted from the body.</summary>
public sealed class CreateBucketOptions
{
    public bool? Public { get; init; }
    public long? FileSizeLimit { get; init; }
    public List<string>? AllowedMimeTypes { get; init; }
    public bool? Versioning { get; init; }
    public int? DeleteAfterDays { get; init; }
    /// <summary>Link the bucket to one of the project's storage locations.</summary>
    public string? ByosBindingId { get; init; }
}

/// <summary>An object listing entry; <c>Path</c> is relative to the bucket.</summary>
public sealed class StorageObject
{
    [JsonPropertyName("path")] public string Path { get; init; } = "";
    [JsonPropertyName("size")] public long Size { get; init; }
    [JsonPropertyName("lastModified")] public string? LastModified { get; init; }
}

public sealed class UploadOptions
{
    /// <summary>MIME type of the object; sent as the file part's Content-Type and as the <c>contentType</c> part.</summary>
    public string? ContentType { get; init; }
    /// <summary>False (default) refuses to overwrite an existing path (409 <c>K9O_ALREADY_EXISTS</c>).</summary>
    public bool Upsert { get; init; }
    /// <summary>Caller-defined string metadata: at most 10 keys and 2 KB in total (enforced by the gateway).</summary>
    public Dictionary<string, string>? Metadata { get; init; }
}

public sealed class SignedUrlOptions
{
    /// <summary>Seconds, required: 1 to 604800 (7 days). There is deliberately no default.</summary>
    public required long ExpiresIn { get; init; }
    /// <summary>True makes the download an attachment.</summary>
    public bool? Download { get; init; }
}

public sealed class SignedUrl
{
    [JsonPropertyName("signedUrl")] public string Url { get; init; } = "";
    [JsonPropertyName("expiresIn")] public long ExpiresIn { get; init; }
}

public sealed class RemovedObject
{
    [JsonPropertyName("bucket")] public string Bucket { get; init; } = "";
    [JsonPropertyName("path")] public string Path { get; init; } = "";
    [JsonPropertyName("deleted")] public bool Deleted { get; init; }
}

public sealed class DeletedBucket
{
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("deleted")] public bool Deleted { get; init; }
    [JsonPropertyName("objectsDeleted")] public long ObjectsDeleted { get; init; }
}
