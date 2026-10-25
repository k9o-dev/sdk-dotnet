using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace K9o;

/// <summary>
/// Org-plane bound namespace — client.Org. Singular
/// bound-org namespace (distinct from client.Orgs collection): Members /
/// Groups / Smtp / AuditLog / Gate (Roles + IdpProviders) + DefaultLlm.
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

    public async Task<Envelope<List<Dictionary<string, object?>>>> ListAsync(string? status = null, CancellationToken ct = default)
    {
        var q = status != null ? new Dictionary<string, string> { ["status"] = status } : null;
        return await HttpTransport.RequestAsync<List<Dictionary<string, object?>>>(_c.Config, HttpMethod.Get, UrlGrammar.OrgPath("members"), null, q, ct).ConfigureAwait(false);
    }

    public async Task<Envelope<Dictionary<string, object?>>> InviteAsync(string email, string role, CancellationToken ct = default) =>
        await HttpTransport.RequestAsync<Dictionary<string, object?>>(_c.Config, HttpMethod.Post, UrlGrammar.OrgPath("members"),
            new Dictionary<string, object?> { ["email"] = email, ["role"] = role }, null, ct).ConfigureAwait(false);

    public async Task<Envelope<Dictionary<string, object?>>> GetAsync(string userId, CancellationToken ct = default) =>
        await HttpTransport.RequestAsync<Dictionary<string, object?>>(_c.Config, HttpMethod.Get, UrlGrammar.OrgPath($"members/{HttpUtility.UrlEncode(userId)}"), null, null, ct).ConfigureAwait(false);

    public async Task<Envelope<Dictionary<string, object?>>> RemoveAsync(string userId, CancellationToken ct = default) =>
        await HttpTransport.RequestAsync<Dictionary<string, object?>>(_c.Config, HttpMethod.Delete, UrlGrammar.OrgPath($"members/{HttpUtility.UrlEncode(userId)}"), null, null, ct).ConfigureAwait(false);
}

public sealed class OrgGroups
{
    private readonly K9oClient _c;
    internal OrgGroups(K9oClient c) => _c = c;

    public async Task<Envelope<List<Dictionary<string, object?>>>> ListAsync(CancellationToken ct = default) =>
        await HttpTransport.RequestAsync<List<Dictionary<string, object?>>>(_c.Config, HttpMethod.Get, UrlGrammar.OrgPath("groups"), null, null, ct).ConfigureAwait(false);

    public async Task<Envelope<Dictionary<string, object?>>> CreateAsync(string name, CancellationToken ct = default) =>
        await HttpTransport.RequestAsync<Dictionary<string, object?>>(_c.Config, HttpMethod.Post, UrlGrammar.OrgPath("groups"),
            new Dictionary<string, object?> { ["name"] = name }, null, ct).ConfigureAwait(false);

    public async Task<Envelope<Dictionary<string, object?>>> GetAsync(string groupId, CancellationToken ct = default) =>
        await HttpTransport.RequestAsync<Dictionary<string, object?>>(_c.Config, HttpMethod.Get, UrlGrammar.OrgPath($"groups/{HttpUtility.UrlEncode(groupId)}"), null, null, ct).ConfigureAwait(false);

    public async Task<Envelope<Dictionary<string, object?>>> DeleteAsync(string groupId, CancellationToken ct = default) =>
        await HttpTransport.RequestAsync<Dictionary<string, object?>>(_c.Config, HttpMethod.Delete, UrlGrammar.OrgPath($"groups/{HttpUtility.UrlEncode(groupId)}"), null, null, ct).ConfigureAwait(false);
}

public sealed class OrgAuditLog
{
    private readonly K9oClient _c;
    internal OrgAuditLog(K9oClient c) => _c = c;

    public async Task<Envelope<List<Dictionary<string, object?>>>> QueryAsync(AuditQueryOpts? opts = null, CancellationToken ct = default) =>
        await HttpTransport.RequestAsync<List<Dictionary<string, object?>>>(_c.Config, HttpMethod.Get, UrlGrammar.OrgPath("audit-log"), null, (opts ?? new AuditQueryOpts()).ToQuery(), ct).ConfigureAwait(false);
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

    public async Task<Envelope<List<Dictionary<string, object?>>>> ListAsync(string? userId = null, string? role = null, CancellationToken ct = default)
    {
        var q = new Dictionary<string, string>();
        if (userId != null) q["userId"] = userId;
        if (role != null) q["role"] = role;
        return await HttpTransport.RequestAsync<List<Dictionary<string, object?>>>(_c.Config, HttpMethod.Get, UrlGrammar.OrgPath("org-role-grants"), null, q, ct).ConfigureAwait(false);
    }

    public async Task<Envelope<Dictionary<string, object?>>> GrantAsync(string userId, string role, CancellationToken ct = default) =>
        await HttpTransport.RequestAsync<Dictionary<string, object?>>(_c.Config, HttpMethod.Post, UrlGrammar.OrgPath("org-role-grants"),
            new Dictionary<string, object?> { ["userId"] = userId, ["role"] = role }, null, ct).ConfigureAwait(false);

    public async Task<Envelope<Dictionary<string, object?>>> GetAsync(string grantId, CancellationToken ct = default) =>
        await HttpTransport.RequestAsync<Dictionary<string, object?>>(_c.Config, HttpMethod.Get, UrlGrammar.OrgPath($"org-role-grants/{HttpUtility.UrlEncode(grantId)}"), null, null, ct).ConfigureAwait(false);

    public async Task<Envelope<Dictionary<string, object?>>> RevokeAsync(string grantId, CancellationToken ct = default) =>
        await HttpTransport.RequestAsync<Dictionary<string, object?>>(_c.Config, HttpMethod.Delete, UrlGrammar.OrgPath($"org-role-grants/{HttpUtility.UrlEncode(grantId)}"), null, null, ct).ConfigureAwait(false);
}

public sealed class OrgIdpProviders
{
    private readonly K9oClient _c;
    internal OrgIdpProviders(K9oClient c) => _c = c;

    public async Task<Envelope<List<Dictionary<string, object?>>>> ListAsync(CancellationToken ct = default) =>
        await HttpTransport.RequestAsync<List<Dictionary<string, object?>>>(_c.Config, HttpMethod.Get, UrlGrammar.OrgPath("idp-providers"), null, null, ct).ConfigureAwait(false);

    public async Task<Envelope<Dictionary<string, object?>>> CreateAsync(string name, string kind, Dictionary<string, object?> config, CancellationToken ct = default) =>
        await HttpTransport.RequestAsync<Dictionary<string, object?>>(_c.Config, HttpMethod.Post, UrlGrammar.OrgPath("idp-providers"),
            new Dictionary<string, object?> { ["name"] = name, ["kind"] = kind, ["config"] = config }, null, ct).ConfigureAwait(false);

    public async Task<Envelope<Dictionary<string, object?>>> GetAsync(string idpId, CancellationToken ct = default) =>
        await HttpTransport.RequestAsync<Dictionary<string, object?>>(_c.Config, HttpMethod.Get, UrlGrammar.OrgPath($"idp-providers/{HttpUtility.UrlEncode(idpId)}"), null, null, ct).ConfigureAwait(false);

    public async Task<Envelope<Dictionary<string, object?>>> ConfigureAsync(string idpId, Dictionary<string, object?> patch, CancellationToken ct = default) =>
        await HttpTransport.RequestAsync<Dictionary<string, object?>>(_c.Config, HttpMethod.Patch, UrlGrammar.OrgPath($"idp-providers/{HttpUtility.UrlEncode(idpId)}"), patch, null, ct).ConfigureAwait(false);

    public async Task<Envelope<Dictionary<string, object?>>> DeleteAsync(string idpId, CancellationToken ct = default) =>
        await HttpTransport.RequestAsync<Dictionary<string, object?>>(_c.Config, HttpMethod.Delete, UrlGrammar.OrgPath($"idp-providers/{HttpUtility.UrlEncode(idpId)}"), null, null, ct).ConfigureAwait(false);
}

public sealed class OrgSmtp
{
    private readonly K9oClient _c;
    internal OrgSmtp(K9oClient c) => _c = c;

    public async Task<Envelope<Dictionary<string, object?>>> GetAsync(CancellationToken ct = default) =>
        await HttpTransport.RequestAsync<Dictionary<string, object?>>(_c.Config, HttpMethod.Get, UrlGrammar.OrgPath("org/smtp"), null, null, ct).ConfigureAwait(false);

    public async Task<Envelope<Dictionary<string, object?>>> SetAsync(SetSmtpInput input, CancellationToken ct = default) =>
        await HttpTransport.RequestAsync<Dictionary<string, object?>>(_c.Config, HttpMethod.Patch, UrlGrammar.OrgPath("org"),
            new Dictionary<string, object?> { ["smtpConfig"] = input.ToMap() }, null, ct).ConfigureAwait(false);

    public async Task<Envelope<Dictionary<string, object?>>> TestAsync(string to, string? fromOverride = null, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?> { ["to"] = to };
        if (fromOverride != null) body["fromOverride"] = fromOverride;
        return await HttpTransport.RequestAsync<Dictionary<string, object?>>(_c.Config, HttpMethod.Post, UrlGrammar.OrgPath("org/smtp/test-email"), body, null, ct).ConfigureAwait(false);
    }

    public async Task<Envelope<Dictionary<string, object?>>> UnsetAsync(CancellationToken ct = default) =>
        await HttpTransport.RequestAsync<Dictionary<string, object?>>(_c.Config, HttpMethod.Patch, UrlGrammar.OrgPath("org"),
            new Dictionary<string, object?> { ["smtpConfig"] = null }, null, ct).ConfigureAwait(false);
}

/// <summary>SMTP config input (password routed via Kibo enclave).</summary>
public sealed class SetSmtpInput
{
    public required string Host { get; init; }
    public required int Port { get; init; }
    public required string Username { get; init; }
    public required string Password { get; init; }
    public required string FromEmail { get; init; }
    public required string FromName { get; init; }
    public required string Encryption { get; init; }
    public string? ReplyToEmail { get; init; }

    internal Dictionary<string, object?> ToMap()
    {
        var m = new Dictionary<string, object?>
        {
            ["host"] = Host, ["port"] = Port, ["username"] = Username, ["password"] = Password,
            ["fromEmail"] = FromEmail, ["fromName"] = FromName, ["encryption"] = Encryption,
        };
        if (ReplyToEmail != null) m["replyToEmail"] = ReplyToEmail;
        return m;
    }
}

public sealed class DefaultLlm
{
    private readonly K9oClient _client;
    internal DefaultLlm(K9oClient client) { _client = client; }

    /// <summary>
    /// Read the org-default LLM model id (null when unset → gateway
    /// defaults to <c>mistral-7b</c>). ORG_ADMIN; tier-gated.
    /// </summary>
    public async Task<Envelope<string?>> GetAsync(CancellationToken ct = default) =>
        await HttpTransport.RequestAsync<string?>(_client.Config, HttpMethod.Get, UrlGrammar.OrgPath("org/default-llm"), null, null, ct).ConfigureAwait(false);

    /// <summary>Set the org-default LLM (ORG_ADMIN; tier-gated).</summary>
    public async Task<Envelope<string>> SetAsync(string modelId, CancellationToken ct = default) =>
        await HttpTransport.RequestAsync<string>(_client.Config, HttpMethod.Patch, UrlGrammar.OrgPath("org/default-llm"),
            new Dictionary<string, string> { ["modelId"] = modelId }, null, ct).ConfigureAwait(false);
}
