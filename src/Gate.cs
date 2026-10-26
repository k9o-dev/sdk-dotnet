using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace K9o;

/// <summary>
/// Gate (Identity-Aware Kernel) namespace. Project-scoped.
/// Throws <see cref="KError"/> with <c>K9O_MISSING_PROJECT_CONTEXT</c>
/// if called without <c>ProjectSlug</c> bound on the client.
/// </summary>
public sealed class Gate
{
    private readonly K9oClient _client;

    internal Gate(K9oClient client) => _client = client;

    private string RequireProject()
    {
        if (string.IsNullOrEmpty(_client.Config.ProjectSlug))
        {
            throw new KError(
                "K9O_MISSING_PROJECT_CONTEXT",
                "client.Gate operations require ProjectSlug bound on the client",
                null, null, null, null
            );
        }
        return _client.Config.ProjectSlug!;
    }

    public Task<Envelope<JsonElement>> SignUpAsync(SignUpInput input, CancellationToken ct = default) =>
        HttpTransport.RequestAsync<JsonElement>(_client.Config, HttpMethod.Post,
            UrlGrammar.ProjectPath(RequireProject(), "gate/signup"), input, null, ct);

    public Task<Envelope<JsonElement>> SignInAsync(SignInInput input, CancellationToken ct = default) =>
        HttpTransport.RequestAsync<JsonElement>(_client.Config, HttpMethod.Post,
            UrlGrammar.ProjectPath(RequireProject(), "gate/signin"), input, null, ct);

    public Task<Envelope<JsonElement>> SignOutAsync(SignOutInput input, CancellationToken ct = default) =>
        HttpTransport.RequestAsync<JsonElement>(_client.Config, HttpMethod.Post,
            UrlGrammar.ProjectPath(RequireProject(), "gate/signout"), input, null, ct);

    public Task<Envelope<JsonElement>> WhoamiAsync(CancellationToken ct = default) =>
        HttpTransport.RequestAsync<JsonElement>(_client.Config, HttpMethod.Get,
            UrlGrammar.ProjectPath(RequireProject(), "gate/whoami"), null, null, ct);

    public Task<Envelope<JsonElement>> RefreshTokenAsync(RefreshTokenInput input, CancellationToken ct = default) =>
        HttpTransport.RequestAsync<JsonElement>(_client.Config, HttpMethod.Post,
            UrlGrammar.ProjectPath(RequireProject(), "gate/refresh"), input, null, ct);
}

public sealed class SignInInput
{
    [JsonPropertyName("email")] public required string Email { get; init; }
    [JsonPropertyName("password")] public required string Password { get; init; }
}

public sealed class SignUpInput
{
    [JsonPropertyName("email")] public required string Email { get; init; }
    [JsonPropertyName("password")] public required string Password { get; init; }
    [JsonPropertyName("displayName")] public string? DisplayName { get; init; }
}

public sealed class SignOutInput
{
    [JsonPropertyName("refreshToken")] public string? RefreshToken { get; init; }
}

public sealed class RefreshTokenInput
{
    [JsonPropertyName("refreshToken")] public required string RefreshToken { get; init; }
}
