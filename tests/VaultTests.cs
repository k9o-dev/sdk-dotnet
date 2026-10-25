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

/// <summary>
/// Wire-shape tests for <c>client.Vault.Secrets.*</c> — the full family: List / Get /
/// Create / RevealChallenge / Reveal / Update / Rotate / Move / Delete.
///
/// Verifies URL/query/method/body against the LIVE-VERIFIED contract (not just the documented
/// OpenAPI schema, which is inaccurate for Update/Rotate/Move's response shape), mirroring
/// `packages/kibo/test/secrets.test.ts`:
///  - `region` required on every named-secret op except List
///  - `value` base64-encoded on write, base64-decoded on reveal
///  - the `X-K9O-Reveal-Challenge` header on Reveal
/// Hermetic — no real network calls; the gateway is mocked via a custom HttpMessageHandler.
/// </summary>
public class VaultTests
{
    /// <summary>Captures the request (method/path+query/body/headers) and returns a canned response.</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        public string? LastMethod;
        public string? LastPathAndQuery;
        public string? LastBody;
        public Dictionary<string, string> LastHeaders = new();
        public Func<HttpRequestMessage, HttpResponseMessage> Responder = _ => Json(200, "{\"data\":null,\"meta\":{}}");

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastMethod = request.Method.Method;
            LastPathAndQuery = request.RequestUri!.PathAndQuery;
            LastBody = request.Content != null ? await request.Content.ReadAsStringAsync(cancellationToken) : null;
            LastHeaders = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
            return Responder(request);
        }

        public static HttpResponseMessage Json(int status, string body)
        {
            if (status == 204) return new HttpResponseMessage((HttpStatusCode)status);
            return new HttpResponseMessage((HttpStatusCode)status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
        }
    }

    private static (K9oClient client, StubHandler stub) ProjectClient(Func<HttpRequestMessage, HttpResponseMessage>? responder = null)
    {
        var stub = new StubHandler();
        if (responder != null) stub.Responder = responder;
        var client = K9oClient.Create(new K9oOptions
        {
            BaseUrl = "https://acme.api.k9o.io",
            ProjectSlug = "member-portal",
            HttpClient = new HttpClient(stub),
        });
        return (client, stub);
    }

    private const string SummaryJson =
        "{\"id\":\"sec_abc\",\"name\":\"db/pw\",\"ownerId\":\"usr_1\",\"visibility\":{\"kind\":\"private\"}," +
        "\"version\":1,\"kid\":\"vkey_1\",\"createdAt\":\"t\",\"createdBy\":\"usr_1\",\"updatedAt\":\"t\"," +
        "\"updatedBy\":\"usr_1\",\"rotatedAt\":null,\"retiredAt\":null,\"metadata\":{}}";

    [Fact]
    public async Task ListGetsVaultSecretsWithPrefixCursorLimitAndNoRegion()
    {
        var (c, stub) = ProjectClient(_ => StubHandler.Json(200,
            $"{{\"data\":{{\"secrets\":[{SummaryJson}],\"nextCursor\":null}},\"meta\":{{}}}}"));
        var env = await c.Vault.Secrets.ListAsync(prefix: "db/", limit: 10);
        Assert.Equal("GET", stub.LastMethod);
        Assert.Equal("/@member-portal/vault/secrets?prefix=db%2f&limit=10", stub.LastPathAndQuery);
        Assert.DoesNotContain("region", stub.LastPathAndQuery);
        Assert.Equal("sec_abc", env.Data!.Secrets[0].Id);
    }

    [Fact]
    public async Task ListWithNoFiltersOmitsQueryString()
    {
        var (c, stub) = ProjectClient(_ => StubHandler.Json(200, "{\"data\":{\"secrets\":[],\"nextCursor\":null},\"meta\":{}}"));
        await c.Vault.Secrets.ListAsync();
        Assert.Equal("/@member-portal/vault/secrets", stub.LastPathAndQuery);
    }

    [Fact]
    public async Task GetGetsVaultSecretsNameWithRegion()
    {
        var (c, stub) = ProjectClient(_ => StubHandler.Json(200, $"{{\"data\":{SummaryJson},\"meta\":{{}}}}"));
        await c.Vault.Secrets.GetAsync("db/pw", "us01");
        Assert.Equal("GET", stub.LastMethod);
        Assert.Equal("/@member-portal/vault/secrets/db%2fpw?region=us01", stub.LastPathAndQuery);
    }

    [Fact]
    public async Task CreatePostsWithRegionQueryAndBase64EncodedValue()
    {
        var (c, stub) = ProjectClient(_ => StubHandler.Json(200, $"{{\"data\":{SummaryJson},\"meta\":{{}}}}"));
        await c.Vault.Secrets.CreateAsync("db/pw", "us01", "hunter2", new SecretVisibility { Kind = "private" });
        Assert.Equal("POST", stub.LastMethod);
        Assert.Equal("/@member-portal/vault/secrets/db%2fpw?region=us01", stub.LastPathAndQuery);
        var expectedB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("hunter2"));
        Assert.Contains($"\"value\":\"{expectedB64}\"", stub.LastBody);
        Assert.DoesNotContain("hunter2", stub.LastBody);
        Assert.Contains("\"kind\":\"private\"", stub.LastBody);
    }

    [Fact]
    public async Task RevealChallengePostsOpRevealChallengeAndRegion()
    {
        var (c, stub) = ProjectClient(_ => StubHandler.Json(200, "{\"data\":{\"challenge\":\"rvc_1\",\"expiresAt\":\"t\"},\"meta\":{}}"));
        var env = await c.Vault.Secrets.RevealChallengeAsync("db/pw", "us01");
        Assert.Equal("POST", stub.LastMethod);
        Assert.Contains("op=reveal-challenge", stub.LastPathAndQuery);
        Assert.Contains("region=us01", stub.LastPathAndQuery);
        Assert.Null(stub.LastBody);
        Assert.Equal("rvc_1", env.Data!.Challenge);
    }

    [Fact]
    public async Task RevealGetsWithChallengeHeaderAndDecodesBase64ValueToPlaintext()
    {
        var wireValue = Convert.ToBase64String(Encoding.UTF8.GetBytes("hunter2"));
        var (c, stub) = ProjectClient(_ => StubHandler.Json(200,
            $"{{\"data\":{{\"id\":\"sec_abc\",\"name\":\"db/pw\",\"version\":1,\"value\":\"{wireValue}\"," +
            "\"revealedAt\":\"t\",\"attestationTier\":{\"urn\":\"u\",\"operator\":\"platform\",\"verified\":true}},\"meta\":{}}"));
        var env = await c.Vault.Secrets.RevealAsync("db/pw", "us01", "rvc_1");
        Assert.Equal("GET", stub.LastMethod);
        Assert.Equal("rvc_1", stub.LastHeaders["X-K9O-Reveal-Challenge"]);
        Assert.Equal("hunter2", env.Data!.Value);
        Assert.Equal("platform", env.Data.AttestationTier.Operator);
        Assert.True(env.Data.AttestationTier.Verified);
    }

    [Fact]
    public async Task UpdatePutsWithRegionAndBase64EncodedValueReturnsNarrowShape()
    {
        var (c, stub) = ProjectClient(_ => StubHandler.Json(200, "{\"data\":{\"id\":\"sec_abc\",\"name\":\"db/pw\",\"version\":2},\"meta\":{}}"));
        var env = await c.Vault.Secrets.UpdateAsync("db/pw", "us01", "newpw");
        Assert.Equal("PUT", stub.LastMethod);
        Assert.Equal("/@member-portal/vault/secrets/db%2fpw?region=us01", stub.LastPathAndQuery);
        var expectedB64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("newpw"));
        Assert.Contains($"\"value\":\"{expectedB64}\"", stub.LastBody);
        Assert.Equal(2, env.Data!.Version);
    }

    [Fact]
    public async Task RotatePostsOpRotateAndRegionNoBodyReturnsUndocumentedShape()
    {
        var (c, stub) = ProjectClient(_ => StubHandler.Json(200,
            "{\"data\":{\"id\":\"sec_abc\",\"name\":\"db/pw\",\"version\":2,\"rotatedAt\":\"t\",\"ledgerSequenceNumber\":2},\"meta\":{}}"));
        var env = await c.Vault.Secrets.RotateAsync("db/pw", "us01");
        Assert.Equal("POST", stub.LastMethod);
        Assert.Contains("op=rotate", stub.LastPathAndQuery);
        Assert.Contains("region=us01", stub.LastPathAndQuery);
        Assert.Null(stub.LastBody);
        Assert.Equal(2L, env.Data!.LedgerSequenceNumber);
    }

    [Fact]
    public async Task MovePostsOpMoveAndRegionWithNewPathBody()
    {
        var (c, stub) = ProjectClient(_ => StubHandler.Json(200,
            "{\"data\":{\"id\":\"sec_abc\",\"name\":\"db/pw2\",\"event\":\"SECRET_PATH_CHANGED\"},\"meta\":{}}"));
        var env = await c.Vault.Secrets.MoveAsync("db/pw", "us01", "db/pw2");
        Assert.Equal("POST", stub.LastMethod);
        Assert.Contains("op=move", stub.LastPathAndQuery);
        Assert.Equal("{\"newPath\":\"db/pw2\"}", stub.LastBody);
        Assert.Equal("SECRET_PATH_CHANGED", env.Data!.Event);
    }

    [Fact]
    public async Task DeleteDeletesWithRegionAndHandles204EmptyBody()
    {
        var (c, stub) = ProjectClient(_ => StubHandler.Json(204, ""));
        var env = await c.Vault.Secrets.DeleteAsync("db/pw", "us01");
        Assert.Equal("DELETE", stub.LastMethod);
        Assert.Equal("/@member-portal/vault/secrets/db%2fpw?region=us01", stub.LastPathAndQuery);
        Assert.Null(env.Data);
    }

    [Fact]
    public async Task SecretsOperationsRequireProjectSlug()
    {
        var c = K9oClient.Create(new K9oOptions { BaseUrl = "https://api.k9o.io" });
        var e = await Assert.ThrowsAsync<KError>(() => c.Vault.Secrets.GetAsync("db/pw", "us01"));
        Assert.Equal("K9O_MISSING_PROJECT_CONTEXT", e.Code);
    }
}
