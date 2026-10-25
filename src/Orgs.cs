using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace K9o;

/// <summary>
/// Org lifecycle namespace (collection-style) — client.Orgs.
/// Distinct from client.Org (singular bound-org namespace).
/// </summary>
public sealed class Orgs
{
    private readonly K9oClient _client;
    internal Orgs(K9oClient client) => _client = client;

    public async Task<Envelope<List<Org_>>> ListAsync(CancellationToken ct = default) =>
        await HttpTransport.RequestAsync<List<Org_>>(_client.Config, HttpMethod.Get, UrlGrammar.OrgPath("orgs"), null, null, ct).ConfigureAwait(false);

    public async Task<Envelope<Org_>> GetAsync(string orgSlug, CancellationToken ct = default) =>
        await HttpTransport.RequestAsync<Org_>(_client.Config, HttpMethod.Get, UrlGrammar.OrgPath($"orgs/{HttpUtility.UrlEncode(orgSlug)}"), null, null, ct).ConfigureAwait(false);

    public async Task<Envelope<Org_>> RestoreAsync(string orgId, CancellationToken ct = default) =>
        await HttpTransport.RequestAsync<Org_>(_client.Config, HttpMethod.Post, UrlGrammar.OrgPath($"orgs/{HttpUtility.UrlEncode(orgId)}/restore"), null, null, ct).ConfigureAwait(false);

    public async Task<Envelope<Org_>> CurrentAsync(CancellationToken ct = default) =>
        await HttpTransport.RequestAsync<Org_>(_client.Config, HttpMethod.Get, UrlGrammar.OrgPath("org"), null, null, ct).ConfigureAwait(false);
}

/// <summary>Org record (named Org_ to avoid clash with the Org namespace class).</summary>
public sealed class Org_
{
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    [JsonPropertyName("slug")] public string Slug { get; init; } = "";
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("status")] public string Status { get; init; } = "";
    [JsonPropertyName("createdAt")] public string CreatedAt { get; init; } = "";
    [JsonPropertyName("updatedAt")] public string? UpdatedAt { get; init; }
}
