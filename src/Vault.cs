using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace K9o;

/// <summary>
/// Vault namespace — project-scoped secrets management proxied through the
/// gateway at <c>/@&lt;projectSlug&gt;/vault/...</c>. Mirrors the TS SDK's
/// <c>client.kibo.vault</c> composition (<c>@k9o/kibo</c>'s <c>vault.{zkDeks, secrets}</c>).
///
/// NOTE on package placement: unlike the TS package, .NET's Kibo surface (the separate
/// <c>K9o.Kibo</c> NuGet package, <c>packages/dotnet-kibo</c>) is FFI-only client-side ZK
/// crypto (<c>Kibo.Encrypt</c>/<c>Decrypt</c> over the audited Rust core via uniffi) — it has
/// no <see cref="HttpClient"/>, no <see cref="K9oClient"/> reference, and never calls the
/// gateway REST API at all. The vault-secrets REST family
/// (<c>services/gateway/src/routes/vault-secrets.ts</c>) is a plain authenticated round-trip
/// exactly like Projects/Gate/Studio, so it belongs with the rest of the HttpTransport-backed
/// namespaces here in <c>K9o.Core</c> rather than in <c>K9o.Kibo</c>. Only <see cref="Secrets"/>
/// is implemented; <c>zkDeks</c> (opaque DEK-envelope store/fetch) has no .NET wrapper yet
/// either — out of scope for this change, flagged separately.
/// </summary>
public sealed class Vault
{
    public Secrets Secrets { get; }

    internal Vault(K9oClient client)
    {
        Secrets = new Secrets(client);
    }

    internal static string RequireProjectSlug(HttpClientConfig config, string surface)
    {
        if (string.IsNullOrEmpty(config.ProjectSlug))
        {
            throw new KError("K9O_MISSING_PROJECT_CONTEXT",
                $"{surface} operations require ProjectSlug bound on the client");
        }
        return config.ProjectSlug!;
    }
}

// ----------------------------------------------------------------------
// Secrets — services/gateway/src/routes/vault-secrets.ts
// ----------------------------------------------------------------------

/// <summary>
/// Typed secrets surface: List/Get/Create/RevealChallenge/Reveal/Update/Rotate/Move/Delete.
///
/// <c>region</c> is OPTIONAL on every named-secret operation: the gateway ignores any
/// caller-supplied region and uses its own. Each named-secret method has an overload without
/// <c>region</c>; the overloads taking <c>region</c> keep working and send it on the query
/// string. <see cref="GetAsync(string, CancellationToken)"/> never sends it.
///
/// Response shapes for <see cref="UpdateAsync(string, string, Dictionary{string, string}?, CancellationToken)"/>/<see cref="RotateAsync(string, CancellationToken)"/>/<see cref="MoveAsync(string, string, CancellationToken)"/>
/// are typed to what the gateway actually returns, not the full <see cref="SecretSummary"/> — see the notes on
/// <see cref="UpdateSecretResult"/>/<see cref="RotateSecretResult"/>/<see cref="MoveSecretResult"/>.
/// </summary>
public sealed class Secrets
{
    private const int MaxGetPages = 20;

    private readonly K9oClient _client;
    internal Secrets(K9oClient client) => _client = client;

    private string RequireProjectSlug() => Vault.RequireProjectSlug(_client.Config, "vault.secrets");

    /// <summary>
    /// UTF-8 base64 encode — the vault-secrets wire <c>value</c> field is base64 in BOTH
    /// directions.
    /// </summary>
    private static string EncodeBase64Utf8(string plaintext) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(plaintext));

    /// <summary>Inverse of <see cref="EncodeBase64Utf8"/> — decodes a revealed wire <c>value</c>
    /// back to plaintext (the reveal response's <c>value</c> is also base64).</summary>
    private static string DecodeBase64Utf8(string base64) =>
        Encoding.UTF8.GetString(Convert.FromBase64String(base64));

    /// <summary>
    /// List secrets (metadata only — never lists ciphertext).
    /// <c>GET /@&lt;projectSlug&gt;/vault/secrets</c>.
    /// </summary>
    public async Task<Envelope<ListSecretsResult>> ListAsync(
        string? prefix = null, string? cursor = null, int? limit = null, CancellationToken ct = default)
    {
        var slug = RequireProjectSlug();
        var q = new Dictionary<string, string>();
        if (prefix != null) q["prefix"] = prefix;
        if (cursor != null) q["cursor"] = cursor;
        if (limit != null) q["limit"] = limit.Value.ToString();
        return await HttpTransport.RequestAsync<ListSecretsResult>(
            _client.Config, HttpMethod.Get, UrlGrammar.ProjectPath(slug, "vault/secrets"),
            null, q.Count > 0 ? q : null, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Read a secret's metadata (never its value — use <see cref="RevealChallengeAsync(string, CancellationToken)"/> +
    /// <see cref="RevealAsync(string, string, CancellationToken)"/> for that).
    ///
    /// <c>GET /@&lt;projectSlug&gt;/vault/secrets/&lt;name&gt;</c> is a reveal and always demands an
    /// <c>X-K9O-Reveal-Challenge</c> header, so metadata is read from the list route: list with the
    /// exact name as <c>prefix</c> (limit 100, following <c>nextCursor</c> for up to 20 pages) and
    /// return the secret whose <c>name</c> equals <paramref name="name"/>. Throws <see cref="KError"/>
    /// <c>K9O_NOT_FOUND</c> (status 404) when none matches.
    /// </summary>
    public async Task<Envelope<SecretSummary>> GetAsync(string name, CancellationToken ct = default)
    {
        string? cursor = null;
        for (var page = 0; page < MaxGetPages; page++)
        {
            var env = await ListAsync(prefix: name, cursor: cursor, limit: 100, ct: ct).ConfigureAwait(false);
            var data = env.Data;
            if (data?.Secrets != null)
            {
                foreach (var secret in data.Secrets)
                {
                    if (secret.Name == name) return new Envelope<SecretSummary> { Data = secret, Meta = env.Meta };
                }
            }
            cursor = data?.NextCursor;
            if (string.IsNullOrEmpty(cursor)) break;
        }
        throw new KError(
            "K9O_NOT_FOUND",
            $"No secret named '{name}' in this project.",
            new Dictionary<string, object?> { ["name"] = name },
            null, 404, null);
    }

    /// <summary>Same as <see cref="GetAsync(string, CancellationToken)"/>; <paramref name="region"/> is
    /// accepted for symmetry and not sent.</summary>
    public Task<Envelope<SecretSummary>> GetAsync(string name, string? region, CancellationToken ct = default) =>
        GetAsync(name, ct);

    private static Dictionary<string, string> RegionQuery(string? region, string? op = null)
    {
        var q = new Dictionary<string, string>();
        if (op != null) q["op"] = op;
        if (!string.IsNullOrEmpty(region)) q["region"] = region!;
        return q;
    }

    private static string SecretPath(string name) => $"vault/secrets/{HttpUtility.UrlEncode(name)}";

    /// <summary>
    /// Create a secret under the caller's own auth.
    /// <c>POST /@&lt;projectSlug&gt;/vault/secrets/&lt;name&gt;</c> (+ <c>region</c> when given) → the
    /// full <see cref="SecretSummary"/>. <paramref name="value"/> is the caller-facing PLAINTEXT; this
    /// method base64-encodes it for the wire.
    /// </summary>
    public Task<Envelope<SecretSummary>> CreateAsync(
        string name,
        string value,
        SecretVisibility? visibility = null,
        Dictionary<string, string>? metadata = null,
        CancellationToken ct = default) =>
        CreateAsync(name, null, value, visibility, metadata, ct);

    /// <inheritdoc cref="CreateAsync(string, string, SecretVisibility?, Dictionary{string, string}?, CancellationToken)"/>
    public async Task<Envelope<SecretSummary>> CreateAsync(
        string name,
        string? region,
        string value,
        SecretVisibility? visibility = null,
        Dictionary<string, string>? metadata = null,
        CancellationToken ct = default)
    {
        var slug = RequireProjectSlug();
        var body = new Dictionary<string, object?> { ["value"] = EncodeBase64Utf8(value) };
        if (visibility != null) body["visibility"] = visibility;
        if (metadata != null) body["metadata"] = metadata;
        return await HttpTransport.RequestAsync<SecretSummary>(
            _client.Config, HttpMethod.Post, UrlGrammar.ProjectPath(slug, SecretPath(name)),
            body, RegionQuery(region), ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Mint a single-use reveal challenge — present it as <c>X-K9O-Reveal-Challenge</c> on the
    /// immediately-following <see cref="RevealAsync(string, string, CancellationToken)"/> call.
    /// <c>POST .../&lt;name&gt;?op=reveal-challenge</c>, no body.
    /// </summary>
    public Task<Envelope<RevealChallenge>> RevealChallengeAsync(string name, CancellationToken ct = default) =>
        RevealChallengeAsync(name, null, ct);

    /// <inheritdoc cref="RevealChallengeAsync(string, CancellationToken)"/>
    public async Task<Envelope<RevealChallenge>> RevealChallengeAsync(string name, string? region, CancellationToken ct = default)
    {
        var slug = RequireProjectSlug();
        return await HttpTransport.RequestAsync<RevealChallenge>(
            _client.Config, HttpMethod.Post, UrlGrammar.ProjectPath(slug, SecretPath(name)),
            null, RegionQuery(region, "reveal-challenge"), ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Reveal a secret's plaintext value using a single-use challenge from
    /// <see cref="RevealChallengeAsync(string, CancellationToken)"/>. <c>GET .../&lt;name&gt;</c> with
    /// header <c>X-K9O-Reveal-Challenge</c>. Base64-decodes the wire's <c>value</c> back to plaintext —
    /// the returned <see cref="RevealSecretResult.Value"/> is always plaintext, never base64.
    /// </summary>
    public Task<Envelope<RevealSecretResult>> RevealAsync(string name, string challenge, CancellationToken ct = default) =>
        RevealAsync(name, null, challenge, ct);

    /// <inheritdoc cref="RevealAsync(string, string, CancellationToken)"/>
    public async Task<Envelope<RevealSecretResult>> RevealAsync(string name, string? region, string challenge, CancellationToken ct = default)
    {
        var slug = RequireProjectSlug();
        var headers = new Dictionary<string, string> { ["X-K9O-Reveal-Challenge"] = challenge };
        var envelope = await HttpTransport.RequestAsync<RevealSecretResult>(
            _client.Config, HttpMethod.Get, UrlGrammar.ProjectPath(slug, SecretPath(name)),
            null, RegionQuery(region), ct, headers).ConfigureAwait(false);
        if (envelope.Data == null) return envelope;
        return new Envelope<RevealSecretResult>
        {
            Data = new RevealSecretResult
            {
                Id = envelope.Data.Id,
                Name = envelope.Data.Name,
                Version = envelope.Data.Version,
                Value = DecodeBase64Utf8(envelope.Data.Value),
                RevealedAt = envelope.Data.RevealedAt,
                AttestationTier = envelope.Data.AttestationTier,
            },
            Meta = envelope.Meta,
        };
    }

    /// <summary>
    /// Update a secret's value (bumps version) — owner-only. <c>PUT .../&lt;name&gt;</c>.
    /// Returns only <see cref="UpdateSecretResult"/> (<c>{id, name, version}</c>), narrower than the
    /// full <see cref="SecretSummary"/> — call <see cref="GetAsync(string, CancellationToken)"/>
    /// afterward if the rest is needed.
    /// </summary>
    public Task<Envelope<UpdateSecretResult>> UpdateAsync(
        string name,
        string value,
        Dictionary<string, string>? metadata = null,
        CancellationToken ct = default) =>
        UpdateAsync(name, null, value, metadata, ct);

    /// <inheritdoc cref="UpdateAsync(string, string, Dictionary{string, string}?, CancellationToken)"/>
    public async Task<Envelope<UpdateSecretResult>> UpdateAsync(
        string name,
        string? region,
        string value,
        Dictionary<string, string>? metadata = null,
        CancellationToken ct = default)
    {
        var slug = RequireProjectSlug();
        var body = new Dictionary<string, object?> { ["value"] = EncodeBase64Utf8(value) };
        if (metadata != null) body["metadata"] = metadata;
        return await HttpTransport.RequestAsync<UpdateSecretResult>(
            _client.Config, HttpMethod.Put, UrlGrammar.ProjectPath(slug, SecretPath(name)),
            body, RegionQuery(region), ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Rotate a secret's wrapping key (re-wraps under a fresh <c>kid</c>; content unchanged) —
    /// owner-only. <c>POST .../&lt;name&gt;?op=rotate</c>, no body. Response shape:
    /// <see cref="RotateSecretResult"/>.
    /// </summary>
    public Task<Envelope<RotateSecretResult>> RotateAsync(string name, CancellationToken ct = default) =>
        RotateAsync(name, null, ct);

    /// <inheritdoc cref="RotateAsync(string, CancellationToken)"/>
    public async Task<Envelope<RotateSecretResult>> RotateAsync(string name, string? region, CancellationToken ct = default)
    {
        var slug = RequireProjectSlug();
        return await HttpTransport.RequestAsync<RotateSecretResult>(
            _client.Config, HttpMethod.Post, UrlGrammar.ProjectPath(slug, SecretPath(name)),
            null, RegionQuery(region, "rotate"), ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Move (rename) a secret's hierarchical path — owner-only.
    /// <c>POST .../&lt;name&gt;?op=move</c> with body <c>{newPath}</c>. Response shape:
    /// <see cref="MoveSecretResult"/>; <see cref="MoveSecretResult.Event"/> is a plain string
    /// (<c>"SECRET_PATH_CHANGED"</c> observed), not a closed enum.
    /// </summary>
    public Task<Envelope<MoveSecretResult>> MoveAsync(string name, string newPath, CancellationToken ct = default) =>
        MoveAsync(name, null, newPath, ct);

    /// <inheritdoc cref="MoveAsync(string, string, CancellationToken)"/>
    public async Task<Envelope<MoveSecretResult>> MoveAsync(string name, string? region, string newPath, CancellationToken ct = default)
    {
        var slug = RequireProjectSlug();
        var body = new Dictionary<string, string> { ["newPath"] = newPath };
        return await HttpTransport.RequestAsync<MoveSecretResult>(
            _client.Config, HttpMethod.Post, UrlGrammar.ProjectPath(slug, SecretPath(name)),
            body, RegionQuery(region, "move"), ct).ConfigureAwait(false);
    }

    /// <summary>Retire a secret — owner-only. <c>DELETE .../&lt;name&gt;</c>; any 2xx is success
    /// (<c>204 No Content</c> leaves <c>Data</c> <c>null</c> on the returned envelope).</summary>
    public Task<Envelope<Dictionary<string, object?>>> DeleteAsync(string name, CancellationToken ct = default) =>
        DeleteAsync(name, null, ct);

    /// <inheritdoc cref="DeleteAsync(string, CancellationToken)"/>
    public async Task<Envelope<Dictionary<string, object?>>> DeleteAsync(string name, string? region, CancellationToken ct = default)
    {
        var slug = RequireProjectSlug();
        return await HttpTransport.RequestAsync<Dictionary<string, object?>>(
            _client.Config, HttpMethod.Delete, UrlGrammar.ProjectPath(slug, SecretPath(name)),
            null, RegionQuery(region), ct).ConfigureAwait(false);
    }
}

// ----------------------------------------------------------------------
// Types
// ----------------------------------------------------------------------

/// <summary>Secret visibility. Write ops (create/update) stay owner-only regardless of
/// this tag.</summary>
public sealed class SecretVisibility
{
    /// <summary>"private" | "shared".</summary>
    [JsonPropertyName("kind")] public required string Kind { get; init; }
    [JsonPropertyName("principals")] public List<string>? Principals { get; init; }
    [JsonPropertyName("roles")] public List<string>? Roles { get; init; }
}

/// <summary>A secret WITHOUT its sealed ciphertext — the create/list/get metadata shape.</summary>
public sealed class SecretSummary
{
    /// <summary><c>sec_&lt;base62&gt;</c>.</summary>
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("ownerId")] public string OwnerId { get; init; } = "";
    [JsonPropertyName("visibility")] public SecretVisibility Visibility { get; init; } = new() { Kind = "private" };
    [JsonPropertyName("version")] public int Version { get; init; }
    /// <summary>The wrapping-key id at this version.</summary>
    [JsonPropertyName("kid")] public string Kid { get; init; } = "";
    [JsonPropertyName("createdAt")] public string CreatedAt { get; init; } = "";
    [JsonPropertyName("createdBy")] public string CreatedBy { get; init; } = "";
    [JsonPropertyName("updatedAt")] public string UpdatedAt { get; init; } = "";
    [JsonPropertyName("updatedBy")] public string UpdatedBy { get; init; } = "";
    [JsonPropertyName("rotatedAt")] public string? RotatedAt { get; init; }
    [JsonPropertyName("retiredAt")] public string? RetiredAt { get; init; }
    [JsonPropertyName("metadata")] public Dictionary<string, string> Metadata { get; init; } = new();
}

/// <summary><see cref="Secrets.ListAsync"/> result.</summary>
public sealed class ListSecretsResult
{
    [JsonPropertyName("secrets")] public List<SecretSummary> Secrets { get; init; } = new();
    [JsonPropertyName("nextCursor")] public string? NextCursor { get; init; }
}

/// <summary>Releasing-node observed attestation tier (K4) — present on every reveal.</summary>
public sealed class AttestationTier
{
    /// <summary><c>attestation:destination:region</c> trust-tier URN.</summary>
    [JsonPropertyName("urn")] public string Urn { get; init; } = "";
    /// <summary>Node provenance: "platform" (Kibo-operated) or "client" (Private Perimeter).</summary>
    [JsonPropertyName("operator")] public string Operator { get; init; } = "";
    [JsonPropertyName("verified")] public bool Verified { get; init; }
}

/// <summary><see cref="Secrets.RevealChallengeAsync"/> result — a single-use challenge to present
/// on the immediately-following <see cref="Secrets.RevealAsync"/>.</summary>
public sealed class RevealChallenge
{
    [JsonPropertyName("challenge")] public string Challenge { get; init; } = "";
    [JsonPropertyName("expiresAt")] public string ExpiresAt { get; init; } = "";
}

/// <summary><see cref="Secrets.RevealAsync"/> result. <see cref="Value"/> is the decoded
/// plaintext (the SDK base64-decodes the wire value, the inverse of create/update's encode) —
/// never raw base64.</summary>
public sealed class RevealSecretResult
{
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("version")] public int Version { get; init; }
    [JsonPropertyName("value")] public string Value { get; init; } = "";
    [JsonPropertyName("revealedAt")] public string RevealedAt { get; init; } = "";
    [JsonPropertyName("attestationTier")] public AttestationTier AttestationTier { get; init; } = new();
}

/// <summary><see cref="Secrets.UpdateAsync"/> result — narrower than the
/// full SecretSummary: the gateway returns only <c>{id, name, version}</c> on a real PUT,
/// not the full summary the OpenAPI schema claims.</summary>
public sealed class UpdateSecretResult
{
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("version")] public int Version { get; init; }
}

/// <summary><see cref="Secrets.RotateAsync"/> result — response shape. <see cref="LedgerSequenceNumber"/> is undocumented; kept
/// as-observed.</summary>
public sealed class RotateSecretResult
{
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("version")] public int Version { get; init; }
    [JsonPropertyName("rotatedAt")] public string RotatedAt { get; init; } = "";
    [JsonPropertyName("ledgerSequenceNumber")] public long LedgerSequenceNumber { get; init; }
}

/// <summary><see cref="Secrets.MoveAsync"/> result — response shape, not a
/// SecretSummary: no <c>version</c>, and an <c>event</c> field
/// (<c>"SECRET_PATH_CHANGED"</c> observed; typed as a plain string since only one sample was
/// seen and a closed enum can't be asserted from one observation).</summary>
public sealed class MoveSecretResult
{
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("event")] public string Event { get; init; } = "";
}
