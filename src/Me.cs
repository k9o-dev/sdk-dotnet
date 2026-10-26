using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace K9o;

/// <summary>
/// Operator-self namespace — client.Me. Cross-org;
/// routes under /me. Sub-namespaces: Profile / Sessions / ApiTokens.
/// </summary>
public sealed class Me
{
    public MeProfile Profile { get; }
    public MeSessions Sessions { get; }
    public MeApiTokens ApiTokens { get; }

    internal Me(K9oClient client)
    {
        Profile = new MeProfile(client);
        Sessions = new MeSessions(client);
        ApiTokens = new MeApiTokens(client);
    }
}

public sealed class MeProfile
{
    private readonly K9oClient _c;
    internal MeProfile(K9oClient c) => _c = c;

    public async Task<Envelope<Dictionary<string, object?>>> GetAsync(CancellationToken ct = default) =>
        await HttpTransport.RequestAsync<Dictionary<string, object?>>(_c.Config, HttpMethod.Get, UrlGrammar.OrgPath("me"), null, null, ct).ConfigureAwait(false);

    public async Task<Envelope<Dictionary<string, object?>>> UpdateAsync(string? displayName, CancellationToken ct = default) =>
        await HttpTransport.RequestAsync<Dictionary<string, object?>>(_c.Config, HttpMethod.Patch, UrlGrammar.OrgPath("me"),
            new Dictionary<string, object?> { ["displayName"] = displayName }, null, ct).ConfigureAwait(false);
}

public sealed class MeSessions
{
    private readonly K9oClient _c;
    internal MeSessions(K9oClient c) => _c = c;

    public async Task<Envelope<List<Dictionary<string, object?>>>> ListAsync(CancellationToken ct = default) =>
        await HttpTransport.RequestAsync<List<Dictionary<string, object?>>>(_c.Config, HttpMethod.Get, UrlGrammar.OrgPath("me/sessions"), null, null, ct).ConfigureAwait(false);

    /// <summary>Revoke one session: <c>POST /auth/sessions/&lt;sessionId&gt;/revoke</c>.</summary>
    public async Task<Envelope<Dictionary<string, object?>>> RevokeAsync(string sessionId, CancellationToken ct = default) =>
        await HttpTransport.RequestAsync<Dictionary<string, object?>>(_c.Config, HttpMethod.Post, UrlGrammar.OrgPath($"auth/sessions/{HttpUtility.UrlEncode(sessionId)}/revoke"), null, null, ct).ConfigureAwait(false);

    /// <summary>Revoke all of the operator's sessions: <c>POST /me/sessions/revoke-all</c>.</summary>
    public async Task<Envelope<Dictionary<string, object?>>> RevokeAllAsync(CancellationToken ct = default) =>
        await HttpTransport.RequestAsync<Dictionary<string, object?>>(_c.Config, HttpMethod.Post, UrlGrammar.OrgPath("me/sessions/revoke-all"), null, null, ct).ConfigureAwait(false);
}

public sealed class MeApiTokens
{
    private readonly K9oClient _c;
    internal MeApiTokens(K9oClient c) => _c = c;

    public async Task<Envelope<List<Dictionary<string, object?>>>> ListAsync(CancellationToken ct = default) =>
        await HttpTransport.RequestAsync<List<Dictionary<string, object?>>>(_c.Config, HttpMethod.Get, UrlGrammar.OrgPath("me/api-tokens"), null, null, ct).ConfigureAwait(false);

    /// <summary>
    /// Create a personal access token: <c>POST /me/api-tokens</c> with <c>{ label, scopeManifest, ttlSeconds? }</c>.
    /// <paramref name="ttlSeconds"/> (nullable) is 60 to 31536000. The response carries the plaintext token once.
    /// </summary>
    public async Task<Envelope<Dictionary<string, object?>>> CreateAsync(string label, ScopeManifest scopeManifest, int? ttlSeconds = null, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?> { ["label"] = label, ["scopeManifest"] = scopeManifest.ToMap() };
        if (ttlSeconds != null) body["ttlSeconds"] = ttlSeconds;
        return await HttpTransport.RequestAsync<Dictionary<string, object?>>(_c.Config, HttpMethod.Post, UrlGrammar.OrgPath("me/api-tokens"), body, null, ct).ConfigureAwait(false);
    }

    public async Task<Envelope<Dictionary<string, object?>>> DeleteAsync(string tokenId, CancellationToken ct = default) =>
        await HttpTransport.RequestAsync<Dictionary<string, object?>>(_c.Config, HttpMethod.Delete, UrlGrammar.OrgPath($"me/api-tokens/{HttpUtility.UrlEncode(tokenId)}"), null, null, ct).ConfigureAwait(false);
}

/// <summary>
/// What a token may do; each field narrows the token and <c>null</c> leaves it unrestricted. <c>Operations</c> entries
/// are read, write or admin. The wire field names are the gateway's.
/// </summary>
public sealed class ScopeManifest
{
    public List<string>? Operations { get; init; }
    public List<string>? Projects { get; init; }
    public List<string>? ResourceClasses { get; init; }
    public Dictionary<string, object?>? Collections { get; init; }
    public List<string>? Cidrs { get; init; }

    internal Dictionary<string, object?> ToMap() => new()
    {
        ["operations"] = Operations,
        ["projects"] = Projects,
        ["resourceClasses"] = ResourceClasses,
        ["collections"] = Collections,
        ["cidrs"] = Cidrs,
    };
}
