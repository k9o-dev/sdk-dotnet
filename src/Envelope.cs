using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace K9o;

/// <summary>
/// Standard envelope returned by every k9o surface on success.
/// Generic over the per-method data shape T.
/// </summary>
public class Envelope<T>
{
    [JsonPropertyName("data")]
    public T? Data { get; init; }

    [JsonPropertyName("meta")]
    public Meta Meta { get; init; } = new();
}

/// <summary>Structured meta accessor sugar.</summary>
public class Meta
{
    [JsonPropertyName("cursor")]
    public MetaCursor? Cursor { get; init; }

    [JsonPropertyName("subscription")]
    public MetaSubscription? Subscription { get; init; }

    [JsonPropertyName("idempotency")]
    public MetaIdempotency? Idempotency { get; init; }

    [JsonPropertyName("warnings")]
    public List<MetaWarning> Warnings { get; init; } = new();

    [JsonPropertyName("cost")]
    public Dictionary<string, object?> Cost { get; init; } = new();

    [JsonPropertyName("timing")]
    public MetaTiming? Timing { get; init; }

    [JsonPropertyName("context")]
    public MetaContext? Context { get; init; }
}

public class MetaCursor
{
    [JsonPropertyName("next")]
    public string? Next { get; init; }

    [JsonPropertyName("totalCount")]
    public long? TotalCount { get; init; }
}

public class MetaSubscription
{
    [JsonPropertyName("isInitial")]
    public bool IsInitial { get; init; }
}

public class MetaIdempotency
{
    [JsonPropertyName("replayed")]
    public bool Replayed { get; init; }
}

public class MetaWarning
{
    [JsonPropertyName("code")]
    public string Code { get; init; } = "";

    [JsonPropertyName("message")]
    public string Message { get; init; } = "";
}

public class MetaTiming
{
    // Fractional (sub-ms) — the gateway emits e.g. 10.572.
    [JsonPropertyName("totalMs")]
    public double TotalMs { get; init; }
}

public class MetaContext
{
    [JsonPropertyName("requestId")]
    public string RequestId { get; init; } = "";
}
