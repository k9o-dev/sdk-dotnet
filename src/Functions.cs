using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace K9o;

/// <summary>Function lifecycle namespace — client.Functions.</summary>
public sealed class Functions
{
    private readonly K9oClient _client;
    internal Functions(K9oClient client) => _client = client;

    private string RequireProjectSlug()
    {
        if (string.IsNullOrEmpty(_client.Config.ProjectSlug))
            throw new KError("K9O_MISSING_PROJECT_CONTEXT", "functions operations require ProjectSlug bound on the client");
        return _client.Config.ProjectSlug!;
    }

    /// <summary>
    /// Upload a function artifact and get back its content-addressed <c>artifactKey</c>, to pass to
    /// <see cref="DeployAsync"/>. <c>POST /@&lt;projectSlug&gt;/functions/artifacts</c> with the raw bytes: a WebAssembly
    /// module for the gateway tier (<paramref name="kind"/> "wasm", up to 16 MiB) or an OCI image archive for the
    /// sandbox tier (<paramref name="kind"/> "tar", up to 256 MiB). Uploading the same bytes again returns the same key.
    /// </summary>
    public async Task<Envelope<ArtifactRecord>> UploadArtifactAsync(byte[] bytes, string kind, CancellationToken ct = default)
    {
        var slug = RequireProjectSlug();
        var contentType = kind switch
        {
            "wasm" => "application/wasm",
            "tar" => "application/x-tar",
            _ => throw new KError("K9O_INVALID_FIELD_VALUE", "kind must be \"wasm\" or \"tar\"",
                new Dictionary<string, object?> { ["field"] = "kind", ["reason"] = "unsupported" }, null, null, null),
        };
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        return await HttpTransport.RequestAsync<ArtifactRecord>(_client.Config, HttpMethod.Post,
            UrlGrammar.ProjectPath(slug, "functions/artifacts"), content, null, ct).ConfigureAwait(false);
    }

    /// <summary>Deploy or redeploy a function from an uploaded artifact. Requires PROJECT_ADMIN.</summary>
    public async Task<Envelope<FunctionRecord>> DeployAsync(DeployFunctionInput input, CancellationToken ct = default)
    {
        var slug = RequireProjectSlug();
        return await HttpTransport.RequestAsync<FunctionRecord>(_client.Config, HttpMethod.Post,
            UrlGrammar.ProjectPath(slug, "functions"), input, null, ct).ConfigureAwait(false);
    }

    /// <summary>List functions (cursor pagination); null arguments are not sent.</summary>
    public async Task<Envelope<List<FunctionRecord>>> ListAsync(string? status = null, int? limit = null, string? cursor = null, CancellationToken ct = default)
    {
        var slug = RequireProjectSlug();
        var q = new Dictionary<string, string>();
        if (status != null) q["status"] = status;
        if (limit != null) q["limit"] = limit.Value.ToString();
        if (cursor != null) q["cursor"] = cursor;
        return await HttpTransport.RequestAsync<List<FunctionRecord>>(_client.Config, HttpMethod.Get,
            UrlGrammar.ProjectPath(slug, "functions"), null, q.Count > 0 ? q : null, ct).ConfigureAwait(false);
    }

    public async Task<Envelope<FunctionRecord>> GetAsync(string name, CancellationToken ct = default)
    {
        var slug = RequireProjectSlug();
        return await HttpTransport.RequestAsync<FunctionRecord>(_client.Config, HttpMethod.Get,
            UrlGrammar.ProjectPath(slug, $"functions/{HttpUtility.UrlEncode(name)}"), null, null, ct).ConfigureAwait(false);
    }

    public async Task<Envelope<Dictionary<string, object?>>> DeleteAsync(string name, CancellationToken ct = default)
    {
        var slug = RequireProjectSlug();
        return await HttpTransport.RequestAsync<Dictionary<string, object?>>(_client.Config, HttpMethod.Delete,
            UrlGrammar.ProjectPath(slug, $"functions/{HttpUtility.UrlEncode(name)}"), null, null, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Invoke a function. <paramref name="payload"/> is sent as the <c>payload</c> field of the request body and
    /// reaches the runtime verbatim; <paramref name="options"/> carries the gateway's invoke controls.
    /// </summary>
    public async Task<Envelope<Dictionary<string, object?>>> InvokeAsync(string name, object? payload = null, InvokeOptions? options = null, CancellationToken ct = default)
    {
        var slug = RequireProjectSlug();
        var body = new Dictionary<string, object?>();
        if (payload != null) body["payload"] = payload;
        if (options?.Background != null) body["background"] = options.Background;
        if (options?.OnBehalfOf != null) body["onBehalfOf"] = options.OnBehalfOf;
        if (options?.ConfinedTo != null) body["confinedTo"] = options.ConfinedTo;
        return await HttpTransport.RequestAsync<Dictionary<string, object?>>(_client.Config, HttpMethod.Post,
            UrlGrammar.ProjectPath(slug, $"functions/{HttpUtility.UrlEncode(name)}/invoke"), body, null, ct).ConfigureAwait(false);
    }
}

/// <summary>Optional invoke controls; property names are the gateway's. Null fields are not sent.</summary>
public sealed class InvokeOptions
{
    /// <summary>Run asynchronously instead of waiting for the result.</summary>
    public bool? Background { get; init; }
    public string? OnBehalfOf { get; init; }
    public string? ConfinedTo { get; init; }
}

/// <summary>Result of <see cref="Functions.UploadArtifactAsync"/>.</summary>
public sealed class ArtifactRecord
{
    [JsonPropertyName("artifactKey")] public string ArtifactKey { get; init; } = "";
}

public sealed class FunctionRecord
{
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("runtime")] public string Runtime { get; init; } = "";
    [JsonPropertyName("status")] public string Status { get; init; } = "";
    [JsonPropertyName("tier")] public string Tier { get; init; } = "";
    [JsonPropertyName("createdAt")] public string CreatedAt { get; init; } = "";
    [JsonPropertyName("updatedAt")] public string? UpdatedAt { get; init; }
}

/// <summary>
/// Deploy input; property names are the gateway's. The function's bytes are uploaded first
/// (<see cref="Functions.UploadArtifactAsync"/> returns the <c>artifactKey</c>); deploy references that artifact.
/// <c>Runtime</c> is one of bun, python, go, java, kotlin, dotnet, swift, php, ruby, rust, tinygo, zig,
/// assemblyscript. <c>Tier</c> is <c>gateway</c> (WebAssembly in the gateway) or <c>sandbox</c> (an OCI image
/// archive). <c>Version</c> is a label for this deploy (1 to 64 characters).
/// </summary>
public sealed class DeployFunctionInput
{
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("runtime")] public required string Runtime { get; init; }
    [JsonPropertyName("tier")] public required string Tier { get; init; }
    [JsonPropertyName("artifactKey")] public required string ArtifactKey { get; init; }
    [JsonPropertyName("version")] public required string Version { get; init; }
    [JsonPropertyName("description")] public string? Description { get; init; }
}
