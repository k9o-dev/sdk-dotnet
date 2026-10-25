using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace K9o;

/// <summary>Policy bundle lifecycle namespace — client.Policies.</summary>
public sealed class Policies
{
    private readonly K9oClient _client;
    internal Policies(K9oClient client) => _client = client;

    private string RequireProjectSlug()
    {
        if (string.IsNullOrEmpty(_client.Config.ProjectSlug))
            throw new KError("K9O_MISSING_PROJECT_CONTEXT", "policies operations require ProjectSlug bound on the client");
        return _client.Config.ProjectSlug!;
    }

    public async Task<Envelope<PolicyBundle?>> CurrentAsync(CancellationToken ct = default)
    {
        var slug = RequireProjectSlug();
        return await HttpTransport.RequestAsync<PolicyBundle?>(_client.Config, HttpMethod.Get,
            UrlGrammar.ProjectPath(slug, "policy"), null, null, ct).ConfigureAwait(false);
    }

    public async Task<Envelope<List<PolicyBundle>>> ListAsync(string? status = null, CancellationToken ct = default)
    {
        var slug = RequireProjectSlug();
        var q = status != null ? new Dictionary<string, string> { ["status"] = status } : null;
        return await HttpTransport.RequestAsync<List<PolicyBundle>>(_client.Config, HttpMethod.Get,
            UrlGrammar.ProjectPath(slug, "policies"), null, q, ct).ConfigureAwait(false);
    }

    public async Task<Envelope<PolicyBundle>> CreateAsync(string name, Dictionary<string, object?> rules, CancellationToken ct = default)
    {
        var slug = RequireProjectSlug();
        return await HttpTransport.RequestAsync<PolicyBundle>(_client.Config, HttpMethod.Post,
            UrlGrammar.ProjectPath(slug, "policies"), new Dictionary<string, object?> { ["name"] = name, ["rules"] = rules }, null, ct).ConfigureAwait(false);
    }

    public async Task<Envelope<Dictionary<string, object?>>> ValidateAsync(Dictionary<string, object?> rules, CancellationToken ct = default)
    {
        var slug = RequireProjectSlug();
        return await HttpTransport.RequestAsync<Dictionary<string, object?>>(_client.Config, HttpMethod.Post,
            UrlGrammar.ProjectPath(slug, "policies/validate"), new Dictionary<string, object?> { ["rules"] = rules }, null, ct).ConfigureAwait(false);
    }

    public async Task<Envelope<PolicyBundle>> GetAsync(string bundleId, CancellationToken ct = default)
    {
        var slug = RequireProjectSlug();
        return await HttpTransport.RequestAsync<PolicyBundle>(_client.Config, HttpMethod.Get,
            UrlGrammar.ProjectPath(slug, $"policies/{HttpUtility.UrlEncode(bundleId)}"), null, null, ct).ConfigureAwait(false);
    }

    public async Task<Envelope<PolicyBundle>> UpdateAsync(string bundleId, Dictionary<string, object?> patch, CancellationToken ct = default)
    {
        var slug = RequireProjectSlug();
        return await HttpTransport.RequestAsync<PolicyBundle>(_client.Config, HttpMethod.Patch,
            UrlGrammar.ProjectPath(slug, $"policies/{HttpUtility.UrlEncode(bundleId)}"), patch, null, ct).ConfigureAwait(false);
    }

    public async Task<Envelope<Dictionary<string, object?>>> DeleteAsync(string bundleId, CancellationToken ct = default)
    {
        var slug = RequireProjectSlug();
        return await HttpTransport.RequestAsync<Dictionary<string, object?>>(_client.Config, HttpMethod.Delete,
            UrlGrammar.ProjectPath(slug, $"policies/{HttpUtility.UrlEncode(bundleId)}"), null, null, ct).ConfigureAwait(false);
    }

    public async Task<Envelope<PolicyBundle>> PublishAsync(string bundleId, CancellationToken ct = default)
    {
        var slug = RequireProjectSlug();
        return await HttpTransport.RequestAsync<PolicyBundle>(_client.Config, HttpMethod.Post,
            UrlGrammar.ProjectPath(slug, $"policies/{HttpUtility.UrlEncode(bundleId)}/publish"), null, null, ct).ConfigureAwait(false);
    }
}

public sealed class PolicyBundle
{
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("version")] public int Version { get; init; }
    [JsonPropertyName("status")] public string Status { get; init; } = "";
    [JsonPropertyName("rules")] public Dictionary<string, object?>? Rules { get; init; }
    [JsonPropertyName("createdAt")] public string CreatedAt { get; init; } = "";
    [JsonPropertyName("publishedAt")] public string? PublishedAt { get; init; }
    [JsonPropertyName("createdBy")] public string CreatedBy { get; init; } = "";
}
