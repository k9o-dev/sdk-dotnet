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
        UrlGrammar.ValidateSlug(input.Slug, "project");
        return await HttpTransport.RequestAsync<Project>(
            _client.Config, HttpMethod.Post, UrlGrammar.OrgPath("projects"), input, null, ct).ConfigureAwait(false);
    }

    public async Task<Envelope<Project>> GetAsync(string slug, CancellationToken ct = default)
    {
        UrlGrammar.ValidateSlug(slug, "project");
        return await HttpTransport.RequestAsync<Project>(
            _client.Config, HttpMethod.Get, UrlGrammar.ProjectPath(slug, "project"), null, null, ct).ConfigureAwait(false);
    }

    public async Task<Envelope<Project>> UpdateAsync(string slug, UpdateProjectInput patch, CancellationToken ct = default)
    {
        UrlGrammar.ValidateSlug(slug, "project");
        return await HttpTransport.RequestAsync<Project>(
            _client.Config, HttpMethod.Patch, UrlGrammar.ProjectPath(slug, "project"), patch, null, ct).ConfigureAwait(false);
    }
}

public sealed class Project
{
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    [JsonPropertyName("slug")] public string Slug { get; init; } = "";
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("custodyMode")] public string CustodyMode { get; init; } = "";
    [JsonPropertyName("createdAt")] public string CreatedAt { get; init; } = "";
    [JsonPropertyName("updatedAt")] public string? UpdatedAt { get; init; }
    [JsonPropertyName("orgId")] public string OrgId { get; init; } = "";
}

public sealed class CreateProjectInput
{
    [JsonPropertyName("slug")] public required string Slug { get; init; }
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("custodyMode")] public string? CustodyMode { get; init; }
}

public sealed class UpdateProjectInput
{
    [JsonPropertyName("name")] public string? Name { get; init; }
}
