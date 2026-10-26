using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace K9o;

/// <summary>
/// Realtime subscriptions namespace.
///
/// <para><see cref="SubscribeAsync"/> returns <see cref="IAsyncEnumerable{T}"/> iterated via <c>await foreach</c>. The
/// transport is SSE over <c>GET /@&lt;slug&gt;/realtime/subscribe</c>. The gateway serves no WebSocket upgrade and no
/// durable replay, so neither is offered; after an <c>event: resync</c> frame the caller re-reads current state from
/// the data plane and resubscribes. <c>resync</c> control frames are not events and are skipped.</para>
/// </summary>
public sealed class Realtime
{
    private readonly K9oClient _client;
    internal Realtime(K9oClient client) { _client = client; }

    /// <summary>Typed builders for the 6 closed topic families.</summary>
    public static class Topic
    {
        public static string Collection(string collection) => $"collections.{collection}.*";
        public static string WorkflowRuns() => "studio.workflows.runs.*";
        public static string QueryExecuted(string queryId) => $"studio.queries.executed.{queryId}";
        public static string DashboardRefresh(string dashboardId) => $"studio.dashboards.refresh.{dashboardId}";
        public static string FunctionInvocations(string functionId) => $"forge.functions.invocations.{functionId}";
        public static string AuditOrg(string eventFamily) => $"audit.org.{eventFamily}";
    }

    /// <summary>
    /// Subscribe over SSE. <paramref name="filter"/> (nullable) narrows the topic and composes with row-level security.
    /// </summary>
    public IAsyncEnumerable<RealtimeEvent> SubscribeAsync(string topic, Dictionary<string, object?>? filter = null, CancellationToken ct = default)
    {
        var slug = Studio.RequireProjectSlug(_client.Config, "realtime");
        var q = new Dictionary<string, string> { ["topic"] = topic };
        if (filter != null) q["filter"] = JsonSerializer.Serialize(filter);
        return HttpTransport.StreamSseAsync<RealtimeEvent>(
            _client.Config, HttpMethod.Get,
            UrlGrammar.ProjectPath(slug, "realtime/subscribe"), null, q, ct, e => e.EventType.Length > 0);
    }
}

/// <summary>A single realtime event, as the gateway frames it (wire spelling): topic, eventType, payload, occurredAt.</summary>
public sealed class RealtimeEvent
{
    [JsonPropertyName("topic")] public string Topic { get; init; } = "";
    [JsonPropertyName("eventType")] public string EventType { get; init; } = "";
    [JsonPropertyName("payload")] public object? Payload { get; init; }
    [JsonPropertyName("occurredAt")] public string OccurredAt { get; init; } = "";
}
