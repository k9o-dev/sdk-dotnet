using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using K9o;
using Xunit;

namespace K9o.Tests;

/// <summary>User data streams: exact method, path and body per call; subscribe resume; the 503 mapping.</summary>
public class StreamsTests
{
    private sealed record Call(string Method, string Target, string Body);

    private sealed class Rec : HttpMessageHandler
    {
        public readonly List<Call> Calls = new();
        public Func<Call, (int Status, string Type, string Body)> Responder = _ => (200, "application/json", "{\"data\":null,\"meta\":{}}");

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content != null ? await request.Content.ReadAsStringAsync(ct) : "";
            var call = new Call(request.Method.Method, request.RequestUri!.PathAndQuery, body);
            lock (Calls) Calls.Add(call);
            var (status, type, text) = Responder(call);
            return new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(text, Encoding.UTF8, type) };
        }
    }

    private static (K9oClient c, Rec rec) Make(Func<Call, (int, string, string)>? responder = null)
    {
        var rec = new Rec();
        if (responder != null) rec.Responder = responder;
        var c = K9oClient.Create(new K9oOptions { BaseUrl = "http://localhost:6000", ProjectSlug = "portal", HttpClient = new HttpClient(rec) });
        return (c, rec);
    }

    private static void ExpectLast(Rec rec, string method, string target, string? json)
    {
        var call = rec.Calls.Last();
        Assert.Equal(method, call.Method);
        Assert.Equal(target, call.Target);
        if (json == null) Assert.Equal("", call.Body);
        else Assert.True(JsonNode.DeepEquals(JsonNode.Parse(json), JsonNode.Parse(call.Body)), "body was " + call.Body);
    }

    private static string Frame(string seq) =>
        $"id: {seq}\ndata: {{\"sequence\":\"{seq}\",\"subject\":\"s\",\"data\":{{\"n\":\"{seq}\"}},\"publishedAt\":\"t\"}}\n\n";

    [Fact]
    public async Task ControlAndDataPlaneRequests()
    {
        var (c, rec) = Make(call =>
        {
            string body;
            if (call.Method == "DELETE") body = "{\"data\":{\"slug\":\"orders\",\"status\":\"deleted\"},\"meta\":{}}";
            else if (call.Target.EndsWith("/publish")) body = "{\"data\":{\"streamSlug\":\"orders\",\"subject\":\"k9o.x\",\"bytes\":8,\"accepted\":true},\"meta\":{}}";
            else if (call.Target.EndsWith("/replay")) body = "{\"data\":{\"streamSlug\":\"orders\",\"events\":[{\"sequence\":\"11\",\"subject\":\"s\",\"data\":{\"id\":1},\"publishedAt\":\"t\"}],\"nextCursor\":\"11\",\"hasMore\":false},\"meta\":{}}";
            else if (call.Method == "GET" && call.Target.EndsWith("/user")) body = "{\"data\":[{\"slug\":\"orders\",\"maxBytes\":1073741824}],\"meta\":{}}";
            else body = "{\"data\":{\"slug\":\"orders\",\"displayName\":\"Orders\",\"maxBytes\":null,\"retentionSeconds\":86400},\"meta\":{}}";
            return (200, "application/json", body);
        });

        var created = (await c.Streams.CreateAsync("Orders", 1073741824)).Data!;
        Assert.Equal("orders", created.Slug);
        Assert.Null(created.MaxBytes);
        ExpectLast(rec, "POST", "/@portal/data-streams/user", "{\"displayName\":\"Orders\",\"maxBytes\":1073741824}");
        await c.Streams.CreateAsync("Orders", 1073741824, 86400);
        ExpectLast(rec, "POST", "/@portal/data-streams/user", "{\"displayName\":\"Orders\",\"maxBytes\":1073741824,\"retentionSeconds\":86400}");
        Assert.Equal(1073741824L, (await c.Streams.ListAsync()).Data![0].MaxBytes);
        ExpectLast(rec, "GET", "/@portal/data-streams/user", null);
        await c.Streams.GetAsync("orders");
        ExpectLast(rec, "GET", "/@portal/data-streams/user/orders", null);
        Assert.Equal("deleted", (await c.Streams.DeleteAsync("orders")).Data!.Status);
        ExpectLast(rec, "DELETE", "/@portal/data-streams/user/orders", null);
        Assert.True((await c.Streams.PublishAsync("orders", new Dictionary<string, object?> { ["id"] = 1 }, "eu.new")).Data!.Accepted);
        ExpectLast(rec, "POST", "/@portal/data-streams/user/orders/publish", "{\"data\":{\"id\":1},\"subject\":\"eu.new\"}");
        await c.Streams.PublishAsync("orders", 1);
        ExpectLast(rec, "POST", "/@portal/data-streams/user/orders/publish", "{\"data\":1}");
        var rep = (await c.Streams.ReplayAsync("orders", 10, 20, 5)).Data!;
        Assert.Equal("11", rep.Events[0].Sequence);
        Assert.Equal("11", rep.NextCursor);
        ExpectLast(rec, "POST", "/@portal/data-streams/user/orders/replay", "{\"fromSequence\":10,\"untilSequence\":20,\"limit\":5}");
        await c.Streams.ReplayAsync("orders");
        ExpectLast(rec, "POST", "/@portal/data-streams/user/orders/replay", "{}");
    }

    [Fact]
    public async Task MissingProjectIsTyped()
    {
        var c = K9oClient.Create(new K9oOptions { BaseUrl = "http://localhost:6000", HttpClient = new HttpClient(new Rec()) });
        var e = await Assert.ThrowsAsync<KError>(() => c.Streams.ListAsync());
        Assert.Equal("K9O_MISSING_PROJECT_CONTEXT", e.Code);
    }

    [Fact]
    public async Task UnavailableMapsToKError()
    {
        var (c, _) = Make(_ => (503, "application/json",
            "{\"code\":\"K9O_STREAM_UNAVAILABLE\",\"message\":\"The Pulse Stream data plane is not yet available in this deployment.\",\"context\":{}}"));
        var e = await Assert.ThrowsAsync<KError>(() => c.Streams.PublishAsync("orders", 1));
        Assert.Equal("K9O_STREAM_UNAVAILABLE", e.Code);
        Assert.Equal(503, e.Status);
        e = await Assert.ThrowsAsync<KError>(async () =>
        {
            await foreach (var _ in c.Streams.SubscribeAsync("orders")) { }
        });
        Assert.Equal("K9O_STREAM_UNAVAILABLE", e.Code);
        Assert.Equal(503, e.Status);
    }

    [Fact]
    public async Task SubscribeResumesFromLastSequenceAcrossServerClosedConnection()
    {
        var (c, rec) = Make(call =>
        {
            var body = "retry: 1000\n:ok\n\n";
            if (!call.Target.Contains('?')) body += Frame("1") + Frame("2");
            else if (call.Target.EndsWith("?fromSequence=2")) body += Frame("3");
            return (200, "text/event-stream", body);
        });
        using var cts = new CancellationTokenSource();
        var seqs = new List<string>();
        await foreach (var ev in c.Streams.SubscribeAsync("orders", null, cts.Token, TimeSpan.FromMilliseconds(1)))
        {
            seqs.Add(ev.Sequence);
            if (seqs.Count == 3) break;
        }
        Assert.Equal(new[] { "1", "2", "3" }, seqs);
        Assert.Equal("GET", rec.Calls[0].Method);
        Assert.Equal("/@portal/data-streams/user/orders/subscribe", rec.Calls[0].Target);
        Assert.Equal("/@portal/data-streams/user/orders/subscribe?fromSequence=2", rec.Calls[1].Target);
    }

    [Fact]
    public async Task SubscribeStartsFromGivenSequenceAndStopsOnCancel()
    {
        var (c, rec) = Make(_ => (200, "text/event-stream", Frame("43")));
        using var cts = new CancellationTokenSource();
        var seqs = new List<string>();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var ev in c.Streams.SubscribeAsync("orders", "42", cts.Token, TimeSpan.FromMilliseconds(1)))
            {
                seqs.Add(ev.Sequence);
                cts.Cancel();
            }
        });
        Assert.Equal(new[] { "43" }, seqs);
        Assert.Equal("/@portal/data-streams/user/orders/subscribe?fromSequence=42", rec.Calls[0].Target);
    }
}
