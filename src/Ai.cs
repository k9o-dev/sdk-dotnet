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
