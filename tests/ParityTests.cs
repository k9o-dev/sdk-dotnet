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
/// Full-coverage parity tests for the namespaces bringing .NET to TS
/// surface parity: Migrations / Functions / Policies / Audit / Orgs / Me /
/// full Org. Mocks the gateway via a custom HttpMessageHandler.
/// </summary>
public class ParityTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        public string? LastMethod;
        public string? LastPath;
        public string? LastBody;
        public Func<HttpRequestMessage, string> Responder = _ => "{\"data\":null,\"meta\":{}}";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastMethod = request.Method.Method;
            LastPath = request.RequestUri!.PathAndQuery;
            LastBody = request.Content != null ? await request.Content.ReadAsStringAsync(cancellationToken) : null;
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(Responder(request), Encoding.UTF8, "application/json"),
            };
        }
    }

    private static (K9oClient, StubHandler) Client(Func<HttpRequestMessage, string> responder, string? projectSlug = "portal")
    {
        var stub = new StubHandler { Responder = responder };
        var opts = new K9oOptions { BaseUrl = "https://acme.api.k9o.io", HttpClient = new HttpClient(stub) };
        if (projectSlug != null) opts = new K9oOptions { BaseUrl = "https://acme.api.k9o.io", ProjectSlug = projectSlug, HttpClient = new HttpClient(stub) };
        return (K9oClient.Create(opts), stub);
    }

    [Fact]
    public async Task MigrationsApplyAndPending()
    {
        var (c, stub) = Client(req =>
            req.RequestUri!.AbsolutePath.EndsWith("/apply") && req.RequestUri.AbsolutePath.Contains("pending")
                ? "{\"data\":{\"version\":1,\"slug\":\"init\",\"checksum\":\"c\",\"appliedAt\":\"t\"},\"meta\":{}}"
                : "{\"data\":{\"applied\":[]},\"meta\":{}}");
        // Representative structured migration: a create_table up + drop_table down,
        // exercising the encrypted union (bare-true + { store, searchable }) and
        // the key-tagged Operation shape.
        var migration = new Migration
        {
            Version = 1,
            Slug = "init",
            Up = new List<Operation>
            {
                Operation.OfCreateTable(new CreateTableOp
                {
                    Name = "users",
                    Columns = new List<ColumnSpec>
                    {
                        new() { Name = "id", Type = "uuid", PrimaryKey = true },
                        new() { Name = "email", Type = "text", NotNull = true, Unique = true },
                        new() { Name = "ssn", Type = "text", Encrypted = ColumnEncryption.Inline },
                        new() { Name = "phone", Type = "text", Encrypted = new ColumnEncryption { Store = "vault", Searchable = true } },
                    },
                    Indexes = new List<IndexSpec>
                    {
                        new() { Name = "users_email_idx", Columns = new List<string> { "email" }, Unique = true },
                    },
                }),
            },
            Down = new List<Operation> { Operation.OfDropTable("users") },
        };
        await c.Migrations.ApplyAsync(new List<Migration> { migration });
        Assert.Equal("/@portal/migrations/apply", stub.LastPath);

        // Wire-shape assertions: snake_case keys + nested `searchable` inside the
        // `encrypted` object union; bare-true shorthand for the inline form.
        Assert.Contains("\"create_table\"", stub.LastBody);
        Assert.Contains("\"primary_key\":true", stub.LastBody);
        Assert.Contains("\"not_null\":true", stub.LastBody);
        Assert.Contains("\"encrypted\":true", stub.LastBody);
        Assert.Contains("\"encrypted\":{\"store\":\"vault\",\"searchable\":true}", stub.LastBody);
        Assert.Contains("\"drop_table\":\"users\"", stub.LastBody);

        var applied = (await c.Migrations.ApplyPendingAsync("pend_x")).Data!;
        Assert.Equal(1, applied.Version);
    }

    [Fact]
    public async Task FunctionsDeployAndInvoke()
    {
        var (c, stub) = Client(_ => "{\"data\":{\"id\":\"fn_x\",\"name\":\"greet\",\"runtime\":\"bun\",\"status\":\"deploying\",\"tier\":\"low\",\"createdAt\":\"t\"},\"meta\":{}}");
        var f = (await c.Functions.DeployAsync(new DeployFunctionInput { Name = "greet", Runtime = "bun", Code = "..." })).Data!;
        Assert.Equal("greet", f.Name);
        await c.Functions.InvokeAsync("greet", new Dictionary<string, object?> { ["name"] = "World" });
        Assert.Equal("/@portal/functions/greet/invoke", stub.LastPath);
    }

    [Fact]
    public async Task PoliciesCreateAndPublish()
    {
        var (c, stub) = Client(_ => "{\"data\":{\"id\":\"pol_x\",\"name\":\"rls\",\"version\":1,\"status\":\"draft\",\"rules\":{},\"createdAt\":\"t\",\"createdBy\":\"u\"},\"meta\":{}}");
        var created = (await c.Policies.CreateAsync("rls", new Dictionary<string, object?> { ["a"] = 1 })).Data!;
        Assert.Equal("pol_x", created.Id);
        await c.Policies.PublishAsync("pol_x");
        Assert.Equal("/@portal/policies/pol_x/publish", stub.LastPath);
    }

    [Fact]
    public async Task AuditQuery()
    {
        var (c, stub) = Client(_ => "{\"data\":[],\"meta\":{}}");
        await c.Audit.QueryAsync(new AuditQueryOpts { Type = "data.write.access.insert", Limit = 50 });
        Assert.StartsWith("/@portal/audit-log", stub.LastPath);
        Assert.Contains("type=data.write.access.insert", stub.LastPath);
    }

    [Fact]
    public async Task OrgsCollection()
    {
        var (c, stub) = Client(req => req.RequestUri!.AbsolutePath == "/orgs"
            ? "{\"data\":[],\"meta\":{}}"
            : "{\"data\":{\"id\":\"org_x\",\"slug\":\"acme\",\"name\":\"Acme\",\"status\":\"active\",\"createdAt\":\"t\"},\"meta\":{}}", projectSlug: null);
        await c.Orgs.ListAsync();
        Assert.Equal("/orgs", stub.LastPath);
        var restored = (await c.Orgs.RestoreAsync("org_x")).Data!;
        Assert.Equal("acme", restored.Slug);
    }

    [Fact]
    public async Task MeNamespaces()
    {
        var (c, stub) = Client(_ => "{\"data\":{},\"meta\":{}}", projectSlug: null);
        await c.Me.Profile.GetAsync();
        Assert.Equal("/me", stub.LastPath);
        await c.Me.Sessions.RevokeAllAsync();
        Assert.Equal("DELETE", stub.LastMethod);
        await c.Me.ApiTokens.CreateAsync("ci");
        Assert.Equal("/me/api-tokens", stub.LastPath);
    }

    [Fact]
    public async Task OrgFullSurfaces()
    {
        var (c, stub) = Client(req => req.RequestUri!.AbsolutePath.EndsWith("/default-llm")
            ? "{\"data\":\"mistral-7b\",\"meta\":{}}"
            : "{\"data\":{},\"meta\":{}}", projectSlug: null);
        await c.Org.Members.InviteAsync("x@y.com", "ORG_MEMBER");
        Assert.Equal("POST", stub.LastMethod);
        Assert.Equal("/members", stub.LastPath);
        await c.Org.Gate.Roles.GrantAsync("u_a", "ORG_ADMIN");
        Assert.Equal("/org-role-grants", stub.LastPath);
        await c.Org.Smtp.SetAsync(new SetSmtpInput { Host = "smtp.x.com", Port = 587, Username = "u", Password = "p", FromEmail = "n@x.com", FromName = "X", Encryption = "starttls" });
        Assert.Equal("PATCH", stub.LastMethod);
        Assert.Contains("smtpConfig", stub.LastBody);
        var llm = (await c.Org.DefaultLlm.GetAsync()).Data;
        Assert.Equal("mistral-7b", llm);
    }
}
