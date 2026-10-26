using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace K9o;

/// <summary>
/// Project-plane audit log namespace — client.Audit.
/// Distinct from client.Org.AuditLog (org-plane).
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

/// <summary>
/// Optional audit-query filters (null fields are omitted); names are the gateway's query parameters.
/// <c>EventType</c> is the audit-event name (for example <c>data.write.access.insert</c>); <c>After</c> and
/// <c>Before</c> are ISO 8601 instants.
/// </summary>
public sealed class AuditQueryOpts
{
    public string? EventType { get; init; }
    public string? ActorId { get; init; }
    public string? After { get; init; }
    public string? Before { get; init; }
    public int? Limit { get; init; }
    public string? Cursor { get; init; }

    internal Dictionary<string, string> ToQuery()
    {
        var q = new Dictionary<string, string>();
        if (EventType != null) q["eventType"] = EventType;
        if (ActorId != null) q["actorId"] = ActorId;
        if (After != null) q["after"] = After;
        if (Before != null) q["before"] = Before;
        if (Limit != null) q["limit"] = Limit.Value.ToString();
        if (Cursor != null) q["cursor"] = Cursor;
        return q;
    }
}
