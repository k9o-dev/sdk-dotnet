using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace K9o;

/// <summary>
/// Operator-self namespace — client.Me. Cross-org;
/// org-plane routes under /me. Sub-namespaces: Profile / Sessions /
/// ApiTokens / Notifications / AuditLog / DataExport.
/// </summary>
public sealed class Me
{
    public MeProfile Profile { get; }
    public MeSessions Sessions { get; }
    public MeApiTokens ApiTokens { get; }
    public MeNotifications Notifications { get; }
    public MeAuditLog AuditLog { get; }
    public MeDataExport DataExport { get; }

    internal Me(K9oClient client)
    {
        Profile = new MeProfile(client);
        Sessions = new MeSessions(client);
        ApiTokens = new MeApiTokens(client);
        Notifications = new MeNotifications(client);
        AuditLog = new MeAuditLog(client);
        DataExport = new MeDataExport(client);
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

    public async Task<Envelope<Dictionary<string, object?>>> RevokeAsync(string sessionId, CancellationToken ct = default) =>
        await HttpTransport.RequestAsync<Dictionary<string, object?>>(_c.Config, HttpMethod.Delete, UrlGrammar.OrgPath($"me/sessions/{HttpUtility.UrlEncode(sessionId)}"), null, null, ct).ConfigureAwait(false);

    public async Task<Envelope<Dictionary<string, object?>>> RevokeAllAsync(CancellationToken ct = default) =>
        await HttpTransport.RequestAsync<Dictionary<string, object?>>(_c.Config, HttpMethod.Delete, UrlGrammar.OrgPath("me/sessions"), null, null, ct).ConfigureAwait(false);
}

public sealed class MeApiTokens
{
    private readonly K9oClient _c;
    internal MeApiTokens(K9oClient c) => _c = c;

    public async Task<Envelope<List<Dictionary<string, object?>>>> ListAsync(CancellationToken ct = default) =>
        await HttpTransport.RequestAsync<List<Dictionary<string, object?>>>(_c.Config, HttpMethod.Get, UrlGrammar.OrgPath("me/api-tokens"), null, null, ct).ConfigureAwait(false);

    public async Task<Envelope<Dictionary<string, object?>>> CreateAsync(string name, int? expiresInDays = null, string? scope = null, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?> { ["name"] = name };
        if (expiresInDays != null) body["expiresInDays"] = expiresInDays;
        if (scope != null) body["scope"] = scope;
        return await HttpTransport.RequestAsync<Dictionary<string, object?>>(_c.Config, HttpMethod.Post, UrlGrammar.OrgPath("me/api-tokens"), body, null, ct).ConfigureAwait(false);
    }

    public async Task<Envelope<Dictionary<string, object?>>> DeleteAsync(string tokenId, CancellationToken ct = default) =>
        await HttpTransport.RequestAsync<Dictionary<string, object?>>(_c.Config, HttpMethod.Delete, UrlGrammar.OrgPath($"me/api-tokens/{HttpUtility.UrlEncode(tokenId)}"), null, null, ct).ConfigureAwait(false);
}

public sealed class MeNotifications
{
    private readonly K9oClient _c;
    internal MeNotifications(K9oClient c) => _c = c;

    public async Task<Envelope<List<Dictionary<string, object?>>>> ListAsync(bool unreadOnly = false, int? limit = null, CancellationToken ct = default)
    {
        var q = new Dictionary<string, string>();
        if (unreadOnly) q["unreadOnly"] = "true";
        if (limit != null) q["limit"] = limit.Value.ToString();
        return await HttpTransport.RequestAsync<List<Dictionary<string, object?>>>(_c.Config, HttpMethod.Get, UrlGrammar.OrgPath("me/notifications"), null, q, ct).ConfigureAwait(false);
    }

    public async Task<Envelope<Dictionary<string, object?>>> MarkReadAsync(string notificationId, CancellationToken ct = default) =>
        await HttpTransport.RequestAsync<Dictionary<string, object?>>(_c.Config, HttpMethod.Patch, UrlGrammar.OrgPath($"me/notifications/{HttpUtility.UrlEncode(notificationId)}"),
            new Dictionary<string, object?> { ["read"] = true }, null, ct).ConfigureAwait(false);

    public async Task<Envelope<Dictionary<string, object?>>> MarkAllReadAsync(CancellationToken ct = default) =>
        await HttpTransport.RequestAsync<Dictionary<string, object?>>(_c.Config, HttpMethod.Patch, UrlGrammar.OrgPath("me/notifications"),
            new Dictionary<string, object?> { ["read"] = true }, null, ct).ConfigureAwait(false);
}

public sealed class MeAuditLog
{
    private readonly K9oClient _c;
    internal MeAuditLog(K9oClient c) => _c = c;

    public async Task<Envelope<List<Dictionary<string, object?>>>> QueryAsync(AuditQueryOpts? opts = null, CancellationToken ct = default) =>
        await HttpTransport.RequestAsync<List<Dictionary<string, object?>>>(_c.Config, HttpMethod.Get, UrlGrammar.OrgPath("me/audit-log"), null, (opts ?? new AuditQueryOpts()).ToQuery(), ct).ConfigureAwait(false);
}

public sealed class MeDataExport
{
    private readonly K9oClient _c;
    internal MeDataExport(K9oClient c) => _c = c;

    public async Task<Envelope<Dictionary<string, object?>>> RequestAsync(string format = "json", CancellationToken ct = default) =>
        await HttpTransport.RequestAsync<Dictionary<string, object?>>(_c.Config, HttpMethod.Post, UrlGrammar.OrgPath("me/data-export"),
            new Dictionary<string, object?> { ["format"] = format }, null, ct).ConfigureAwait(false);

    public async Task<Envelope<Dictionary<string, object?>>> StatusAsync(string jobId, CancellationToken ct = default) =>
        await HttpTransport.RequestAsync<Dictionary<string, object?>>(_c.Config, HttpMethod.Get, UrlGrammar.OrgPath($"me/data-export/{HttpUtility.UrlEncode(jobId)}"), null, null, ct).ConfigureAwait(false);
}
