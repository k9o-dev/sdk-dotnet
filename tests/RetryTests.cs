using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace K9o.Core.Tests;

/// <summary>
/// Retry + backoff behaviour. Idempotent methods are retried on
/// transient failures (transport / 502 / 503 / 504 / 429); semantic 4xx and
/// non-idempotent methods are not. Backoff="none" ⇒ no sleeps.
/// </summary>
public class RetryTests
{
    private sealed class CountingHandler : HttpMessageHandler
    {
        public int Calls;
        private readonly Func<int, (HttpStatusCode, string)> _seq;
        public CountingHandler(Func<int, (HttpStatusCode, string)> seq) => _seq = seq;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            // Regional-host probe (HEAD /) is not an API call: answer 200, do not count it.
            if (request.Method == HttpMethod.Head)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            var n = Interlocked.Increment(ref Calls);
            var (status, body) = _seq(n);
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }

    private const string Down = "{\"code\":\"K9O_UPSTREAM_UNAVAILABLE\",\"message\":\"down\"}";
    private static readonly RetryConfig NoSleep =
        new() { MaxRetries = 2, Backoff = "none", InitialDelayMs = 0, MaxDelayMs = 0 };

    private static (K9oClient, CountingHandler) Client(Func<int, (HttpStatusCode, string)> seq)
    {
        var h = new CountingHandler(seq);
        var opts = new K9oOptions
        {
            BaseUrl = "https://acme.api.k9o.io",
            HttpClient = new HttpClient(h),
            RetryConfig = NoSleep,
        };
        return (K9oClient.Create(opts), h);
    }

    [Fact]
    public async Task GetRetriesTransientThenSucceeds()
    {
        var (c, h) = Client(n => n <= 2
            ? (HttpStatusCode.ServiceUnavailable, Down)
            : (HttpStatusCode.OK, "{\"data\":[],\"meta\":{}}"));
        await c.Orgs.ListAsync();
        Assert.Equal(3, h.Calls); // 2 failures + 1 success
    }

    [Fact]
    public async Task GetExhaustsRetries()
    {
        var (c, h) = Client(_ => (HttpStatusCode.ServiceUnavailable, Down));
        await Assert.ThrowsAsync<KError>(() => c.Orgs.ListAsync());
        Assert.Equal(3, h.Calls); // max_retries (2) + 1 initial
    }

    [Fact]
    public async Task PostNotRetried()
    {
        var (c, h) = Client(_ => (HttpStatusCode.ServiceUnavailable, Down));
        await Assert.ThrowsAsync<KError>(() =>
            c.Projects.CreateAsync(new CreateProjectInput { Slug = "valid-slug", DisplayName = "X" }));
        Assert.Equal(1, h.Calls);
    }

    [Fact]
    public async Task GetSemantic4xxNotRetried()
    {
        var (c, h) = Client(_ => (HttpStatusCode.NotFound, "{\"code\":\"K9O_NOT_FOUND\",\"message\":\"nope\"}"));
        await Assert.ThrowsAsync<KError>(() => c.Orgs.ListAsync());
        Assert.Equal(1, h.Calls);
    }
}
