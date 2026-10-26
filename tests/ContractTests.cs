using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using K9o;
using Xunit;

namespace K9o.Tests;

/// <summary>
/// Exact wire contract: for each call the SDK sends, the HTTP method, path (with query) and JSON body the gateway's
/// route reads (gateway OpenAPI and the route handlers under services/gateway/src/routes).
/// </summary>
public class ContractTests
{
    private sealed class Rec : HttpMessageHandler
    {
        public string? Method;
        public string? Target;
        public string? ContentType;
        public byte[] Body = Array.Empty<byte>();
        public string Response = "{\"data\":null,\"meta\":{}}";
        public string ResponseType = "application/json";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Method = request.Method.Method;
            Target = request.RequestUri!.PathAndQuery;
            ContentType = request.Content?.Headers.ContentType?.MediaType;
            Body = request.Content != null ? await request.Content.ReadAsByteArrayAsync(ct) : Array.Empty<byte>();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(Response, Encoding.UTF8, ResponseType),
            };
        }
    }

    private static (K9oClient project, K9oClient org, Rec rec) Make()
    {
        var rec = new Rec();
        var http = new HttpClient(rec);
        var project = K9oClient.Create(new K9oOptions { BaseUrl = "http://localhost:6000", ProjectSlug = "portal", HttpClient = http });
        var org = K9oClient.Create(new K9oOptions { BaseUrl = "http://localhost:6000", HttpClient = http });
        return (project, org, rec);
    }

    private static (string, SortedDictionary<string, string>) Split(string target)
    {
        var i = target.IndexOf('?');
        var q = new SortedDictionary<string, string>();
        if (i >= 0)
            foreach (var kv in target[(i + 1)..].Split('&'))
            {
                var p = kv.Split('=', 2);
                q[p[0]] = p.Length > 1 ? p[1] : "";
            }
        return (i >= 0 ? target[..i] : target, q);
    }

    private static void Expect(Rec rec, string method, string path, string? json)
    {
        Assert.Equal(method, rec.Method);
        var (ep, eq) = Split(path);
        var (ap, aq) = Split(rec.Target!);
        Assert.Equal(ep, ap);
        Assert.Equal(eq, aq);
        if (json == null)
            Assert.True(rec.Body.Length == 0, "expected no body, got " + Encoding.UTF8.GetString(rec.Body));
        else
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(json), JsonNode.Parse(rec.Body)),
                "body was " + Encoding.UTF8.GetString(rec.Body));
    }

    // ---- projects ----

    [Fact]
    public async Task ProjectsCreateSendsDisplayNameAndOnlySetFields()
    {
        var (_, org, rec) = Make();
        await org.Projects.CreateAsync(new CreateProjectInput { DisplayName = "Demo", Slug = "demo", Region = "de01", CustodyMode = "PLATFORM_MANAGED" });
        Expect(rec, "POST", "/projects", "{\"displayName\":\"Demo\",\"slug\":\"demo\",\"region\":\"de01\",\"custodyMode\":\"PLATFORM_MANAGED\"}");
        await org.Projects.CreateAsync(new CreateProjectInput { DisplayName = "Only a name" });
        Expect(rec, "POST", "/projects", "{\"displayName\":\"Only a name\"}");
    }

    [Fact]
    public async Task ProjectsGetUpdateListUseTheProjectsRoute()
    {
        var (_, org, rec) = Make();
        await org.Projects.GetAsync("demo");
        Expect(rec, "GET", "/projects/demo", null);
        await org.Projects.UpdateAsync("demo", new UpdateProjectInput { DisplayName = "Renamed" });
        Expect(rec, "PATCH", "/projects/demo", "{\"displayName\":\"Renamed\"}");
        await org.Projects.ListAsync();
        Expect(rec, "GET", "/projects", null);
    }

    [Fact]
    public async Task ProjectRecordReadsDisplayName()
    {
        var (_, org, rec) = Make();
        rec.Response = "{\"data\":{\"id\":\"proj_1\",\"slug\":\"demo\",\"displayName\":\"Demo\",\"custodyMode\":\"PLATFORM_MANAGED\",\"orgId\":\"org_1\",\"extra\":1},\"meta\":{}}";
        var p = (await org.Projects.GetAsync("demo")).Data!;
        Assert.Equal("Demo", p.DisplayName);
    }

    // ---- functions ----

    [Fact]
    public async Task FunctionsDeploySendsArtifactKeyVersionAndTier()
    {
        var (c, _, rec) = Make();
        await c.Functions.DeployAsync(new DeployFunctionInput { Name = "hello", Runtime = "bun", Tier = "sandbox", ArtifactKey = "functions/p/abc.tar", Version = "1" });
        Expect(rec, "POST", "/@portal/functions",
            "{\"name\":\"hello\",\"runtime\":\"bun\",\"tier\":\"sandbox\",\"artifactKey\":\"functions/p/abc.tar\",\"version\":\"1\"}");
    }

    [Fact]
    public async Task FunctionsUploadArtifactSendsRawBytes()
    {
        var (c, _, rec) = Make();
        var wasm = new byte[] { 0, 97, 115, 109 };
        await c.Functions.UploadArtifactAsync(wasm, "wasm");
        Assert.Equal("POST", rec.Method);
        Assert.Equal("/@portal/functions/artifacts", rec.Target);
        Assert.Equal("application/wasm", rec.ContentType);
        Assert.Equal(wasm, rec.Body);
        await c.Functions.UploadArtifactAsync(wasm, "tar");
        Assert.Equal("application/x-tar", rec.ContentType);
        await Assert.ThrowsAsync<KError>(() => c.Functions.UploadArtifactAsync(wasm, "zip"));
    }

    [Fact]
    public async Task FunctionsListAndInvoke()
    {
        var (c, _, rec) = Make();
        await c.Functions.ListAsync("active", 10, "c1");
        Expect(rec, "GET", "/@portal/functions?status=active&limit=10&cursor=c1", null);
        await c.Functions.InvokeAsync("hello", new Dictionary<string, object?> { ["a"] = 1 });
        Expect(rec, "POST", "/@portal/functions/hello/invoke", "{\"payload\":{\"a\":1}}");
        await c.Functions.InvokeAsync("hello", null, new InvokeOptions { Background = true });
        Expect(rec, "POST", "/@portal/functions/hello/invoke", "{\"background\":true}");
        await c.Functions.GetAsync("hello");
        Expect(rec, "GET", "/@portal/functions/hello", null);
        await c.Functions.DeleteAsync("hello");
        Expect(rec, "DELETE", "/@portal/functions/hello", null);
    }

    // ---- policies ----

    [Fact]
    public async Task PoliciesSendThePolicyDocument()
    {
        var (c, _, rec) = Make();
        PolicyInput Doc(string? mode = null) => new() { Policy = new Dictionary<string, object?> { ["collections"] = new Dictionary<string, object?>() }, EditorMode = mode };
        await c.Policies.CreateAsync(Doc("json"));
        Expect(rec, "POST", "/@portal/policies", "{\"policy\":{\"collections\":{}},\"editorMode\":\"json\"}");
        await c.Policies.ValidateAsync(Doc());
        Expect(rec, "POST", "/@portal/policies/validate", "{\"policy\":{\"collections\":{}}}");
        await c.Policies.UpdateAsync("pol_1", Doc());
        Expect(rec, "PATCH", "/@portal/policies/pol_1", "{\"policy\":{\"collections\":{}}}");
        await c.Policies.PublishAsync("pol_1");
        Expect(rec, "POST", "/@portal/policies/pol_1/publish", null);
        await c.Policies.ListAsync("published");
        Expect(rec, "GET", "/@portal/policies?status=published", null);
        await c.Policies.CurrentAsync();
        Expect(rec, "GET", "/@portal/policy", null);
        await c.Policies.DeleteAsync("pol_1");
        Expect(rec, "DELETE", "/@portal/policies/pol_1", null);
    }

    // ---- audit ----

    [Fact]
    public async Task AuditQueryUsesTheGatewayFilterNames()
    {
        var (c, _, rec) = Make();
        await c.Audit.QueryAsync(new AuditQueryOpts { EventType = "auth.sign_in", ActorId = "u_1", After = "2026-01-01", Before = "2026-02-01", Limit = 10, Cursor = "cur" });
        Expect(rec, "GET", "/@portal/audit-log?eventType=auth.sign_in&actorId=u_1&after=2026-01-01&before=2026-02-01&limit=10&cursor=cur", null);
    }

    // ---- org plane ----

    [Fact]
    public async Task OrgMembersGroupsAudit()
    {
        var (_, org, rec) = Make();
        await org.Org.Members.ListAsync(status: "active", userId: "u_1", role: "ORG_MEMBER", acceptanceStatus: "accepted");
        Expect(rec, "GET", "/members?userId=u_1&role=ORG_MEMBER&acceptanceStatus=accepted&status=active", null);
        await org.Org.Members.InviteAsync("a@b.co", "ORG_MEMBER");
        Expect(rec, "POST", "/members", "{\"email\":\"a@b.co\",\"role\":\"ORG_MEMBER\"}");
        await org.Org.Members.RemoveAsync("u_1");
        Expect(rec, "DELETE", "/members/u_1", null);
        await org.Org.Groups.ListAsync("u_1");
        Expect(rec, "GET", "/groups?userId=u_1", null);
        await org.Org.Groups.GetAsync("g_1");
        Expect(rec, "GET", "/groups/g_1", null);
        await org.Org.AuditLog.QueryAsync("data.write.org", 10);
        Expect(rec, "GET", "/audit-log?type=data.write.org&limit=10", null);
        await org.Org.GetAsync();
        Expect(rec, "GET", "/org", null);
    }

    [Fact]
    public async Task OrgRoleGrants()
    {
        var (_, org, rec) = Make();
        await org.Org.Gate.Roles.ListAsync();
        Expect(rec, "GET", "/role-grants", null);
        await org.Org.Gate.Roles.GrantAsync("u_1", "ORG_ADMIN");
        Expect(rec, "POST", "/role-grants", "{\"userId\":\"u_1\",\"role\":\"ORG_ADMIN\"}");
        await org.Org.Gate.Roles.GrantAsync("u_1", "ORG_ADMIN", "audit");
        Expect(rec, "POST", "/role-grants", "{\"userId\":\"u_1\",\"role\":\"ORG_ADMIN\",\"decisionReason\":\"audit\"}");
        await org.Org.Gate.Roles.GetAsync("g_1");
        Expect(rec, "GET", "/role-grants/g_1", null);
        await org.Org.Gate.Roles.RevokeAsync("g_1");
        Expect(rec, "DELETE", "/role-grants/g_1", null);
    }

    [Fact]
    public async Task OrgIdentityProviders()
    {
        var (_, org, rec) = Make();
        await org.Org.Gate.IdpProviders.ListAsync();
        Expect(rec, "GET", "/identity-providers", null);
        await org.Org.Gate.IdpProviders.CreateAsync(new CreateIdpInput { IdpProvider = "saml", DisplayName = "Okta", ClientId = "cid", ClientSecretRef = "ref_1", TierGating = "free_tier_ok" });
        Expect(rec, "POST", "/identity-providers",
            "{\"idpProvider\":\"saml\",\"displayName\":\"Okta\",\"clientId\":\"cid\",\"clientSecretRef\":\"ref_1\",\"tierGating\":\"free_tier_ok\"}");
        await org.Org.Gate.IdpProviders.GetAsync("idp_1");
        Expect(rec, "GET", "/identity-providers/idp_1", null);
        await org.Org.Gate.IdpProviders.ConfigureAsync("idp_1", new Dictionary<string, object?> { ["enabled"] = true });
        Expect(rec, "PATCH", "/identity-providers/idp_1", "{\"enabled\":true}");
        await org.Org.Gate.IdpProviders.DeleteAsync("idp_1");
        Expect(rec, "DELETE", "/identity-providers/idp_1", null);
    }

    [Fact]
    public async Task OrgSmtpSendsPasswordRefAndFromAddress()
    {
        var (_, org, rec) = Make();
        await org.Org.Smtp.SetAsync(new SetSmtpInput { Host = "smtp.x.io", Port = 587, Username = "u", PasswordRef = "ref_1", FromAddress = "a@b.co", FromName = "A" });
        Expect(rec, "PATCH", "/org",
            "{\"smtpConfig\":{\"host\":\"smtp.x.io\",\"port\":587,\"username\":\"u\",\"passwordRef\":\"ref_1\",\"fromAddress\":\"a@b.co\",\"fromName\":\"A\"}}");
        await org.Org.Smtp.SetAsync(new SetSmtpInput { Host = "smtp.x.io", Port = 587, Username = "u", PasswordRef = "ref_1", FromAddress = "a@b.co" });
        Expect(rec, "PATCH", "/org",
            "{\"smtpConfig\":{\"host\":\"smtp.x.io\",\"port\":587,\"username\":\"u\",\"passwordRef\":\"ref_1\",\"fromAddress\":\"a@b.co\"}}");
        await org.Org.Smtp.TestAsync("a@b.co");
        Expect(rec, "POST", "/org/smtp/test-email", "{\"to\":\"a@b.co\"}");
        await org.Org.Smtp.TestAsync("a@b.co", "Hi");
        Expect(rec, "POST", "/org/smtp/test-email", "{\"to\":\"a@b.co\",\"subject\":\"Hi\"}");
        await org.Org.Smtp.UnsetAsync();
        Expect(rec, "PATCH", "/org", "{\"smtpConfig\":null}");
    }

    [Fact]
    public async Task OrgDefaultLlmUsesDefaultLlmRoute()
    {
        var (_, org, rec) = Make();
        await org.Org.DefaultLlm.GetAsync();
        Expect(rec, "GET", "/default-llm", null);
        await org.Org.DefaultLlm.SetAsync("llama-3-8b");
        Expect(rec, "PATCH", "/default-llm", "{\"modelId\":\"llama-3-8b\"}");
    }

    [Fact]
    public async Task OrgsRoutes()
    {
        var (_, org, rec) = Make();
        await org.Orgs.ListAsync();
        Expect(rec, "GET", "/orgs", null);
        await org.Orgs.GetAsync("acme");
        Expect(rec, "GET", "/orgs/acme", null);
        await org.Orgs.RestoreAsync("org_1");
        Expect(rec, "POST", "/orgs/org_1/restore", null);
        await org.Orgs.CurrentAsync();
        Expect(rec, "GET", "/org", null);
    }

    // ---- me ----

    [Fact]
    public async Task MeSessionsAndTokens()
    {
        var (_, org, rec) = Make();
        await org.Me.Profile.GetAsync();
        Expect(rec, "GET", "/me", null);
        await org.Me.Profile.UpdateAsync("A");
        Expect(rec, "PATCH", "/me", "{\"displayName\":\"A\"}");
        await org.Me.Sessions.ListAsync();
        Expect(rec, "GET", "/me/sessions", null);
        await org.Me.Sessions.RevokeAsync("s_1");
        Expect(rec, "POST", "/auth/sessions/s_1/revoke", null);
        await org.Me.Sessions.RevokeAllAsync();
        Expect(rec, "POST", "/me/sessions/revoke-all", null);
        await org.Me.ApiTokens.ListAsync();
        Expect(rec, "GET", "/me/api-tokens", null);
        await org.Me.ApiTokens.CreateAsync("ci", new ScopeManifest { Operations = new() { "read" }, Projects = new() { "portal" } }, 3600);
        Expect(rec, "POST", "/me/api-tokens",
            "{\"label\":\"ci\",\"scopeManifest\":{\"operations\":[\"read\"],\"projects\":[\"portal\"],\"resourceClasses\":null,\"collections\":null,\"cidrs\":null},\"ttlSeconds\":3600}");
        await org.Me.ApiTokens.DeleteAsync("pat_1");
        Expect(rec, "DELETE", "/me/api-tokens/pat_1", null);
    }

    // ---- compute / ai ----

    [Fact]
    public async Task ComputeLlmsSendFlatParams()
    {
        var (c, _, rec) = Make();
        await c.Compute.Llms.ListAsync();
        Expect(rec, "GET", "/@portal/compute/llms", null);
        await c.Compute.Llms.ReadAsync("llama-3-8b");
        Expect(rec, "GET", "/@portal/compute/llms/llama-3-8b", null);
        await c.Compute.Llms.InvokeAsync("llama-3-8b", new LlmInvokeInput { Prompt = "hi", Params = new() { ["maxTokens"] = 10, ["temperature"] = 0.2 } });
        Expect(rec, "POST", "/@portal/compute/llms/llama-3-8b/invoke", "{\"prompt\":\"hi\",\"maxTokens\":10,\"temperature\":0.2}");
        await c.Compute.Llms.InvokeAsync("llama-3-8b", new LlmInvokeInput { Prompt = "hi" });
        Expect(rec, "POST", "/@portal/compute/llms/llama-3-8b/invoke", "{\"prompt\":\"hi\"}");
    }

    [Fact]
    public async Task ComputeLlmsInvokeReadsContentAndUsage()
    {
        var (c, _, rec) = Make();
        rec.Response = "{\"data\":{\"modelId\":\"llama-3-8b\",\"content\":\"hello\",\"finishReason\":\"stop\",\"usage\":{\"promptTokens\":1,\"completionTokens\":2,\"totalTokens\":3},\"usageType\":\"sdk.direct\"},\"meta\":{}}";
        var r = (await c.Compute.Llms.InvokeAsync("llama-3-8b", new LlmInvokeInput { Prompt = "hi" })).Data!;
        Assert.Equal("hello", r.Content);
        Assert.Equal(3, r.Usage!.TotalTokens);
    }

    [Fact]
    public async Task ComputeLlmsStreamBodyIsFlat()
    {
        var (c, _, rec) = Make();
        rec.ResponseType = "text/event-stream";
        rec.Response = "data: [DONE]\n\n";
        await foreach (var _ in c.Compute.Llms.InvokeStreamAsync("llama-3-8b", new LlmInvokeInput { Prompt = "hi", Params = new() { ["maxTokens"] = 5 } })) { }
        Expect(rec, "POST", "/@portal/compute/llms/llama-3-8b/invoke-stream", "{\"prompt\":\"hi\",\"maxTokens\":5}");
    }

    [Fact]
    public async Task AiEmbedBody()
    {
        var (c, _, rec) = Make();
        await c.Ai.EmbedAsync(new[] { "a", "b" }, "m");
        Expect(rec, "POST", "/@portal/ai/embeddings", "{\"model\":\"m\",\"input\":[\"a\",\"b\"]}");
    }

    // ---- studio ----

    [Fact]
    public async Task WorkflowsWireShapes()
    {
        var (c, _, rec) = Make();
        await c.Studio.Workflows.TriggerAsync("wfl_1", new Dictionary<string, object?> { ["k"] = "v" });
        Expect(rec, "POST", "/@portal/workflows/wfl_1/runs", "{\"triggerContext\":{\"k\":\"v\"}}");
        await c.Studio.Workflows.TriggerAsync("wfl_1");
        Expect(rec, "POST", "/@portal/workflows/wfl_1/runs", "{\"triggerContext\":{}}");
        await c.Studio.Workflows.ListAsync("active", 5, "c");
        Expect(rec, "GET", "/@portal/workflows?status=active&limit=5&cursor=c", null);
        await c.Studio.Workflows.Runs.ListAsync("wfl_1", 5, "c");
        Expect(rec, "GET", "/@portal/workflows/wfl_1/runs?limit=5&cursor=c", null);
        await c.Studio.Workflows.CreateAsync(new CreateWorkflowInput
        {
            Name = "w", TriggerType = "manual", TriggerConfig = new(),
            Actions = new() { new() { ["index"] = 0, ["action_type"] = "branch", ["config"] = new Dictionary<string, object?>() } },
        });
        Expect(rec, "POST", "/@portal/workflows",
            "{\"name\":\"w\",\"triggerType\":\"manual\",\"triggerConfig\":{},\"actions\":[{\"index\":0,\"action_type\":\"branch\",\"config\":{}}]}");
    }

    [Fact]
    public async Task DashboardsWireShapes()
    {
        var (c, _, rec) = Make();
        await c.Studio.Dashboards.ListAsync("active", true, 5, "c");
        Expect(rec, "GET", "/@portal/dashboards?status=active&pinnedOnly=true&limit=5&cursor=c", null);
        await c.Studio.Dashboards.CreateAsync(new CreateDashboardInput { Name = "d" });
        Expect(rec, "POST", "/@portal/dashboards", "{\"name\":\"d\"}");
    }

    // ---- realtime ----

    [Fact]
    public async Task RealtimeSubscribeIsASseGetWithTopicAndFilterAndSkipsResync()
    {
        var (c, _, rec) = Make();
        rec.ResponseType = "text/event-stream";
        rec.Response = "event: resync\ndata: {\"reason\":\"cursor-evicted\"}\n\nevent: x\ndata: {\"topic\":\"t\",\"eventType\":\"x\",\"payload\":{},\"occurredAt\":\"now\"}\n\n";
        var types = new List<string>();
        await foreach (var ev in c.Realtime.SubscribeAsync("collections.orders.*", new Dictionary<string, object?> { ["status"] = "open" }))
            types.Add(ev.EventType);
        Assert.Equal(new[] { "x" }, types);
        Assert.Equal("GET", rec.Method);
        var (path, q) = Split(rec.Target!);
        Assert.Equal("/@portal/realtime/subscribe", path);
        Assert.Equal("collections.orders.%2a", q["topic"].Replace("*", "%2a"));
        Assert.Contains("status", Uri.UnescapeDataString(q["filter"]));
    }

    // ---- migrations / gate / storage / vault bodies ----

    [Fact]
    public async Task MigrationsBodies()
    {
        var (c, _, rec) = Make();
        var m = new Migration { Version = 1, Slug = "init", Up = new(), Down = new() };
        await c.Migrations.ApplyAsync(new() { m });
        Expect(rec, "POST", "/@portal/migrations/apply", "{\"migrations\":[{\"version\":1,\"slug\":\"init\",\"up\":[],\"down\":[]}]}");
        await c.Migrations.QueueAsync(new() { m });
        Expect(rec, "POST", "/@portal/migrations/queue", "{\"migrations\":[{\"version\":1,\"slug\":\"init\",\"up\":[],\"down\":[]}]}");
        await c.Migrations.RejectPendingAsync("pm_1", "no");
        Expect(rec, "POST", "/@portal/migrations/pending/pm_1/reject", "{\"reason\":\"no\"}");
        await c.Migrations.RejectPendingAsync("pm_1");
        Expect(rec, "POST", "/@portal/migrations/pending/pm_1/reject", null);
        await c.Migrations.ListPendingAsync();
        Expect(rec, "GET", "/@portal/migrations/pending", null);
        await c.Migrations.DeletePendingAsync("pm_1");
        Expect(rec, "DELETE", "/@portal/migrations/pending/pm_1", null);
        await c.Migrations.GetAsync("1");
        Expect(rec, "GET", "/@portal/migrations/1", null);
    }

    [Fact]
    public async Task GateBodies()
    {
        var (c, _, rec) = Make();
        await c.Gate.SignInAsync(new SignInInput { Email = "a@b.co", Password = "pw" });
        Expect(rec, "POST", "/@portal/gate/signin", "{\"email\":\"a@b.co\",\"password\":\"pw\"}");
        await c.Gate.SignUpAsync(new SignUpInput { Email = "a@b.co", Password = "pw" });
        Expect(rec, "POST", "/@portal/gate/signup", "{\"email\":\"a@b.co\",\"password\":\"pw\"}");
        await c.Gate.SignOutAsync(new SignOutInput { RefreshToken = "rt" });
        Expect(rec, "POST", "/@portal/gate/signout", "{\"refreshToken\":\"rt\"}");
        await c.Gate.RefreshTokenAsync(new RefreshTokenInput { RefreshToken = "rt" });
        Expect(rec, "POST", "/@portal/gate/refresh", "{\"refreshToken\":\"rt\"}");
        await c.Gate.WhoamiAsync();
        Expect(rec, "GET", "/@portal/gate/whoami", null);
    }

    [Fact]
    public async Task StorageBodies()
    {
        var (c, _, rec) = Make();
        await c.Storage.CreateBucketAsync("docs", new CreateBucketOptions { Public = false });
        Expect(rec, "POST", "/@portal/storage/buckets", "{\"name\":\"docs\",\"public\":false}");
        await c.Storage.UpdateBucketAsync("docs", new Dictionary<string, object?> { ["public"] = true });
        Expect(rec, "PATCH", "/@portal/storage/buckets/docs", "{\"public\":true}");
        await c.Storage.From("docs").CreateSignedUrlAsync("a.txt", new SignedUrlOptions { ExpiresIn = 600 });
        Expect(rec, "POST", "/@portal/storage/buckets/docs/objects/sign", "{\"path\":\"a.txt\",\"expiresIn\":600}");
        await c.Storage.From("docs").RemoveAsync("a.txt");
        Expect(rec, "DELETE", "/@portal/storage/buckets/docs/objects?path=a.txt", null);
    }
}
