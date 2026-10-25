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

    public async Task<Envelope<FunctionRecord>> DeployAsync(DeployFunctionInput input, CancellationToken ct = default)
    {
        var slug = RequireProjectSlug();
        return await HttpTransport.RequestAsync<FunctionRecord>(_client.Config, HttpMethod.Post,
            UrlGrammar.ProjectPath(slug, "functions"), input, null, ct).ConfigureAwait(false);
    }

    public async Task<Envelope<List<FunctionRecord>>> ListAsync(string? status = null, CancellationToken ct = default)
    {
        var slug = RequireProjectSlug();
        var q = status != null ? new Dictionary<string, string> { ["status"] = status } : null;
        return await HttpTransport.RequestAsync<List<FunctionRecord>>(_client.Config, HttpMethod.Get,
            UrlGrammar.ProjectPath(slug, "functions"), null, q, ct).ConfigureAwait(false);
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

    /// <summary>Invoke a function; payload is passed to the runtime verbatim.</summary>
    public async Task<Envelope<Dictionary<string, object?>>> InvokeAsync(string name, object payload, CancellationToken ct = default)
    {
        var slug = RequireProjectSlug();
        return await HttpTransport.RequestAsync<Dictionary<string, object?>>(_client.Config, HttpMethod.Post,
            UrlGrammar.ProjectPath(slug, $"functions/{HttpUtility.UrlEncode(name)}/invoke"), payload, null, ct).ConfigureAwait(false);
    }
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

public sealed class DeployFunctionInput
{
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("runtime")] public required string Runtime { get; init; }
    [JsonPropertyName("code")] public required string Code { get; init; }
    [JsonPropertyName("tier")] public string? Tier { get; init; }
    [JsonPropertyName("config")] public Dictionary<string, object?>? Config { get; init; }
}
