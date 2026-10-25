using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace K9o;

/// <summary>
/// Realtime Subscriptions namespace (ALL-LIVE primitive).
///
/// <para>.NET idiom: <see cref="SubscribeAsync"/> returns
/// <see cref="IAsyncEnumerable{T}"/> iterated via <c>await foreach</c>.
/// Per + the SDK-abstracted-primitive discipline the SDK
/// picks the transport; SPLIT (polling) is the internal fallback. The
/// HttpClient-based SDK uses SSE primary (no stdlib WebSocket); the
/// consumer's <c>await foreach</c> is transport-agnostic. Poll fallback
/// is exposed via <see cref="SubscribePollAsync"/>.</para>
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

    /// <summary>Subscribe via SSE transport (primary).</summary>
    public IAsyncEnumerable<RealtimeEvent> SubscribeAsync(string topic, Dictionary<string, object?>? filter = null, CancellationToken ct = default)
    {
        var slug = Studio.RequireProjectSlug(_client.Config, "realtime");
        var q = new Dictionary<string, string> { ["topic"] = topic };
        if (filter != null) q["filter"] = JsonSerializer.Serialize(filter);
        return HttpTransport.StreamSseAsync<RealtimeEvent>(
            _client.Config, HttpMethod.Get,
            UrlGrammar.ProjectPath(slug, "realtime/subscribe"), null, q, ct);
    }

    /// <summary>
    /// One-shot historical replay (capped 10k server-side).
    /// Use the highest observed <c>Sequence</c> as the next
    /// <c>fromSequence</c> for cursor pagination.
    /// </summary>
    public async Task<Envelope<List<RealtimeEvent>>> ReplayAsync(ReplayInput input, CancellationToken ct = default)
    {
        var slug = Studio.RequireProjectSlug(_client.Config, "realtime");
        if (input.FromSequence < 0)
        {
            throw new KError(
                "K9O_INVALID_FILTER",
                "Replay FromSequence must be a non-negative integer",
                new Dictionary<string, object?> { ["fromSequence"] = input.FromSequence },
                "Pass the highest sequence number you have already observed (or 0 to replay from the start).",
                null, null
            );
        }
        return await HttpTransport.RequestAsync<List<RealtimeEvent>>(
            _client.Config, HttpMethod.Post,
            UrlGrammar.ProjectPath(slug, "realtime/replay"), input, null, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Poll-transport fallback (SDK-internal SPLIT). Returns a
    /// single batch of pending events via the replay endpoint; caller can
    /// loop tracking the highest observed <c>Sequence</c>.
    /// </summary>
    public async Task<Envelope<List<RealtimeEvent>>> SubscribePollAsync(string topic, long fromSequence, Dictionary<string, object?>? filter = null, CancellationToken ct = default)
    {
        var slug = Studio.RequireProjectSlug(_client.Config, "realtime");
        var body = new ReplayInput { Topic = topic, FromSequence = fromSequence, Filter = filter };
        return await HttpTransport.RequestAsync<List<RealtimeEvent>>(
            _client.Config, HttpMethod.Post,
            UrlGrammar.ProjectPath(slug, "realtime/replay"), body, null, ct).ConfigureAwait(false);
    }
}

public sealed class RealtimeEvent
{
    [JsonPropertyName("topic")] public string Topic { get; init; } = "";
    [JsonPropertyName("type")] public string Type { get; init; } = "";
    [JsonPropertyName("sequence")] public long Sequence { get; init; }
    [JsonPropertyName("payload")] public object? Payload { get; init; }
    [JsonPropertyName("occurredAt")] public string OccurredAt { get; init; } = "";
}

public sealed class ReplayInput
{
    [JsonPropertyName("topic")] public required string Topic { get; init; }
    [JsonPropertyName("fromSequence")] public required long FromSequence { get; init; }
    [JsonPropertyName("filter")] public Dictionary<string, object?>? Filter { get; init; }
    [JsonPropertyName("untilSequence")] public long? UntilSequence { get; init; }
    [JsonPropertyName("limit")] public int? Limit { get; init; }
}
