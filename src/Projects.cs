using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace K9o;

/// <summary>Project lifecycle namespace.</summary>
public sealed class Projects
{
    private readonly K9oClient _client;

    internal Projects(K9oClient client) => _client = client;

    public async Task<Envelope<List<Project>>> ListAsync(CancellationToken ct = default) =>
        await HttpTransport.RequestAsync<List<Project>>(
            _client.Config, HttpMethod.Get, UrlGrammar.OrgPath("projects"), null, null, ct).ConfigureAwait(false);

    public async Task<Envelope<Project>> CreateAsync(CreateProjectInput input, CancellationToken ct = default)
    {
        if (input.Slug != null) UrlGrammar.ValidateSlug(input.Slug, "project");
        return await HttpTransport.RequestAsync<Project>(
            _client.Config, HttpMethod.Post, UrlGrammar.OrgPath("projects"), input, null, ct).ConfigureAwait(false);
    }

    public async Task<Envelope<Project>> GetAsync(string slug, CancellationToken ct = default)
    {
        UrlGrammar.ValidateSlug(slug, "project");
        return await HttpTransport.RequestAsync<Project>(
            _client.Config, HttpMethod.Get, UrlGrammar.OrgPath($"projects/{slug}"), null, null, ct).ConfigureAwait(false);
    }

    public async Task<Envelope<Project>> UpdateAsync(string slug, UpdateProjectInput patch, CancellationToken ct = default)
    {
        UrlGrammar.ValidateSlug(slug, "project");
        return await HttpTransport.RequestAsync<Project>(
            _client.Config, HttpMethod.Patch, UrlGrammar.OrgPath($"projects/{slug}"), patch, null, ct).ConfigureAwait(false);
    }
}

public sealed class Project
{
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    [JsonPropertyName("slug")] public string Slug { get; init; } = "";
    [JsonPropertyName("displayName")] public string DisplayName { get; init; } = "";
    [JsonPropertyName("description")] public string? Description { get; init; }
    [JsonPropertyName("region")] public string Region { get; init; } = "";
    [JsonPropertyName("custodyMode")] public string CustodyMode { get; init; } = "";
    [JsonPropertyName("status")] public string Status { get; init; } = "";
    [JsonPropertyName("createdAt")] public string CreatedAt { get; init; } = "";
    [JsonPropertyName("orgId")] public string OrgId { get; init; } = "";
}

/// <summary>
/// Body of <c>POST /projects</c>; property names are the gateway's. <see cref="DisplayName"/> is required (1 to 128
/// characters); the gateway derives the slug from it when <see cref="Slug"/> is null. <see cref="CustodyMode"/> is one
/// of <c>PLATFORM_MANAGED</c>, <c>SKR</c>, <c>BYOK</c>, <c>HYOK</c>. Null fields are not sent.
/// </summary>
public sealed class CreateProjectInput
{
    [JsonPropertyName("displayName")] public required string DisplayName { get; init; }
    [JsonPropertyName("slug")] public string? Slug { get; init; }
    [JsonPropertyName("region")] public string? Region { get; init; }
    [JsonPropertyName("custodyMode")] public string? CustodyMode { get; init; }
    [JsonPropertyName("processingContexts")] public List<string>? ProcessingContexts { get; init; }
    [JsonPropertyName("idempotencyKey")] public string? IdempotencyKey { get; init; }
}

/// <summary>Body of <c>PATCH /projects/&lt;slug&gt;</c>. Null fields are not sent.</summary>
public sealed class UpdateProjectInput
{
    [JsonPropertyName("displayName")] public string? DisplayName { get; init; }
    [JsonPropertyName("description")] public string? Description { get; init; }
    [JsonPropertyName("softDeleteRetentionDays")] public int? SoftDeleteRetentionDays { get; init; }
    [JsonPropertyName("processingContexts")] public List<string>? ProcessingContexts { get; init; }
}
