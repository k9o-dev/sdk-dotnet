using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace K9o;

/// <summary>
/// Project-plane audit log namespace — client.Audit.
/// Distinct from client.Org.AuditLog (org-plane) + client.Me.AuditLog.
/// </summary>
public sealed class Audit
{
    private readonly K9oClient _client;
    internal Audit(K9oClient client) => _client = client;

    private string RequireProjectSlug()
    {
        if (string.IsNullOrEmpty(_client.Config.ProjectSlug))
            throw new KError("K9O_MISSING_PROJECT_CONTEXT", "audit operations require ProjectSlug bound on the client");
        return _client.Config.ProjectSlug!;
    }

    public async Task<Envelope<List<AuditEvent>>> QueryAsync(AuditQueryOpts? opts = null, CancellationToken ct = default)
    {
        var slug = RequireProjectSlug();
        return await HttpTransport.RequestAsync<List<AuditEvent>>(_client.Config, HttpMethod.Get,
            UrlGrammar.ProjectPath(slug, "audit-log"), null, (opts ?? new AuditQueryOpts()).ToQuery(), ct).ConfigureAwait(false);
    }
}

public sealed class AuditEvent
{
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    [JsonPropertyName("type")] public string Type { get; init; } = "";
    [JsonPropertyName("actorId")] public string? ActorId { get; init; }
    [JsonPropertyName("targetKind")] public string? TargetKind { get; init; }
    [JsonPropertyName("targetId")] public string? TargetId { get; init; }
    [JsonPropertyName("payload")] public Dictionary<string, object?>? Payload { get; init; }
    [JsonPropertyName("occurredAt")] public string OccurredAt { get; init; } = "";
    [JsonPropertyName("requestId")] public string? RequestId { get; init; }
    [JsonPropertyName("traceId")] public string? TraceId { get; init; }
}

/// <summary>Optional audit-query filters (null fields are omitted).</summary>
public sealed class AuditQueryOpts
{
    public string? Since { get; init; }
    public string? Until { get; init; }
    public string? Type { get; init; }
    public string? ActorId { get; init; }
    public string? TargetKind { get; init; }
    public string? TargetId { get; init; }
    public int? Limit { get; init; }
    public string? Cursor { get; init; }

    internal Dictionary<string, string> ToQuery()
    {
        var q = new Dictionary<string, string>();
        if (Since != null) q["since"] = Since;
        if (Until != null) q["until"] = Until;
        if (Type != null) q["type"] = Type;
        if (ActorId != null) q["actorId"] = ActorId;
        if (TargetKind != null) q["targetKind"] = TargetKind;
        if (TargetId != null) q["targetId"] = TargetId;
        if (Limit != null) q["limit"] = Limit.Value.ToString();
        if (Cursor != null) q["cursor"] = Cursor;
        return q;
    }
}
