using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace K9o;

/// <summary>
/// k9o — Kilimanjaro platform SDK for .NET.
///
/// Idiomatic .NET: PascalCase methods + throw
/// <see cref="KError"/> on error + HttpClient transport.
///
/// <example>
/// <code>
/// var k9o = K9oClient.Create(new K9oOptions {
///     BaseUrl = "https://acme.api.k9o.io",
///     ApiKey = Environment.GetEnvironmentVariable("K9O_API_KEY"),
///     OrgSlug = "acme",
///     ProjectSlug = "member-portal",
/// });
///
/// var response = await k9o.From("posts").Select().Limit(10).ExecuteAsync&lt;Post&gt;();
/// foreach (var row in response.Data!) Console.WriteLine(row);
/// </code>
/// </example>
/// </summary>
public sealed class K9oClient
{
    internal readonly HttpClientConfig Config;

    public Projects Projects { get; }
    public Gate Gate { get; }
    public Studio Studio { get; }
    public Compute Compute { get; }
    public Ai Ai { get; }
    public Realtime Realtime { get; }
    public Streams Streams { get; }
    public Org Org { get; }
    public Orgs Orgs { get; }
    public Migrations Migrations { get; }
    public Functions Functions { get; }
    public Policies Policies { get; }
    public Approvals Approvals { get; }
    public ApprovalPolicies ApprovalPolicies { get; }
    public Schema Schema { get; }
    public Audit Audit { get; }
    public Me Me { get; }
    public Vault Vault { get; }
    public Storage Storage { get; }

    private K9oClient(HttpClientConfig config)
    {
        Config = config;
        Projects = new Projects(this);
        Gate = new Gate(this);
        Studio = new Studio(this);
        Compute = new Compute(this);
        Ai = new Ai(this);
        Realtime = new Realtime(this);
        Streams = new Streams(this);
        Org = new Org(this);
        Orgs = new Orgs(this);
        Migrations = new Migrations(this);
        Functions = new Functions(this);
        Policies = new Policies(this);
        Approvals = new Approvals(this);
        ApprovalPolicies = new ApprovalPolicies(this);
        Schema = new Schema(this);
        Audit = new Audit(this);
        Me = new Me(this);
        Vault = new Vault(this);
        Storage = new Storage(this);
    }

    /// <summary>
    /// Construct a K9oClient bound to the supplied options. Resolves the
    /// effective <c>BaseUrl</c> (an omitted baseUrl defaults to the public cloud;
    /// a platform host (<c>api.&lt;apex&gt;</c>) + <c>OrgSlug</c>
    /// derives the canonical org host; any other host passes through).
    /// Validates the URL; throws <see cref="KError"/> with
    /// <c>K9O_INVALID_BASE_URL</c> on violation.
    /// </summary>
    public static K9oClient Create(K9oOptions options)
    {
        var normalised = UrlGrammar.ResolveBaseUrl(options.BaseUrl, options.OrgSlug);
        return new K9oClient(new HttpClientConfig
        {
            BaseUrl = normalised,
            ApiKey = options.ApiKey,
            OrgSlug = options.OrgSlug,
            ProjectSlug = options.ProjectSlug,
            DefaultHeaders = options.DefaultHeaders ?? new Dictionary<string, string>(),
            HttpClient = options.HttpClient ?? new HttpClient(),
            ProbeClient = options.HttpClient ?? Region.NoRedirectClient,
            Retry = options.RetryConfig ?? new RetryConfig(),
        });
    }

    /// <summary>Data-plane DSL entry point.</summary>
    public Data.FromBuilder From(string collection) =>
        new(this, collection);

    /// <summary>
    /// Run one read-only SQL statement with positional parameters (<c>$1, $2, ...</c>); the SQL is sent as written.
    /// Values are strings, numbers, booleans or null. Rows come back as JSON objects.
    /// </summary>
    public Task<Envelope<List<Dictionary<string, System.Text.Json.JsonElement>>>> QueryAsync(string sql, IReadOnlyList<object?> parameters, CancellationToken ct = default) =>
        Query.RunAsync<Dictionary<string, System.Text.Json.JsonElement>>(this, sql, parameters ?? Array.Empty<object?>(), null, ct);

    /// <summary>Run one read-only SQL statement with named parameters: <c>:name</c> in the SQL is bound to the dictionary's value.</summary>
    public Task<Envelope<List<Dictionary<string, System.Text.Json.JsonElement>>>> QueryAsync(string sql, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct = default) =>
        Query.RunAsync<Dictionary<string, System.Text.Json.JsonElement>>(this, sql, null, parameters ?? new Dictionary<string, object?>(), ct);

    /// <summary>Run one read-only SQL statement with no parameters.</summary>
    public Task<Envelope<List<Dictionary<string, System.Text.Json.JsonElement>>>> QueryAsync(string sql, CancellationToken ct = default) =>
        Query.RunAsync<Dictionary<string, System.Text.Json.JsonElement>>(this, sql, Array.Empty<object?>(), null, ct);

    /// <summary><see cref="QueryAsync(string, IReadOnlyList{object?}, CancellationToken)"/> with rows bound to <typeparamref name="T"/>.</summary>
    public Task<Envelope<List<T>>> QueryAsync<T>(string sql, IReadOnlyList<object?> parameters, CancellationToken ct = default) =>
        Query.RunAsync<T>(this, sql, parameters ?? Array.Empty<object?>(), null, ct);

    /// <summary><see cref="QueryAsync(string, IReadOnlyDictionary{string, object?}, CancellationToken)"/> with rows bound to <typeparamref name="T"/>.</summary>
    public Task<Envelope<List<T>>> QueryAsync<T>(string sql, IReadOnlyDictionary<string, object?> parameters, CancellationToken ct = default) =>
        Query.RunAsync<T>(this, sql, null, parameters ?? new Dictionary<string, object?>(), ct);

    /// <summary>
    /// Full-text search over one text column of a collection: <c>From(collection).Select().TextSearch(...)</c> plus the page
    /// <c>Limit</c> / <c>Cursor</c>. Best matches first.
    /// </summary>
    public Task<Envelope<List<T>>> SearchAsync<T>(string collection, SearchOptions options, CancellationToken ct = default)
    {
        if (options == null) throw new KError("K9O_INVALID_FIELD_VALUE", "search requires a column and a query");
        var b = From(collection).Select().TextSearch(options.Column, options.Q, options.Language ?? "english", options.Type ?? "plain");
        if (options.Limit.HasValue) b.Limit(options.Limit.Value);
        if (options.Cursor != null) b.Cursor(options.Cursor);
        return b.ExecuteAsync<T>(ct);
    }

    /// <summary><see cref="SearchAsync{T}"/> with rows as JSON objects.</summary>
    public Task<Envelope<List<Dictionary<string, System.Text.Json.JsonElement>>>> SearchAsync(string collection, SearchOptions options, CancellationToken ct = default) =>
        SearchAsync<Dictionary<string, System.Text.Json.JsonElement>>(collection, options, ct);

    /// <summary>Bound baseUrl (read-only access for advanced use).</summary>
    public string BaseUrl => Config.BaseUrl;
}

/// <summary>Options for <see cref="K9oClient.Create"/>.</summary>
public sealed class K9oOptions
{
    /// <summary>
    /// Gateway host root (scheme + host [+ port]). OPTIONAL:
    /// when omitted, defaults to the public cloud; on a platform host
    /// (<c>api.&lt;apex&gt;</c>) with <see cref="OrgSlug"/> the org host
    /// <c>&lt;orgSlug&gt;.api.&lt;apex&gt;</c> is derived.
    /// </summary>
    public string? BaseUrl { get; init; }
    public string? ApiKey { get; init; }
    public string? OrgSlug { get; init; }
    public string? ProjectSlug { get; init; }
    public Dictionary<string, string>? DefaultHeaders { get; init; }
    public HttpClient? HttpClient { get; init; }

    /// <summary>
    /// Retry/backoff policy; null ⇒ defaults (3 retries,
    /// exponential) on idempotent methods + transient failures.
    /// </summary>
    public RetryConfig? RetryConfig { get; init; }
}

/// <summary>
/// Retry + backoff policy. Applied to idempotent methods
/// (GET/HEAD/OPTIONS/DELETE) on transient failures only (transport error /
/// 502 / 503 / 504 / 429); semantic 4xx and non-idempotent methods are
/// never retried.
/// </summary>
public sealed class RetryConfig
{
    public int MaxRetries { get; init; } = 3;
    /// <summary>"exponential" | "linear" | "none".</summary>
    public string Backoff { get; init; } = "exponential";
    public int InitialDelayMs { get; init; } = 100;
    public int MaxDelayMs { get; init; } = 5000;
}

internal sealed class HttpClientConfig
{
    public required string BaseUrl { get; init; }
    public string? ApiKey { get; init; }
    public string? OrgSlug { get; init; }
    public string? ProjectSlug { get; init; }
    public Dictionary<string, string> DefaultHeaders { get; init; } = new();
    public required HttpClient HttpClient { get; init; }
    public RetryConfig Retry { get; init; } = new();
    /// <summary>Client used for the regional-host probe (no-redirect when the SDK owns the transport).</summary>
    public required HttpClient ProbeClient { get; init; }
    internal readonly object RegionLock = new();
    internal Task<string>? RegionTask;
}

internal static class HttpTransport
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly HashSet<string> RetryableMethods =
        new(StringComparer.OrdinalIgnoreCase) { "GET", "HEAD", "OPTIONS", "DELETE" };

    /// <summary>
    /// Issue a request with retry on transient failures. Idempotent methods
    /// are retried with backoff per <c>config.Retry</c>;
    /// semantic / non-idempotent failures throw immediately.
    /// </summary>
    public static async Task<Envelope<T>> RequestAsync<T>(
        HttpClientConfig config,
        HttpMethod method,
        string path,
        object? body = null,
        Dictionary<string, string>? query = null,
        CancellationToken cancellationToken = default,
        Dictionary<string, string>? headers = null,
        bool anonymous = false
    )
    {
        var rc = config.Retry;
        KError? last = null;
        for (var attempt = 0; attempt <= rc.MaxRetries; attempt++)
        {
            try
            {
                return await RequestOnceAsync<T>(config, method, path, body, query, cancellationToken, headers, anonymous).ConfigureAwait(false);
            }
            catch (KError e)
            {
                last = e;
                if (attempt == rc.MaxRetries || !IsRetryable(e, method)) throw;
                var delayMs = ComputeDelayMs(rc, attempt);
                if (delayMs > 0) await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
            }
        }
        throw last!; // unreachable: loop returns or throws
    }

    private static bool IsRetryable(KError e, HttpMethod method)
    {
        if (!RetryableMethods.Contains(method.Method)) return false;
        if (e.Code == "K9O_UPSTREAM_UNAVAILABLE") return true;
        return e.Status is 502 or 503 or 504 or 429;
    }

    private static int ComputeDelayMs(RetryConfig c, int attempt)
    {
        if (c.Backoff == "none") return 0;
        var @base = c.Backoff == "linear"
            ? c.InitialDelayMs * (attempt + 1)
            : c.InitialDelayMs * (1 << attempt);
        var capped = Math.Min(@base, c.MaxDelayMs);
        var jitter = capped * 0.25 * (Random.Shared.NextDouble() * 2 - 1);
        return Math.Max(0, (int)(capped + jitter));
    }

    /// <summary>Single attempt. <see cref="RequestAsync{T}"/> wraps this in retry.</summary>
    private static async Task<Envelope<T>> RequestOnceAsync<T>(
        HttpClientConfig config,
        HttpMethod method,
        string path,
        object? body = null,
        Dictionary<string, string>? query = null,
        CancellationToken cancellationToken = default,
        Dictionary<string, string>? headers = null,
        bool anonymous = false
    )
    {
        var origin = await Region.ResolveAsync(config, cancellationToken).ConfigureAwait(false);
        var url = BuildUrl(origin, path, query);
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        foreach (var (k, v) in config.DefaultHeaders)
        {
            // Anonymous calls carry no credential: drop auth/cookie defaults too.
            if (anonymous && (k.Equals("authorization", StringComparison.OrdinalIgnoreCase)
                || k.Equals("cookie", StringComparison.OrdinalIgnoreCase))) continue;
            request.Headers.TryAddWithoutValidation(k, v);
        }
        if (config.ProjectSlug != null) request.Headers.TryAddWithoutValidation("x-k9o-project-slug", config.ProjectSlug);
        if (config.OrgSlug != null) request.Headers.TryAddWithoutValidation("x-k9o-org-slug", config.OrgSlug);
        if (!anonymous && config.ApiKey != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);
        // Per-call headers (e.g. Vault.Secrets.RevealAsync's X-K9O-Reveal-Challenge) — merged last so
        // they can't be silently dropped by a default header of the same name.
        if (headers != null) foreach (var (k, v) in headers) request.Headers.TryAddWithoutValidation(k, v);
        // HttpContent (e.g. MultipartFormDataContent) is sent as-is so its own Content-Type (with boundary) is kept.
        if (body is HttpContent raw) request.Content = raw;
        else if (body != null) request.Content = JsonContent.Create(body, options: JsonOptions);

        HttpResponseMessage response;
        try
        {
            response = await config.HttpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException e)
        {
            throw new KError(
                "K9O_UPSTREAM_UNAVAILABLE",
                $"Network error reaching {url}: {e.Message}",
                new Dictionary<string, object?> { ["url"] = url },
                "Check connectivity to the gateway; retry with backoff if transient.",
                null, null
            );
        }

        if ((int)response.StatusCode == 204)
        {
            return new Envelope<T> { Data = default, Meta = new Meta() };
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        var statusCode = (int)response.StatusCode;

        if (response.IsSuccessStatusCode)
        {
            try
            {
                var env = JsonSerializer.Deserialize<Envelope<T>>(bytes, JsonOptions);
                return env ?? throw new KError(
                    "K9O_INTERNAL_ERROR",
                    $"Malformed envelope from {url}: deserialized to null",
                    new Dictionary<string, object?> { ["url"] = url, ["status"] = statusCode },
                    null, statusCode, null
                );
            }
            catch (JsonException e)
            {
                throw new KError(
                    "K9O_INTERNAL_ERROR",
                    $"Malformed envelope from {url}: {e.Message}",
                    new Dictionary<string, object?> { ["url"] = url, ["status"] = statusCode },
                    null, statusCode, null
                );
            }
        }

        // Non-2xx — try KError wire shape
        try
        {
            using var doc = JsonDocument.Parse(bytes);
            var root = doc.RootElement;
            if (root.TryGetProperty("code", out var codeEl) && codeEl.ValueKind == JsonValueKind.String
                && root.TryGetProperty("message", out var msgEl) && msgEl.ValueKind == JsonValueKind.String)
            {
                var contextDict = new Dictionary<string, object?>();
                if (root.TryGetProperty("context", out var ctx) && ctx.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in ctx.EnumerateObject())
                    {
                        contextDict[prop.Name] = prop.Value.ValueKind switch
                        {
                            JsonValueKind.String => prop.Value.GetString(),
                            JsonValueKind.Number => prop.Value.TryGetInt64(out var i) ? (object)i : prop.Value.GetDouble(),
                            JsonValueKind.True => true,
                            JsonValueKind.False => false,
                            _ => prop.Value.ToString(),
                        };
                    }
                }
                string? recommendation = root.TryGetProperty("recommendation", out var rec) && rec.ValueKind == JsonValueKind.String ? rec.GetString() : null;
                string? requestId = root.TryGetProperty("requestId", out var rid) && rid.ValueKind == JsonValueKind.String ? rid.GetString() : null;
                throw new KError(codeEl.GetString()!, msgEl.GetString()!, contextDict, recommendation, statusCode, requestId);
            }
        }
        catch (JsonException)
        {
            // fall through
        }

        throw new KError(
            "K9O_INTERNAL_ERROR",
            $"Upstream returned {statusCode} with non-KError body",
            new Dictionary<string, object?> { ["url"] = url, ["status"] = statusCode },
            null, statusCode, null
        );
    }

    /// <summary>
    /// Open an SSE stream and yield decoded events as an IAsyncEnumerable.
    /// Used by Compute LLMs InvokeStreamAsync + Realtime SubscribeAsync.
    /// The [DONE] sentinel terminates the stream; non-2xx throws KError
    /// before any events flow. <c>accept</c> (optional) drops decoded
    /// frames that are not events (for example a resync control frame).
    /// </summary>
    public static async IAsyncEnumerable<T> StreamSseAsync<T>(
        HttpClientConfig config,
        HttpMethod method,
        string path,
        object? body = null,
        Dictionary<string, string>? query = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default,
        Func<T, bool>? accept = null
    )
    {
        var origin = await Region.ResolveAsync(config, cancellationToken).ConfigureAwait(false);
        var url = BuildUrl(origin, path, query);
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        foreach (var (k, v) in config.DefaultHeaders) request.Headers.TryAddWithoutValidation(k, v);
        if (config.ProjectSlug != null) request.Headers.TryAddWithoutValidation("x-k9o-project-slug", config.ProjectSlug);
        if (config.OrgSlug != null) request.Headers.TryAddWithoutValidation("x-k9o-org-slug", config.OrgSlug);
        if (config.ApiKey != null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);
        if (body != null) request.Content = JsonContent.Create(body, options: JsonOptions);

        HttpResponseMessage response;
        try
        {
            response = await config.HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException e)
        {
            throw new KError(
                "K9O_UPSTREAM_UNAVAILABLE",
                $"Network error reaching {url}: {e.Message}",
                new Dictionary<string, object?> { ["url"] = url },
                "Check connectivity to the gateway; retry with backoff if transient.",
                null, null
            );
        }

        if (!response.IsSuccessStatusCode)
        {
            var errBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            var status = (int)response.StatusCode;
            try
            {
                using var doc = JsonDocument.Parse(errBytes);
                var root = doc.RootElement;
                if (root.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.String
                    && root.TryGetProperty("message", out var m) && m.ValueKind == JsonValueKind.String)
                {
                    throw new KError(c.GetString()!, m.GetString()!, new Dictionary<string, object?>(), null, status, null);
                }
            }
            catch (JsonException) { /* fall through */ }
            throw new KError(
                "K9O_INTERNAL_ERROR",
                $"Upstream returned {status} opening stream",
                new Dictionary<string, object?> { ["url"] = url, ["status"] = status },
                null, status, null
            );
        }

        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new System.IO.StreamReader(stream, System.Text.Encoding.UTF8);
        var dataLines = new List<string>();
        while (!reader.EndOfStream)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line == null) break;
            if (line.Length == 0)
            {
                if (dataLines.Count > 0)
                {
                    var payload = string.Join("\n", dataLines);
                    dataLines.Clear();
                    if (payload == "[DONE]") yield break;
                    T? parsed;
                    try { parsed = JsonSerializer.Deserialize<T>(payload, JsonOptions); }
                    catch (JsonException e)
                    {
                        throw new KError("K9O_INTERNAL_ERROR", $"failed to decode stream chunk: {e.Message}");
                    }
                    if (parsed != null && (accept == null || accept(parsed))) yield return parsed;
                }
                continue;
            }
            if (line.StartsWith(":")) continue;
            if (line.StartsWith("data:"))
            {
                var rest = line.Substring(5);
                if (rest.StartsWith(" ")) rest = rest.Substring(1);
                dataLines.Add(rest);
            }
        }
        // Flush trailing event without boundary
        if (dataLines.Count > 0)
        {
            var payload = string.Join("\n", dataLines);
            if (payload != "[DONE]")
            {
                T? parsed = JsonSerializer.Deserialize<T>(payload, JsonOptions);
                if (parsed != null && (accept == null || accept(parsed))) yield return parsed;
            }
        }
    }

    private static string BuildUrl(string baseUrl, string path, Dictionary<string, string>? query)
    {
        var trimmed = baseUrl.TrimEnd('/');
        var url = trimmed + path;
        if (query != null && query.Count > 0)
        {
            var sb = new System.Text.StringBuilder(url);
            var first = true;
            foreach (var (k, v) in query)
            {
                if (v == null) continue;
                sb.Append(first ? '?' : '&');
                sb.Append(HttpUtility.UrlEncode(k));
                sb.Append('=');
                sb.Append(HttpUtility.UrlEncode(v));
                first = false;
            }
            url = sb.ToString();
        }
        return url;
    }
}
