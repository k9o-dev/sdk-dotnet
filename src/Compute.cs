using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace K9o;

/// <summary>
/// Compute namespace (Compute LLMs registry).
/// .NET idiom +
/// — <see cref="Llms.InvokeStreamAsync"/> returns
/// <see cref="IAsyncEnumerable{T}"/> for <c>await foreach</c> consumption.
/// </summary>
public sealed class Compute
{
    /// <summary>Curated 9-model closed enum (extensible v2.x).</summary>
    public static readonly IReadOnlyList<string> LlmModelIds = new[]
    {
        "mistral-7b", "mistral-8x7b", "llama-3-8b", "llama-3-70b",
        "claude-3-5-sonnet", "gpt-4o", "gpt-4o-mini",
        "text-embedding-3-small", "text-embedding-3-large",
    };

    public Llms Llms { get; }

    internal Compute(K9oClient client) { Llms = new Llms(client); }
}

public sealed class LlmModel
{
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    [JsonPropertyName("providerClass")] public string ProviderClass { get; init; } = "";
    [JsonPropertyName("modality")] public string Modality { get; init; } = "";
    [JsonPropertyName("tierGate")] public string TierGate { get; init; } = "";
    [JsonPropertyName("contextWindow")] public long ContextWindow { get; init; }
    [JsonPropertyName("capabilities")] public List<string>? Capabilities { get; init; }
    [JsonPropertyName("costPerMillionInputTokens")] public double CostPerMillionInputTokens { get; init; }
    [JsonPropertyName("costPerMillionOutputTokens")] public double CostPerMillionOutputTokens { get; init; }
}

public sealed class LlmResponse
{
    [JsonPropertyName("completion")] public string Completion { get; init; } = "";
    [JsonPropertyName("tokensUsed")] public long TokensUsed { get; init; }
    [JsonPropertyName("latencyMs")] public long LatencyMs { get; init; }
}

public sealed class LlmStreamChunk
{
    [JsonPropertyName("delta")] public string Delta { get; init; } = "";
    [JsonPropertyName("done")] public bool Done { get; init; }
    [JsonPropertyName("tokensUsed")] public long TokensUsed { get; init; }
}

public sealed class LlmInvokeInput
{
    [JsonPropertyName("prompt")] public required string Prompt { get; init; }
    [JsonPropertyName("params")] public Dictionary<string, object?>? Params { get; init; }
}

public sealed class Llms
{
    private readonly K9oClient _client;
    internal Llms(K9oClient client) { _client = client; }

    public async Task<Envelope<List<LlmModel>>> ListAsync(string? modality = null, CancellationToken ct = default)
    {
        var slug = Studio.RequireProjectSlug(_client.Config, "compute.llms");
        var q = modality != null ? new Dictionary<string, string> { ["modality"] = modality } : null;
        return await HttpTransport.RequestAsync<List<LlmModel>>(
            _client.Config, HttpMethod.Get, UrlGrammar.ProjectPath(slug, "compute/llms"), null, q, ct).ConfigureAwait(false);
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
            UrlGrammar.ProjectPath(slug, $"compute/llms/{HttpUtility.UrlEncode(modelId)}/invoke"), input, null, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Streaming inference (SSE per-token). Consume via
    /// <c>await foreach (var chunk in client.Compute.Llms.InvokeStreamAsync(...))</c>.
    /// </summary>
    public IAsyncEnumerable<LlmStreamChunk> InvokeStreamAsync(string modelId, LlmInvokeInput input, CancellationToken ct = default)
    {
        var slug = Studio.RequireProjectSlug(_client.Config, "compute.llms");
        return HttpTransport.StreamSseAsync<LlmStreamChunk>(
            _client.Config, HttpMethod.Post,
            UrlGrammar.ProjectPath(slug, $"compute/llms/{HttpUtility.UrlEncode(modelId)}/invoke-stream"),
            input, null, ct);
    }
}
