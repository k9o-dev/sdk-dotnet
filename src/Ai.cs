using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace K9o;

/// <summary>
/// <c>client.Ai.*</c> — platform AI primitives (<c>ai.embed</c> is a platform
/// primitive, not sugar over <c>context.embed</c>).
///
/// Wire mapping (project-plane): <c>EmbedAsync</c> → <c>POST /@&lt;slug&gt;/ai/embeddings</c>
/// body <c>{ model?, input: string | string[] }</c> → <c>{ data: { embeddings, model, dimensions }, meta }</c>.
///
/// <c>GenerateAsync</c> → <c>POST /@&lt;slug&gt;/ai/generate</c> body <c>{ prompt, model?, maxTokens?, temperature?, usageType? }</c>,
/// a verb over <c>Compute.Llms.InvokeAsync</c> that returns the same <see cref="LlmResponse"/>.
///
/// Bounds (enforced by the gateway, not here): at most 64 inputs, each non-empty, at most 256 KiB of
/// input per request, at most 4 requests in flight per project. The SDK sends the input exactly as
/// given, does no client-side content validation, and never puts the input in an error it constructs.
/// </summary>
public sealed class Ai
{
    /// <summary>The one declared embedding model today; a different model is refused with <c>K9O_MODEL_NOT_DECLARED</c>.</summary>
    public const string DefaultEmbeddingModel = "nomic-embed-text-v1.5";

    private readonly K9oClient _client;
    internal Ai(K9oClient client) { _client = client; }

    /// <summary>Embed one string. <paramref name="model"/> is omitted from the body when null (server default).</summary>
    public Task<Envelope<EmbedResult>> EmbedAsync(string input, string? model = null, CancellationToken ct = default) =>
        SendAsync(input, model, ct);

    /// <summary>Embed several strings; the result has one vector per string, in order.</summary>
    public Task<Envelope<EmbedResult>> EmbedAsync(IEnumerable<string> input, string? model = null, CancellationToken ct = default) =>
        SendAsync(input.ToArray(), model, ct);

    /// <summary>Run a prompt with the org's default LLM.</summary>
    public Task<Envelope<LlmResponse>> GenerateAsync(string prompt, CancellationToken ct = default) =>
        GenerateAsync(prompt, new GenerateOptions(), ct);

    /// <summary>
    /// <c>POST /@&lt;slug&gt;/ai/generate</c>. Unset options are not sent. The gateway answers 400 when no model is given and
    /// the org has no default LLM, and 501 <c>K9O_NOT_IMPLEMENTED</c> for outside-provider models.
    /// </summary>
    public async Task<Envelope<LlmResponse>> GenerateAsync(string prompt, GenerateOptions options, CancellationToken ct = default)
    {
        var slug = Studio.RequireProjectSlug(_client.Config, "ai");
        var body = new GenerateRequest
        {
            Prompt = prompt,
            Model = options.Model,
            MaxTokens = options.MaxTokens,
            Temperature = options.Temperature,
            UsageType = options.UsageType,
        };
        return await HttpTransport.RequestAsync<LlmResponse>(
            _client.Config, HttpMethod.Post, UrlGrammar.ProjectPath(slug, "ai/generate"), body, null, ct).ConfigureAwait(false);
    }

    private sealed class GenerateRequest
    {
        [JsonPropertyName("prompt")] public string Prompt { get; init; } = "";
        [JsonPropertyName("model")] public string? Model { get; init; }
        [JsonPropertyName("maxTokens")] public int? MaxTokens { get; init; }
        [JsonPropertyName("temperature")] public double? Temperature { get; init; }
        [JsonPropertyName("usageType")] public string? UsageType { get; init; }
    }

    private async Task<Envelope<EmbedResult>> SendAsync(object input, string? model, CancellationToken ct)
    {
        var slug = Studio.RequireProjectSlug(_client.Config, "ai");
        var body = new EmbedRequest { Model = model, Input = input };
        return await HttpTransport.RequestAsync<EmbedResult>(
            _client.Config, HttpMethod.Post, UrlGrammar.ProjectPath(slug, "ai/embeddings"), body, null, ct).ConfigureAwait(false);
    }

    private sealed class EmbedRequest
    {
        [JsonPropertyName("model")] public string? Model { get; init; }
        [JsonPropertyName("input")] public object Input { get; init; } = "";
    }
}

public sealed class EmbedResult
{
    /// <summary>One vector per input string, in input order.</summary>
    [JsonPropertyName("embeddings")] public List<List<double>> Embeddings { get; init; } = new();
    [JsonPropertyName("model")] public string Model { get; init; } = "";
    [JsonPropertyName("dimensions")] public int Dimensions { get; init; }
}

/// <summary>Optional fields of <c>Ai.GenerateAsync</c>; null fields are not sent.</summary>
public sealed class GenerateOptions
{
    /// <summary>Model id. When null the gateway uses the org's default LLM.</summary>
    public string? Model { get; init; }
    /// <summary>1 to 32000.</summary>
    public int? MaxTokens { get; init; }
    /// <summary>0 to 2.</summary>
    public double? Temperature { get; init; }
    /// <summary>Free-form usage label recorded with the call.</summary>
    public string? UsageType { get; init; }
}
