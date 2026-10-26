using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace K9o;

/// <summary>
/// Compute namespace (Compute LLMs registry).
/// <see cref="Llms.InvokeStreamAsync"/> returns
/// <see cref="IAsyncEnumerable{T}"/> for <c>await foreach</c> consumption.
/// </summary>
public sealed class Compute
{
    /// <summary>Curated closed set of model ids.</summary>
    public static readonly IReadOnlyList<string> LlmModelIds = new[]
    {
        "mistral-7b", "mistral-8x7b", "llama-3-8b", "llama-3-70b",
        "claude-3-5-sonnet", "gpt-4o", "gpt-4o-mini",
        "text-embedding-3-small", "text-embedding-3-large",
    };

    public Llms Llms { get; }

    internal Compute(K9oClient client) { Llms = new Llms(client); }
}

/// <summary>Model metadata, as the gateway lists it.</summary>
public sealed class LlmModel
{
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    [JsonPropertyName("provider")] public string Provider { get; init; } = "";
    [JsonPropertyName("modality")] public string Modality { get; init; } = "";
    [JsonPropertyName("minTier")] public string MinTier { get; init; } = "";
    [JsonPropertyName("inputCostPerMillion")] public double InputCostPerMillion { get; init; }
    [JsonPropertyName("outputCostPerMillion")] public double OutputCostPerMillion { get; init; }
}

/// <summary>Token counts the model server reported for a call.</summary>
public sealed class LlmUsage
{
    [JsonPropertyName("promptTokens")] public long PromptTokens { get; init; }
    [JsonPropertyName("completionTokens")] public long CompletionTokens { get; init; }
    [JsonPropertyName("totalTokens")] public long TotalTokens { get; init; }
}

/// <summary>Synchronous inference result from <c>/invoke</c>.</summary>
public sealed class LlmResponse
{
    [JsonPropertyName("modelId")] public string ModelId { get; init; } = "";
    /// <summary>The generated text.</summary>
    [JsonPropertyName("content")] public string Content { get; init; } = "";
    [JsonPropertyName("finishReason")] public string? FinishReason { get; init; }
    [JsonPropertyName("usage")] public LlmUsage? Usage { get; init; }
    [JsonPropertyName("usageType")] public string UsageType { get; init; } = "";
}

/// <summary>
/// One chunk of the <c>/invoke-stream</c> response: the model server's OpenAI-style chat-completion chunk, passed
/// through unchanged. Incremental text is <c>Choices[0].Delta.Content</c> (see <see cref="Text"/>); the final chunk may
/// carry <c>Usage</c>.
/// </summary>
public sealed class LlmStreamChunk
{
    [JsonPropertyName("id")] public string? Id { get; init; }
    [JsonPropertyName("model")] public string? Model { get; init; }
    [JsonPropertyName("choices")] public List<LlmStreamChoice>? Choices { get; init; }
    [JsonPropertyName("usage")] public Dictionary<string, object?>? Usage { get; init; }

    /// <summary>The text carried by this chunk, or an empty string.</summary>
    [JsonIgnore] public string Text => Choices is { Count: > 0 } c ? c[0].Delta?.Content ?? "" : "";
}

public sealed class LlmStreamChoice
{
    [JsonPropertyName("index")] public int? Index { get; init; }
    [JsonPropertyName("delta")] public LlmStreamDelta? Delta { get; init; }
    [JsonPropertyName("finish_reason")] public string? FinishReason { get; init; }
}

public sealed class LlmStreamDelta
{
    [JsonPropertyName("role")] public string? Role { get; init; }
    [JsonPropertyName("content")] public string? Content { get; init; }
}

/// <summary>
/// Input for invoke / invoke-stream. The gateway reads <c>prompt</c> and, flat in the same body, the optional
/// <c>maxTokens</c> (1 to 32000), <c>temperature</c> (0 to 2) and <c>usageType</c>; <see cref="Params"/> carries those
/// optional keys and is merged into the body.
/// </summary>
public sealed class LlmInvokeInput
{
    public required string Prompt { get; init; }
    public Dictionary<string, object?>? Params { get; init; }

    internal Dictionary<string, object?> ToBody()
    {
        var body = new Dictionary<string, object?> { ["prompt"] = Prompt };
        if (Params != null) foreach (var (k, v) in Params) body[k] = v;
        return body;
    }
}

public sealed class Llms
{
    private readonly K9oClient _client;
    internal Llms(K9oClient client) { _client = client; }

    /// <summary>List the models available to the project tier. <c>GET /@&lt;slug&gt;/compute/llms</c> (the route takes no filters).</summary>
    public async Task<Envelope<List<LlmModel>>> ListAsync(CancellationToken ct = default)
    {
        var slug = Studio.RequireProjectSlug(_client.Config, "compute.llms");
        return await HttpTransport.RequestAsync<List<LlmModel>>(
            _client.Config, HttpMethod.Get, UrlGrammar.ProjectPath(slug, "compute/llms"), null, null, ct).ConfigureAwait(false);
    }

    public async Task<Envelope<LlmModel>> ReadAsync(string modelId, CancellationToken ct = default)
    {
        var slug = Studio.RequireProjectSlug(_client.Config, "compute.llms");
        return await HttpTransport.RequestAsync<LlmModel>(
            _client.Config, HttpMethod.Get,
            UrlGrammar.ProjectPath(slug, $"compute/llms/{HttpUtility.UrlEncode(modelId)}"), null, null, ct).ConfigureAwait(false);
    }

    /// <summary>Synchronous inference (PROJECT_AUTHOR/ADMIN).</summary>
    public async Task<Envelope<LlmResponse>> InvokeAsync(string modelId, LlmInvokeInput input, CancellationToken ct = default)
    {
        var slug = Studio.RequireProjectSlug(_client.Config, "compute.llms");
        return await HttpTransport.RequestAsync<LlmResponse>(
            _client.Config, HttpMethod.Post,
            UrlGrammar.ProjectPath(slug, $"compute/llms/{HttpUtility.UrlEncode(modelId)}/invoke"), input.ToBody(), null, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Streaming inference (SSE; the model server's OpenAI-style chunks). Consume via
    /// <c>await foreach (var chunk in client.Compute.Llms.InvokeStreamAsync(...))</c>.
    /// </summary>
    public IAsyncEnumerable<LlmStreamChunk> InvokeStreamAsync(string modelId, LlmInvokeInput input, CancellationToken ct = default)
    {
        var slug = Studio.RequireProjectSlug(_client.Config, "compute.llms");
        return HttpTransport.StreamSseAsync<LlmStreamChunk>(
            _client.Config, HttpMethod.Post,
            UrlGrammar.ProjectPath(slug, $"compute/llms/{HttpUtility.UrlEncode(modelId)}/invoke-stream"),
            input.ToBody(), null, ct);
    }
}
