using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using K9o;
using Xunit;

namespace K9o.Tests;

/// <summary>
/// P6 surface-family tests (Studio / Compute.Llms / Realtime / Org.DefaultLlm).
/// Mocks the gateway via a custom <see cref="HttpMessageHandler"/>.
/// </summary>
public class P6Tests
{
    /// <summary>Captures the request + returns a canned response.</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        public string? LastMethod;
        public string? LastPath;
        public string? LastBody;
        public Func<HttpRequestMessage, HttpResponseMessage> Responder = _ => Json(200, "{\"data\":null,\"meta\":{}}");

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastMethod = request.Method.Method;
            LastPath = request.RequestUri!.PathAndQuery;
            LastBody = request.Content != null ? await request.Content.ReadAsStringAsync(cancellationToken) : null;
            return Responder(request);
        }

        public static HttpResponseMessage Json(int status, string body)
        {
            var resp = new HttpResponseMessage((HttpStatusCode)status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            return resp;
        }

        public static HttpResponseMessage Sse(string body)
        {
            var resp = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "text/event-stream"),
            };
            return resp;
        }
    }

    private static (K9oClient client, StubHandler stub) ProjectClient(Func<HttpRequestMessage, HttpResponseMessage>? responder = null)
    {
        var stub = new StubHandler();
        if (responder != null) stub.Responder = responder;
        var http = new HttpClient(stub) { BaseAddress = new Uri("https://acme.api.k9o.io") };
        // BaseAddress is informational; HttpTransport builds full URLs from K9oClient.BaseUrl
        var client = K9oClient.Create(new K9oOptions
        {
            BaseUrl = "https://acme.api.k9o.io",
            ProjectSlug = "portal",
            HttpClient = http,
        });
        return (client, stub);
    }

    [Fact]
    public async Task WorkflowsTriggerAndCancel()
    {
        var (c, stub) = ProjectClient(req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/runs/wfr_1/cancel"))
                return StubHandler.Json(200, "{\"data\":{\"id\":\"wfr_1\",\"workflowId\":\"wfl_x\",\"projectId\":\"p\",\"status\":\"cancelled\"},\"meta\":{}}");
            return StubHandler.Json(200, "{\"data\":{\"id\":\"wfr_1\",\"workflowId\":\"wfl_x\",\"projectId\":\"p\",\"status\":\"pending\"},\"meta\":{}}");
        });
        var run = (await c.Studio.Workflows.TriggerAsync("wfl_x", new Dictionary<string, object?> { ["foo"] = "bar" })).Data!;
        Assert.Equal("pending", run.Status);
        Assert.Equal("POST", stub.LastMethod);
        Assert.Equal("/@portal/workflows/wfl_x/runs", stub.LastPath);
        Assert.Contains("bar", stub.LastBody);

        var cancelled = (await c.Studio.Workflows.Runs.CancelAsync("wfl_x", "wfr_1")).Data!;
        Assert.Equal("cancelled", cancelled.Status);
        Assert.Equal("/@portal/workflows/wfl_x/runs/wfr_1/cancel", stub.LastPath);
    }

    [Fact]
    public async Task DashboardsPin()
    {
        var (c, stub) = ProjectClient(req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/pin"))
                return StubHandler.Json(200, "{\"data\":{\"id\":\"dsh_x\",\"projectId\":\"p\",\"name\":\"ops\",\"status\":\"active\",\"pinnedToWorkspace\":true},\"meta\":{}}");
            return StubHandler.Json(200, "{\"data\":{},\"meta\":{}}");
        });
        var pinned = (await c.Studio.Dashboards.PinAsync("dsh_x")).Data!;
        Assert.True(pinned.PinnedToWorkspace);
        Assert.Equal("/@portal/dashboards/dsh_x/pin", stub.LastPath);
    }

    [Fact]
    public async Task LlmsInvoke()
    {
        var (c, stub) = ProjectClient(_ => StubHandler.Json(200,
            "{\"data\":{\"completion\":\"hi\",\"tokensUsed\":2,\"latencyMs\":9},\"meta\":{}}"));
        var resp = (await c.Compute.Llms.InvokeAsync("mistral-7b",
            new LlmInvokeInput { Prompt = "hello" })).Data!;
        Assert.Equal("hi", resp.Completion);
        Assert.Equal(2, resp.TokensUsed);
        Assert.Equal("/@portal/compute/llms/mistral-7b/invoke", stub.LastPath);
    }

    [Fact]
    public async Task LlmsInvokeStreamYieldsChunks()
    {
        var sse =
            "data: {\"delta\": \"Hel\"}\n\n" +
            "data: {\"delta\": \"lo\", \"done\": true, \"tokensUsed\": 1}\n\n" +
            "data: [DONE]\n\n";
        var (c, stub) = ProjectClient(_ => StubHandler.Sse(sse));
        var acc = new StringBuilder();
        var lastDone = false;
        await foreach (var chunk in c.Compute.Llms.InvokeStreamAsync("gpt-4o", new LlmInvokeInput { Prompt = "hi" }))
        {
            acc.Append(chunk.Delta);
            if (chunk.Done) lastDone = true;
        }
        Assert.Equal("Hello", acc.ToString());
        Assert.True(lastDone);
        Assert.Equal("/@portal/compute/llms/gpt-4o/invoke-stream", stub.LastPath);
    }

    [Fact]
    public async Task OrgDefaultLlmGetAndSet()
    {
        var stub = new StubHandler { Responder = _ => StubHandler.Json(200, "{\"data\":\"mistral-7b\",\"meta\":{}}") };
        var http = new HttpClient(stub);
        var c = K9oClient.Create(new K9oOptions { BaseUrl = "https://acme.api.k9o.io", HttpClient = http });
        var got = (await c.Org.DefaultLlm.GetAsync()).Data;
        Assert.Equal("mistral-7b", got);
        Assert.Equal("/org/default-llm", stub.LastPath);

        await c.Org.DefaultLlm.SetAsync("claude-3-5-sonnet");
        Assert.Equal("PATCH", stub.LastMethod);
        Assert.Contains("claude-3-5-sonnet", stub.LastBody);
    }

    [Fact]
    public void RealtimeTopicBuilders()
    {
        Assert.Equal("collections.orders.*", Realtime.Topic.Collection("orders"));
        Assert.Equal("studio.workflows.runs.*", Realtime.Topic.WorkflowRuns());
        Assert.Equal("studio.dashboards.refresh.dsh_x", Realtime.Topic.DashboardRefresh("dsh_x"));
        Assert.Equal("forge.functions.invocations.fn_x", Realtime.Topic.FunctionInvocations("fn_x"));
        Assert.Equal("audit.org.role_granted", Realtime.Topic.AuditOrg("role_granted"));
    }

    [Fact]
    public async Task RealtimeReplayRejectsNegativeSequence()
    {
        var c = K9oClient.Create(new K9oOptions { BaseUrl = "https://acme.api.k9o.io", ProjectSlug = "portal" });
        var e = await Assert.ThrowsAsync<KError>(() => c.Realtime.ReplayAsync(
            new ReplayInput { Topic = "studio.workflows.runs.*", FromSequence = -1 }));
        Assert.Equal("K9O_INVALID_FILTER", e.Code);
    }

    [Fact]
    public async Task RealtimeReplayPostsBody()
    {
        var (c, stub) = ProjectClient(_ => StubHandler.Json(200, "{\"data\":[],\"meta\":{}}"));
        await c.Realtime.ReplayAsync(new ReplayInput
        {
            Topic = Realtime.Topic.WorkflowRuns(),
            FromSequence = 42,
            Limit = 100,
        });
        Assert.Equal("POST", stub.LastMethod);
        Assert.Equal("/@portal/realtime/replay", stub.LastPath);
        Assert.Contains("42", stub.LastBody);
    }

    [Fact]
    public async Task RealtimeSubscribeSseYieldsEvents()
    {
        var sse =
            "data: {\"topic\":\"collections.orders.*\",\"type\":\"row_inserted\",\"sequence\":1,\"payload\":{},\"occurredAt\":\"t1\"}\n\n" +
            "data: {\"topic\":\"collections.orders.*\",\"type\":\"row_updated\",\"sequence\":2,\"payload\":{},\"occurredAt\":\"t2\"}\n\n";
        var (c, stub) = ProjectClient(_ => StubHandler.Sse(sse));
        var types = new List<string>();
        await foreach (var ev in c.Realtime.SubscribeAsync(Realtime.Topic.Collection("orders")))
        {
            types.Add(ev.Type);
        }
        Assert.Equal(new[] { "row_inserted", "row_updated" }, types);
        Assert.StartsWith("/@portal/realtime/subscribe", stub.LastPath);
    }

    // ---- ai.embed ----

    private const string EmbedOk = "{\"data\":{\"embeddings\":[[0.1,0.2]],\"model\":\"nomic-embed-text-v1.5\",\"dimensions\":2},\"meta\":{}}";

    [Fact]
    public async Task AiEmbedPostsStringInput()
    {
        var (c, stub) = ProjectClient(_ => StubHandler.Json(200, EmbedOk));
        var r = await c.Ai.EmbedAsync("hello");
        Assert.Equal("POST", stub.LastMethod);
        Assert.Equal("/@portal/ai/embeddings", stub.LastPath);
        Assert.Equal("{\"input\":\"hello\"}", stub.LastBody);
        Assert.Equal(2, r.Data!.Dimensions);
        Assert.Equal(0.2, r.Data.Embeddings[0][1]);
    }

    [Fact]
    public async Task AiEmbedSendsListAndModel()
    {
        var (c, stub) = ProjectClient(_ => StubHandler.Json(200, EmbedOk));
        await c.Ai.EmbedAsync(new[] { "search_document: a", "search_document: b" }, "nomic-embed-text-v1.5");
        using var doc = System.Text.Json.JsonDocument.Parse(stub.LastBody!);
        Assert.Equal("nomic-embed-text-v1.5", doc.RootElement.GetProperty("model").GetString());
        var arr = doc.RootElement.GetProperty("input");
        Assert.Equal(System.Text.Json.JsonValueKind.Array, arr.ValueKind);
        Assert.Equal("search_document: b", arr[1].GetString());
    }

    [Fact]
    public async Task AiEmbedOmitsModelWhenNotGiven()
    {
        var (c, stub) = ProjectClient(_ => StubHandler.Json(200, EmbedOk));
        await c.Ai.EmbedAsync(new[] { "x" });
        using var doc = System.Text.Json.JsonDocument.Parse(stub.LastBody!);
        Assert.False(doc.RootElement.TryGetProperty("model", out _));
    }

    [Fact]
    public async Task AiEmbedRequiresProjectSlug()
    {
        var stub = new StubHandler();
        var c = K9oClient.Create(new K9oOptions { BaseUrl = "https://acme.api.k9o.io", HttpClient = new HttpClient(stub) });
        var e = await Assert.ThrowsAsync<KError>(() => c.Ai.EmbedAsync("x"));
        Assert.Equal("K9O_MISSING_PROJECT_CONTEXT", e.Code);
        Assert.Null(stub.LastMethod);
    }

    [Fact]
    public async Task AiEmbedSurfacesKErrorWithoutInput()
    {
        var secret = "the-secret-document-body";
        var (c, _) = ProjectClient(_ => StubHandler.Json(400,
            "{\"code\":\"K9O_MODEL_NOT_DECLARED\",\"message\":\"model is not a declared embedding model\",\"context\":{\"declared\":\"nomic-embed-text-v1.5\"},\"recommendation\":null}"));
        var e = await Assert.ThrowsAsync<KError>(() => c.Ai.EmbedAsync(secret, "other"));
        Assert.Equal("K9O_MODEL_NOT_DECLARED", e.Code);
        Assert.Equal(400, e.Status);
        Assert.DoesNotContain(secret, e.ToString());
        Assert.DoesNotContain(secret, e.Message);
    }
}
