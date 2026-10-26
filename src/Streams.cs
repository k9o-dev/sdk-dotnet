using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace K9o;

/// <summary>
/// User data streams namespace — <c>client.Streams</c>.
///
/// <para>Control plane (<see cref="CreateAsync"/>, <see cref="ListAsync"/>, <see cref="GetAsync"/>,
/// <see cref="DeleteAsync"/>) and the data plane over <c>/@&lt;slug&gt;/data-streams/user/&lt;stream&gt;</c>:
/// <see cref="PublishAsync"/>, <see cref="ReplayAsync"/> (one bounded read) and <see cref="SubscribeAsync"/> (SSE live
/// tail). While the data plane is not available the gateway answers <c>K9O_STREAM_UNAVAILABLE</c> (503), thrown as a
/// <see cref="KError"/>.</para>
/// </summary>
public sealed class Streams
{
    private readonly K9oClient _client;
    internal Streams(K9oClient client) { _client = client; }

    private string Base() => UrlGrammar.ProjectPath(Studio.RequireProjectSlug(_client.Config, "streams"), "data-streams/user");

    private string One(string streamSlug) => $"{Base()}/{Uri.EscapeDataString(streamSlug)}";

    /// <summary>Create a stream. <paramref name="maxBytes"/> is the storage ceiling; <paramref name="retentionSeconds"/> may be null (gateway default).</summary>
    public Task<Envelope<UserStream>> CreateAsync(string displayName, long maxBytes, long? retentionSeconds = null, CancellationToken ct = default) =>
        HttpTransport.RequestAsync<UserStream>(_client.Config, HttpMethod.Post, Base(),
            new CreateStreamBody { DisplayName = displayName, MaxBytes = maxBytes, RetentionSeconds = retentionSeconds }, null, ct);

    /// <summary>List the project's streams.</summary>
    public Task<Envelope<List<UserStream>>> ListAsync(CancellationToken ct = default) =>
        HttpTransport.RequestAsync<List<UserStream>>(_client.Config, HttpMethod.Get, Base(), null, null, ct);

    /// <summary>Get one stream by slug.</summary>
    public Task<Envelope<UserStream>> GetAsync(string streamSlug, CancellationToken ct = default) =>
        HttpTransport.RequestAsync<UserStream>(_client.Config, HttpMethod.Get, One(streamSlug), null, null, ct);

    /// <summary>Retire a stream.</summary>
    public Task<Envelope<StreamDeleted>> DeleteAsync(string streamSlug, CancellationToken ct = default) =>
        HttpTransport.RequestAsync<StreamDeleted>(_client.Config, HttpMethod.Delete, One(streamSlug), null, null, ct);

    /// <summary>Append an event. <paramref name="data"/> is any JSON-serialisable value; <paramref name="subject"/> (nullable) is a dot-separated routing suffix.</summary>
    public Task<Envelope<PublishResult>> PublishAsync(string streamSlug, object? data, string? subject = null, CancellationToken ct = default)
    {
        var body = new Dictionary<string, object?> { ["data"] = data };
        if (subject != null) body["subject"] = subject;
        return HttpTransport.RequestAsync<PublishResult>(_client.Config, HttpMethod.Post, $"{One(streamSlug)}/publish", body, null, ct);
    }

    /// <summary>Read a bounded range of events in one call.</summary>
    public Task<Envelope<ReplayResult>> ReplayAsync(string streamSlug, long? fromSequence = null, long? untilSequence = null, int? limit = null, CancellationToken ct = default) =>
        HttpTransport.RequestAsync<ReplayResult>(_client.Config, HttpMethod.Post, $"{One(streamSlug)}/replay",
            new ReplayBody { FromSequence = fromSequence, UntilSequence = untilSequence, Limit = limit }, null, ct);

    /// <summary>
    /// Tail a stream over SSE. <paramref name="fromSequence"/> (nullable) resumes after that sequence. The server closes
    /// each connection after a bounded time; the enumeration reconnects from the last delivered sequence after
    /// <paramref name="reconnectDelay"/> (default one second) and stops when <paramref name="ct"/> is cancelled (the enumeration then throws <see cref="OperationCanceledException"/>, as <c>await foreach</c> does everywhere in .NET). A
    /// failure such as <c>K9O_STREAM_UNAVAILABLE</c> is thrown as a <see cref="KError"/> from the enumeration.
    /// </summary>
    public IAsyncEnumerable<StreamEvent> SubscribeAsync(string streamSlug, string? fromSequence = null, CancellationToken ct = default, TimeSpan? reconnectDelay = null)
    {
        var path = $"{One(streamSlug)}/subscribe";
        return Tail(path, fromSequence, reconnectDelay ?? TimeSpan.FromSeconds(1), ct);
    }

    private async IAsyncEnumerable<StreamEvent> Tail(string path, string? last, TimeSpan delay, [EnumeratorCancellation] CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var q = new Dictionary<string, string>();
            if (!string.IsNullOrEmpty(last)) q["fromSequence"] = last!;
            await foreach (var ev in HttpTransport.StreamSseAsync<StreamEvent>(
                _client.Config, HttpMethod.Get, path, null, q, ct, e => e.Sequence.Length > 0).ConfigureAwait(false))
            {
                last = ev.Sequence;
                yield return ev;
            }
            await Task.Delay(delay, ct).ConfigureAwait(false);
        }
    }
}

/// <summary>A user data stream as the gateway returns it. <c>MaxBytes</c> is null when there is no storage ceiling.</summary>
public sealed class UserStream
{
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    [JsonPropertyName("slug")] public string Slug { get; init; } = "";
    [JsonPropertyName("projectId")] public string ProjectId { get; init; } = "";
    [JsonPropertyName("displayName")] public string DisplayName { get; init; } = "";
    [JsonPropertyName("subjectPrefix")] public string SubjectPrefix { get; init; } = "";
    [JsonPropertyName("region")] public string Region { get; init; } = "";
    [JsonPropertyName("retentionSeconds")] public long RetentionSeconds { get; init; }
    [JsonPropertyName("retentionClass")] public string RetentionClass { get; init; } = "";
    [JsonPropertyName("maxBytes")] public long? MaxBytes { get; init; }
    [JsonPropertyName("status")] public string Status { get; init; } = "";
    [JsonPropertyName("createdAt")] public string CreatedAt { get; init; } = "";
    [JsonPropertyName("createdBy")] public string? CreatedBy { get; init; }
}

/// <summary>Result of <see cref="Streams.DeleteAsync"/>.</summary>
public sealed class StreamDeleted
{
    [JsonPropertyName("slug")] public string Slug { get; init; } = "";
    [JsonPropertyName("status")] public string Status { get; init; } = "";
}

/// <summary>Result of <see cref="Streams.PublishAsync"/>.</summary>
public sealed class PublishResult
{
    [JsonPropertyName("streamSlug")] public string StreamSlug { get; init; } = "";
    [JsonPropertyName("subject")] public string Subject { get; init; } = "";
    [JsonPropertyName("bytes")] public long Bytes { get; init; }
    [JsonPropertyName("accepted")] public bool Accepted { get; init; }
}

/// <summary>One event of a stream: <c>Sequence</c> is the stream sequence (a decimal string); <c>Data</c> the published JSON.</summary>
public sealed class StreamEvent
{
    [JsonPropertyName("sequence")] public string Sequence { get; init; } = "";
    [JsonPropertyName("subject")] public string Subject { get; init; } = "";
    [JsonPropertyName("data")] public object? Data { get; init; }
    [JsonPropertyName("publishedAt")] public string PublishedAt { get; init; } = "";
}

/// <summary>A page of events from <see cref="Streams.ReplayAsync"/>. Pass <c>NextCursor</c> as the next <c>fromSequence</c> while <c>HasMore</c>.</summary>
public sealed class ReplayResult
{
    [JsonPropertyName("streamSlug")] public string StreamSlug { get; init; } = "";
    [JsonPropertyName("events")] public List<StreamEvent> Events { get; init; } = new();
    [JsonPropertyName("nextCursor")] public string? NextCursor { get; init; }
    [JsonPropertyName("hasMore")] public bool HasMore { get; init; }
}

internal sealed class CreateStreamBody
{
    [JsonPropertyName("displayName")] public string DisplayName { get; init; } = "";
    [JsonPropertyName("maxBytes")] public long MaxBytes { get; init; }
    [JsonPropertyName("retentionSeconds")] public long? RetentionSeconds { get; init; }
}

internal sealed class ReplayBody
{
    [JsonPropertyName("fromSequence")] public long? FromSequence { get; init; }
    [JsonPropertyName("untilSequence")] public long? UntilSequence { get; init; }
    [JsonPropertyName("limit")] public int? Limit { get; init; }
}
