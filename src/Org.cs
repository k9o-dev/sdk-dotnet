using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace K9o;

/// <summary>
/// Org-plane bound namespace — client.Org. Singular
/// bound-org namespace (distinct from client.Orgs collection): Members /
/// Groups (read) / Smtp / AuditLog / Gate (Roles + IdpProviders) + DefaultLlm.
/// </summary>
public sealed class Org
{
    private readonly K9oClient _client;

    public OrgMembers Members { get; }
    public OrgGroups Groups { get; }
    public OrgSmtp Smtp { get; }
    public OrgAuditLog AuditLog { get; }
    public OrgGate Gate { get; }
    public DefaultLlm DefaultLlm { get; }

    internal Org(K9oClient client)
    {
        _client = client;
        Members = new OrgMembers(client);
        Groups = new OrgGroups(client);
        Smtp = new OrgSmtp(client);
        AuditLog = new OrgAuditLog(client);
        Gate = new OrgGate(client);
        DefaultLlm = new DefaultLlm(client);
    }

    /// <summary>Read the current org (slug + name + status + settings).</summary>
    public async Task<Envelope<Dictionary<string, object?>>> GetAsync(CancellationToken ct = default) =>
        await HttpTransport.RequestAsync<Dictionary<string, object?>>(_client.Config, HttpMethod.Get, UrlGrammar.OrgPath("org"), null, null, ct).ConfigureAwait(false);
}

public sealed class OrgMembers
{
    private readonly K9oClient _c;
    internal OrgMembers(K9oClient c) => _c = c;

    /// <summary>
    /// List members. The route is not paged; it filters by <c>userId</c>, <c>role</c>, <c>acceptanceStatus</c>
    /// (<c>accepted</c> or <c>pending</c>), <c>status</c> (<c>active</c> or <c>suspended</c>) and <c>state</c>.
    /// Null arguments are not sent.
    /// </summary>
    public async Task<Envelope<List<Dictionary<string, object?>>>> ListAsync(string? status = null, string? userId = null, string? role = null, string? acceptanceStatus = null, string? state = null, CancellationToken ct = default)
    {
        var q = new Dictionary<string, string>();
        if (userId != null) q["userId"] = userId;
        if (role != null) q["role"] = role;
        if (acceptanceStatus != null) q["acceptanceStatus"] = acceptanceStatus;
        if (status != null) q["status"] = status;
        if (state != null) q["state"] = state;
        return await HttpTransport.RequestAsync<List<Dictionary<string, object?>>>(_c.Config, HttpMethod.Get, UrlGrammar.OrgPath("members"), null, q.Count > 0 ? q : null, ct).ConfigureAwait(false);
    }

    public async Task<Envelope<Dictionary<string, object?>>> InviteAsync(string email, string role, CancellationToken ct = default) =>
        await HttpTransport.RequestAsync<Dictionary<string, object?>>(_c.Config, HttpMethod.Post, UrlGrammar.OrgPath("members"),
            new Dictionary<string, object?> { ["email"] = email, ["role"] = role }, null, ct).ConfigureAwait(false);

    public async Task<Envelope<Dictionary<string, object?>>> RemoveAsync(string userId, CancellationToken ct = default) =>
        await HttpTransport.RequestAsync<Dictionary<string, object?>>(_c.Config, HttpMethod.Delete, UrlGrammar.OrgPath($"members/{HttpUtility.UrlEncode(userId)}"), null, null, ct).ConfigureAwait(false);
}

public sealed class OrgGroups
{
    private readonly K9oClient _c;
    internal OrgGroups(K9oClient c) => _c = c;

    /// <summary>List groups; <paramref name="userId"/> (nullable) narrows to the groups that user belongs to.</summary>
    public async Task<Envelope<List<Dictionary<string, object?>>>> ListAsync(string? userId = null, CancellationToken ct = default) =>
        await HttpTransport.RequestAsync<List<Dictionary<string, object?>>>(_c.Config, HttpMethod.Get, UrlGrammar.OrgPath("groups"), null,
            userId != null ? new Dictionary<string, string> { ["userId"] = userId } : null, ct).ConfigureAwait(false);

    public async Task<Envelope<Dictionary<string, object?>>> GetAsync(string groupId, CancellationToken ct = default) =>
        await HttpTransport.RequestAsync<Dictionary<string, object?>>(_c.Config, HttpMethod.Get, UrlGrammar.OrgPath($"groups/{HttpUtility.UrlEncode(groupId)}"), null, null, ct).ConfigureAwait(false);
}

public sealed class OrgAuditLog
{
    private readonly K9oClient _c;
    internal OrgAuditLog(K9oClient c) => _c = c;

    /// <summary>Org-plane audit events. The route reads only <c>type</c> (event-type prefix) and <c>limit</c>; null arguments are not sent.</summary>
    public async Task<Envelope<List<Dictionary<string, object?>>>> QueryAsync(string? type = null, int? limit = null, CancellationToken ct = default)
    {
        var q = new Dictionary<string, string>();
        if (type != null) q["type"] = type;
        if (limit != null) q["limit"] = limit.Value.ToString();
        return await HttpTransport.RequestAsync<List<Dictionary<string, object?>>>(_c.Config, HttpMethod.Get, UrlGrammar.OrgPath("audit-log"), null, q.Count > 0 ? q : null, ct).ConfigureAwait(false);
    }
}

public sealed class OrgGate
{
    public OrgRoles Roles { get; }
    public OrgIdpProviders IdpProviders { get; }

    internal OrgGate(K9oClient client)
    {
        Roles = new OrgRoles(client);
        IdpProviders = new OrgIdpProviders(client);
    }
}

public sealed class OrgRoles
{
    private readonly K9oClient _c;
    internal OrgRoles(K9oClient c) => _c = c;

    public async Task<Envelope<List<Dictionary<string, object?>>>> ListAsync(CancellationToken ct = default) =>
        await HttpTransport.RequestAsync<List<Dictionary<string, object?>>>(_c.Config, HttpMethod.Get, UrlGrammar.OrgPath("role-grants"), null, null, ct).ConfigureAwait(false);

    /// <summary>Grant an org role; <paramref name="decisionReason"/> (nullable) is recorded with the grant.</summary>
    public async Task<Envelope<Dictionary<string, object?>>> GrantAsync(string userId, string role, string? decisionReason = null, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?> { ["userId"] = userId, ["role"] = role };
        if (decisionReason != null) body["decisionReason"] = decisionReason;
        return await HttpTransport.RequestAsync<Dictionary<string, object?>>(_c.Config, HttpMethod.Post, UrlGrammar.OrgPath("role-grants"), body, null, ct).ConfigureAwait(false);
    }

    public async Task<Envelope<Dictionary<string, object?>>> GetAsync(string grantId, CancellationToken ct = default) =>
        await HttpTransport.RequestAsync<Dictionary<string, object?>>(_c.Config, HttpMethod.Get, UrlGrammar.OrgPath($"role-grants/{HttpUtility.UrlEncode(grantId)}"), null, null, ct).ConfigureAwait(false);

    /// <summary>Revoke a grant (soft delete).</summary>
    public async Task<Envelope<Dictionary<string, object?>>> RevokeAsync(string grantId, CancellationToken ct = default) =>
        await HttpTransport.RequestAsync<Dictionary<string, object?>>(_c.Config, HttpMethod.Delete, UrlGrammar.OrgPath($"role-grants/{HttpUtility.UrlEncode(grantId)}"), null, null, ct).ConfigureAwait(false);
}

/// <summary>
/// Body of <c>POST /identity-providers</c>. <c>IdpProvider</c> is one of google, github, microsoft, oidc, saml, scim,
/// ldap, active_directory, apple, facebook, twitter. <c>ClientSecretRef</c> is a reference to a stored secret, never
/// the secret itself. <c>TierGating</c> is <c>free_tier_ok</c> or <c>ridge_plus</c>. Null fields are not sent.
/// </summary>
public sealed class CreateIdpInput
{
    [JsonPropertyName("idpProvider")] public required string IdpProvider { get; init; }
    [JsonPropertyName("displayName")] public required string DisplayName { get; init; }
    [JsonPropertyName("clientId")] public required string ClientId { get; init; }
    [JsonPropertyName("clientSecretRef")] public required string ClientSecretRef { get; init; }
    [JsonPropertyName("tierGating")] public required string TierGating { get; init; }
    [JsonPropertyName("redirectUri")] public string? RedirectUri { get; init; }
    [JsonPropertyName("metadataUrl")] public string? MetadataUrl { get; init; }
    [JsonPropertyName("attributeMap")] public Dictionary<string, object?>? AttributeMap { get; init; }
    [JsonPropertyName("enabled")] public bool? Enabled { get; init; }
}

public sealed class OrgIdpProviders
{
    private readonly K9oClient _c;
    internal OrgIdpProviders(K9oClient c) => _c = c;

    public async Task<Envelope<List<Dictionary<string, object?>>>> ListAsync(CancellationToken ct = default) =>
        await HttpTransport.RequestAsync<List<Dictionary<string, object?>>>(_c.Config, HttpMethod.Get, UrlGrammar.OrgPath("identity-providers"), null, null, ct).ConfigureAwait(false);

    public async Task<Envelope<Dictionary<string, object?>>> CreateAsync(CreateIdpInput input, CancellationToken ct = default) =>
        await HttpTransport.RequestAsync<Dictionary<string, object?>>(_c.Config, HttpMethod.Post, UrlGrammar.OrgPath("identity-providers"), input, null, ct).ConfigureAwait(false);

    public async Task<Envelope<Dictionary<string, object?>>> GetAsync(string idpId, CancellationToken ct = default) =>
        await HttpTransport.RequestAsync<Dictionary<string, object?>>(_c.Config, HttpMethod.Get, UrlGrammar.OrgPath($"identity-providers/{HttpUtility.UrlEncode(idpId)}"), null, null, ct).ConfigureAwait(false);

    /// <summary>Patch an IdP: any of displayName, clientId, clientSecretRef, redirectUri, metadataUrl, attributeMap, enabled.</summary>
    public async Task<Envelope<Dictionary<string, object?>>> ConfigureAsync(string idpId, Dictionary<string, object?> patch, CancellationToken ct = default) =>
        await HttpTransport.RequestAsync<Dictionary<string, object?>>(_c.Config, HttpMethod.Patch, UrlGrammar.OrgPath($"identity-providers/{HttpUtility.UrlEncode(idpId)}"), patch, null, ct).ConfigureAwait(false);

    public async Task<Envelope<Dictionary<string, object?>>> DeleteAsync(string idpId, CancellationToken ct = default) =>
        await HttpTransport.RequestAsync<Dictionary<string, object?>>(_c.Config, HttpMethod.Delete, UrlGrammar.OrgPath($"identity-providers/{HttpUtility.UrlEncode(idpId)}"), null, null, ct).ConfigureAwait(false);
}

public sealed class OrgSmtp
{
    private readonly K9oClient _c;
    internal OrgSmtp(K9oClient c) => _c = c;

    /// <summary>Set or update the org SMTP config: <c>PATCH /org</c> with <c>{ smtpConfig }</c>.</summary>
    public async Task<Envelope<Dictionary<string, object?>>> SetAsync(SetSmtpInput input, CancellationToken ct = default) =>
        await HttpTransport.RequestAsync<Dictionary<string, object?>>(_c.Config, HttpMethod.Patch, UrlGrammar.OrgPath("org"),
            new Dictionary<string, object?> { ["smtpConfig"] = input.ToMap() }, null, ct).ConfigureAwait(false);

    /// <summary>Send a diagnostic email to verify the config (<paramref name="subject"/> is optional).</summary>
    public async Task<Envelope<Dictionary<string, object?>>> TestAsync(string to, string? subject = null, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?> { ["to"] = to };
        if (subject != null) body["subject"] = subject;
        return await HttpTransport.RequestAsync<Dictionary<string, object?>>(_c.Config, HttpMethod.Post, UrlGrammar.OrgPath("org/smtp/test-email"), body, null, ct).ConfigureAwait(false);
    }

    /// <summary>Clear the org-level SMTP config: <c>PATCH /org</c> with <c>{ smtpConfig: null }</c>.</summary>
    public async Task<Envelope<Dictionary<string, object?>>> UnsetAsync(CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?> { ["smtpConfig"] = null };
        return await HttpTransport.RequestAsync<Dictionary<string, object?>>(_c.Config, HttpMethod.Patch, UrlGrammar.OrgPath("org"),
            body, null, ct).ConfigureAwait(false);
    }
}

/// <summary>
/// SMTP config, as the gateway takes it in the <c>smtpConfig</c> field of <c>PATCH /org</c>. The password is never
/// sent: <c>PasswordRef</c> is a reference to a stored secret. <c>FromName</c> may be null.
/// </summary>
public sealed class SetSmtpInput
{
    public required string Host { get; init; }
    public required int Port { get; init; }
    public required string Username { get; init; }
    public required string PasswordRef { get; init; }
    public required string FromAddress { get; init; }
    public string? FromName { get; init; }

    internal Dictionary<string, object?> ToMap()
    {
        var m = new Dictionary<string, object?>
        {
            ["host"] = Host, ["port"] = Port, ["username"] = Username, ["passwordRef"] = PasswordRef,
            ["fromAddress"] = FromAddress,
        };
        if (FromName != null) m["fromName"] = FromName;
        return m;
    }
}

public sealed class DefaultLlm
{
    private readonly K9oClient _client;
    internal DefaultLlm(K9oClient client) { _client = client; }

    /// <summary>Read the org-default LLM. <c>GET /default-llm</c>; any accepted member of the org may read it.</summary>
    public async Task<Envelope<DefaultLlmInfo>> GetAsync(CancellationToken ct = default) =>
        await HttpTransport.RequestAsync<DefaultLlmInfo>(_client.Config, HttpMethod.Get, UrlGrammar.OrgPath("default-llm"), null, null, ct).ConfigureAwait(false);

    /// <summary>Set the org-default LLM (<c>PATCH /default-llm</c>; ORG_ADMIN or ORG_OWNER; tier-gated).</summary>
    public async Task<Envelope<DefaultLlmInfo>> SetAsync(string modelId, CancellationToken ct = default) =>
        await HttpTransport.RequestAsync<DefaultLlmInfo>(_client.Config, HttpMethod.Patch, UrlGrammar.OrgPath("default-llm"),
            new Dictionary<string, string> { ["modelId"] = modelId }, null, ct).ConfigureAwait(false);
}

/// <summary>The org-default LLM: <c>{ orgId, defaultModelId, updatedAt, updatedBy }</c>.</summary>
public sealed class DefaultLlmInfo
{
    [JsonPropertyName("orgId")] public string OrgId { get; init; } = "";
    [JsonPropertyName("defaultModelId")] public string DefaultModelId { get; init; } = "";
    [JsonPropertyName("updatedAt")] public string? UpdatedAt { get; init; }
    [JsonPropertyName("updatedBy")] public string? UpdatedBy { get; init; }
}
