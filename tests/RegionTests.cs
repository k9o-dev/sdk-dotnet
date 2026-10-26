using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using K9o;
using Xunit;

namespace K9o.Tests;

/// <summary>Regional host resolution (mirrors the TypeScript region tests).</summary>
public class RegionTests
{
    private sealed record Rec(string Method, string Url, Dictionary<string, string> Headers);

    private sealed class Stub : HttpMessageHandler
    {
        public readonly List<Rec> Calls = new();
        public Func<HttpRequestMessage, HttpResponseMessage> OnHead = _ => new HttpResponseMessage(HttpStatusCode.OK);
        public Func<HttpRequestMessage, HttpResponseMessage> OnOther = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"data\":{},\"meta\":{}}", Encoding.UTF8, "application/json"),
        };

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var h = request.Headers.ToDictionary(kv => kv.Key.ToLowerInvariant(), kv => string.Join(",", kv.Value));
            lock (Calls) Calls.Add(new Rec(request.Method.Method, request.RequestUri!.ToString(), h));
            return Task.FromResult(request.Method == HttpMethod.Head ? OnHead(request) : OnOther(request));
        }
    }

    private static HttpResponseMessage Redirect(string location)
    {
        var r = new HttpResponseMessage(HttpStatusCode.PermanentRedirect);
        r.Headers.TryAddWithoutValidation("Location", location);
        return r;
    }

    private static (K9oClient, Stub) Make(string baseUrl, Func<HttpRequestMessage, HttpResponseMessage>? head = null)
    {
        var stub = new Stub();
        if (head != null) stub.OnHead = head;
        var c = K9oClient.Create(new K9oOptions
        {
            BaseUrl = baseUrl,
            ApiKey = "sk_secret",
            ProjectSlug = "portal",
            DefaultHeaders = new Dictionary<string, string> { ["x-custom-default"] = "d", ["cookie"] = "s=1" },
            HttpClient = new HttpClient(stub),
            RetryConfig = new RetryConfig { MaxRetries = 0, Backoff = "none" },
        });
        return (c, stub);
    }

    [Fact]
    public async Task ProbeGoesFirstIsHeadUnauthenticatedThenCredentialedCallGoesRegional()
    {
        var (c, stub) = Make("https://acme.api.k9o.io", _ => Redirect("https://acme.api-eu01.k9o.io/"));
        await c.Gate.WhoamiAsync();
        Assert.Equal(2, stub.Calls.Count);
        var probe = stub.Calls[0];
        Assert.Equal("HEAD", probe.Method);
        Assert.Equal("https://acme.api.k9o.io/", probe.Url);
        Assert.False(probe.Headers.ContainsKey("authorization"));
        Assert.False(probe.Headers.ContainsKey("cookie"));
        Assert.False(probe.Headers.ContainsKey("x-custom-default"));
        Assert.False(probe.Headers.ContainsKey("x-k9o-project-slug"));
        var call = stub.Calls[1];
        Assert.StartsWith("https://acme.api-eu01.k9o.io/", call.Url);
        Assert.True(call.Headers.ContainsKey("authorization"));
    }

    [Fact]
    public async Task WorksOnAnInternalApexAndRefusesAnotherApex()
    {
        var (c, stub) = Make("https://acme.api.corp.internal", _ => Redirect("https://acme.api-de01.corp.internal/"));
        await c.Gate.WhoamiAsync();
        Assert.Equal("https://acme.api.corp.internal/", stub.Calls[0].Url);
        Assert.Equal("https://acme.api-de01.corp.internal/@portal/gate/whoami", stub.Calls[1].Url);

        // Same org, different apex: refused; the credential stays on the configured host.
        var (c2, stub2) = Make("https://acme.api.corp.internal", _ => Redirect("https://acme.api-de01.k9o.io/"));
        await c2.Gate.WhoamiAsync();
        Assert.Equal("https://acme.api.corp.internal/@portal/gate/whoami", stub2.Calls[1].Url);

        // localhost is never probed.
        var (c3, stub3) = Make("http://localhost:6000", _ => Redirect("https://evil.example/"));
        await c3.Gate.WhoamiAsync();
        Assert.DoesNotContain(stub3.Calls, x => x.Method == "HEAD");
    }

    [Fact]
    public void OrgSlugComposesOntoAnyPlatformHost()
    {
        Assert.Equal("https://acme.api.corp.internal", UrlGrammar.ResolveBaseUrl("https://api.corp.internal", "acme"));
        Assert.Equal("https://acme.api-staging.example.org:8443", UrlGrammar.ResolveBaseUrl("https://api-staging.example.org:8443", "acme"));
        Assert.Equal("http://localhost:6000", UrlGrammar.ResolveBaseUrl("http://localhost:6000", "acme"));
        Assert.Equal("https://acme.api-de01.k9o.io", UrlGrammar.ResolveBaseUrl("https://acme.api-de01.k9o.io", "other"));
        var h = UrlGrammar.ParseApiHost("acme.api-de01.corp.internal")!;
        Assert.Equal("acme", h.OrgSlug);
        Assert.Equal("de01", h.Region);
        Assert.Equal("corp.internal", h.Apex);
        Assert.Null(UrlGrammar.ParseApiHost("localhost"));
    }

    [Fact]
    public async Task StagingApexFamilyIsAccepted()
    {
        var (c, stub) = Make("https://acme.api-staging.k9o.io", _ => Redirect("https://acme.api-staging-de01.k9o.io/"));
        await c.Gate.WhoamiAsync();
        Assert.StartsWith("https://acme.api-staging-de01.k9o.io/", stub.Calls[1].Url);
    }

    [Fact]
    public async Task RelativeLocationResolvesAgainstBaseAndIsRefused()
    {
        // A relative Location can only ever resolve to the configured host, which is not a regional host.
        var (c, stub) = Make("https://acme.api.k9o.io", _ => Redirect("/elsewhere"));
        await c.Gate.WhoamiAsync();
        Assert.StartsWith("https://acme.api.k9o.io/", stub.Calls[1].Url);
    }

    [Fact]
    public async Task ResultIsCachedOneProbeForTwoCalls()
    {
        var (c, stub) = Make("https://acme.api.k9o.io", _ => Redirect("https://acme.api-eu01.k9o.io/"));
        await c.Gate.WhoamiAsync();
        await c.Gate.WhoamiAsync();
        Assert.Equal(1, stub.Calls.Count(x => x.Method == "HEAD"));
        Assert.Equal(2, stub.Calls.Count(x => x.Method == "GET"));
    }

    [Fact]
    public async Task NoRedirectKeepsConfiguredHostAndIsCached()
    {
        var (c, stub) = Make("https://acme.api.k9o.io");
        await c.Gate.WhoamiAsync();
        await c.Gate.WhoamiAsync();
        Assert.Equal(1, stub.Calls.Count(x => x.Method == "HEAD"));
        Assert.All(stub.Calls.Where(x => x.Method == "GET"), x => Assert.StartsWith("https://acme.api.k9o.io/", x.Url));
    }

    [Theory]
    [InlineData("https://api-eu01.k9o.io")]
    [InlineData("https://acme.api-eu01.k9o.io")]
    [InlineData("https://acme.example.com")]
    [InlineData("https://platform.acme.internal")]
    [InlineData("http://localhost:8080")]
    [InlineData("https://api.k9o.io")]
    [InlineData("https://acme.api-staging-eu01.k9o.io")]
    public async Task NonRegionFreeBaseUrlIsNeverProbed(string baseUrl)
    {
        var (c, stub) = Make(baseUrl);
        await c.Gate.WhoamiAsync();
        Assert.DoesNotContain(stub.Calls, x => x.Method == "HEAD");
        Assert.StartsWith(baseUrl, stub.Calls[0].Url);
    }

    [Theory]
    [InlineData("https://evil.example.com/")]
    [InlineData("https://acme.api-eu01.k9o.io.evil.com/")]
    [InlineData("https://evilacme.api-eu01.k9o.io/")]
    [InlineData("https://other.api-eu01.k9o.io/")]
    [InlineData("https://acme.api-staging-eu01.k9o.io/")]
    [InlineData("http://acme.api-eu01.k9o.io/")]
    [InlineData("https://user:pw@acme.api-eu01.k9o.io/")]
    [InlineData("https://acme.api-eu01.k9o.io:8443/")]
    [InlineData("https://acme.api-europe.k9o.io/")]
    [InlineData("https://acme.api-eu1.k9o.io/")]
    [InlineData("https://acme.api-EU01.k9o.io.evil.io/")]
    public async Task HostileLocationIsRefusedAndCredentialStaysOnConfiguredHost(string location)
    {
        var (c, stub) = Make("https://acme.api.k9o.io", _ => Redirect(location));
        await c.Gate.WhoamiAsync();
        var call = stub.Calls[1];
        Assert.StartsWith("https://acme.api.k9o.io/", call.Url);
    }

    [Fact]
    public async Task StagingBaseRefusesProductionRegionalLocation()
    {
        var (c, stub) = Make("https://acme.api-staging.k9o.io", _ => Redirect("https://acme.api-eu01.k9o.io/"));
        await c.Gate.WhoamiAsync();
        Assert.StartsWith("https://acme.api-staging.k9o.io/", stub.Calls[1].Url);
    }

    [Fact]
    public async Task ProbeNetworkFailureIsNotCached()
    {
        var n = 0;
        var (c, stub) = Make("https://acme.api.k9o.io", _ =>
        {
            if (Interlocked.Increment(ref n) == 1) throw new HttpRequestException("boom");
            return Redirect("https://acme.api-eu01.k9o.io/");
        });
        await c.Gate.WhoamiAsync();
        Assert.StartsWith("https://acme.api.k9o.io/", stub.Calls.Last(x => x.Method == "GET").Url);
        await c.Gate.WhoamiAsync();
        Assert.Equal(2, stub.Calls.Count(x => x.Method == "HEAD"));
        Assert.StartsWith("https://acme.api-eu01.k9o.io/", stub.Calls.Last(x => x.Method == "GET").Url);
    }

    [Fact]
    public async Task Non308StatusKeepsConfiguredHost()
    {
        var (c, stub) = Make("https://acme.api.k9o.io", _ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.TemporaryRedirect);
            r.Headers.TryAddWithoutValidation("Location", "https://acme.api-eu01.k9o.io/");
            return r;
        });
        await c.Gate.WhoamiAsync();
        Assert.StartsWith("https://acme.api.k9o.io/", stub.Calls[1].Url);
    }

    [Fact]
    public async Task StreamingPathUsesRegionalOrigin()
    {
        var (c, stub) = Make("https://acme.api.k9o.io", _ => Redirect("https://acme.api-eu01.k9o.io/"));
        stub.OnOther = _ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("data: {\"delta\":\"x\",\"done\":true}\n\ndata: [DONE]\n\n", Encoding.UTF8, "text/event-stream"),
        };
        await foreach (var _ in c.Compute.Llms.InvokeStreamAsync("m", new LlmInvokeInput { Prompt = "hi" })) { }
        Assert.Equal("HEAD", stub.Calls[0].Method);
        Assert.False(stub.Calls[0].Headers.ContainsKey("authorization"));
        Assert.StartsWith("https://acme.api-eu01.k9o.io/", stub.Calls[1].Url);
        Assert.True(stub.Calls[1].Headers.ContainsKey("authorization"));
    }

    [Fact]
    public async Task ClientDefaultAuthorizationHeaderSkipsProbe()
    {
        var stub = new Stub { OnHead = _ => Redirect("https://acme.api-eu01.k9o.io/") };
        var http = new HttpClient(stub);
        http.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "Bearer leak");
        var c = K9oClient.Create(new K9oOptions { BaseUrl = "https://acme.api.k9o.io", ProjectSlug = "portal", HttpClient = http });
        await c.Gate.WhoamiAsync();
        Assert.DoesNotContain(stub.Calls, x => x.Method == "HEAD");
    }

    [Fact]
    public void SdkOwnedProbeTransportDoesNotFollowRedirects()
    {
        using var h = Region.CreateProbeHandler();
        Assert.False(h.AllowAutoRedirect);
        Assert.False(h.UseCookies);
    }
}
