using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using K9o;
using Xunit;

namespace K9o.Tests;

/// <summary>Client-side validation of approvals, approval policies and schema collections: K9O_INVALID_FIELD_VALUE before any request.</summary>
public class ApprovalsValidationTests
{
    private sealed class Counting : HttpMessageHandler
    {
        public int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref Requests);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"data\":null,\"meta\":{}}", System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }

    private static readonly Dictionary<string, object?> Pred = new() { ["gt"] = new object[] { "change.row.amount", 1000 } };

    private static (K9oClient c, Counting h) Make(string? slug = "portal")
    {
        var h = new Counting();
        var c = K9oClient.Create(new K9oOptions { BaseUrl = "http://localhost:6000", ProjectSlug = slug, HttpClient = new HttpClient(h) });
        return (c, h);
    }

    private static async Task Invalid(Counting h, Func<Task> call)
    {
        var e = await Assert.ThrowsAsync<KError>(call);
        Assert.Equal("K9O_INVALID_FIELD_VALUE", e.Code);
        Assert.Equal(0, h.Requests);
    }

    private static CreateApprovalPolicyInput Policy(string collection = "orders", string op = "insert", string action = "queue", IReadOnlyDictionary<string, object?>? predicate = null) =>
        new() { Collection = collection, Operation = op, Action = action, Predicate = predicate };

    [Fact]
    public async Task ListLimitRange()
    {
        var (c, h) = Make();
        await Invalid(h, () => c.Approvals.ListAsync(null, 0));
        await Invalid(h, () => c.Approvals.ListAsync(null, 201));
        await Invalid(h, () => c.Approvals.ListAsync("approved", -1));
        await Invalid(h, () => c.Schema.Collections.ListAsync(0));
        await Invalid(h, () => c.Schema.Collections.ListAsync(201));
        await c.Approvals.ListAsync(null, 1);
        await c.Approvals.ListAsync(null, 200);
        await c.Schema.Collections.ListAsync(200);
        Assert.Equal(3, h.Requests);
    }

    [Fact]
    public async Task IdsAndReasons()
    {
        var (c, h) = Make();
        await Invalid(h, () => c.Approvals.GetAsync(""));
        await Invalid(h, () => c.Approvals.GetAsync(null!));
        await Invalid(h, () => c.Approvals.ApproveAsync(""));
        await Invalid(h, () => c.Approvals.RejectAsync("apr_1", null!));
        await Invalid(h, () => c.Approvals.RejectAsync("apr_1", ""));
        await Invalid(h, () => c.Approvals.RejectAsync("apr_1", "   "));
        await Invalid(h, () => c.Approvals.RejectAsync("", "no"));
        await Invalid(h, () => c.ApprovalPolicies.DeleteAsync(""));
    }

    [Fact]
    public async Task Bulk()
    {
        var (c, h) = Make();
        await Invalid(h, () => c.Approvals.BulkAsync("delete", Pred));
        await Invalid(h, () => c.Approvals.BulkAsync(null!, Pred));
        await Invalid(h, () => c.Approvals.BulkAsync("approve", null!));
        await Invalid(h, () => c.Approvals.BulkAsync("reject", Pred));
        await Invalid(h, () => c.Approvals.BulkAsync("reject", Pred, " "));
    }

    [Fact]
    public async Task PolicyCreate()
    {
        var (c, h) = Make();
        var p = c.ApprovalPolicies;
        await Invalid(h, () => p.CreateAsync(null!));
        await Invalid(h, () => p.CreateAsync(Policy(collection: null!, predicate: Pred)));
        await Invalid(h, () => p.CreateAsync(Policy(collection: "1orders", predicate: Pred)));
        await Invalid(h, () => p.CreateAsync(Policy(collection: "or-ders", predicate: Pred)));
        await Invalid(h, () => p.CreateAsync(Policy(collection: new string('a', 64), predicate: Pred)));
        await Invalid(h, () => p.CreateAsync(Policy(op: "upsert", predicate: Pred)));
        await Invalid(h, () => p.CreateAsync(Policy(op: null!, predicate: Pred)));
        await Invalid(h, () => p.CreateAsync(Policy(action: "block", predicate: Pred)));
        await Invalid(h, () => p.CreateAsync(Policy(action: null!, predicate: Pred)));
        await Invalid(h, () => p.CreateAsync(Policy(predicate: null)));
        await p.CreateAsync(Policy(collection: new string('a', 63), op: "delete", action: "auto_approve", predicate: Pred));
        await p.CreateAsync(Policy(collection: "_x", predicate: new Dictionary<string, object?>()));
        Assert.Equal(2, h.Requests);
    }

    [Fact]
    public async Task ProjectSlugRequired()
    {
        var (c, _) = Make(slug: null);
        Assert.Equal("K9O_MISSING_PROJECT_CONTEXT", (await Assert.ThrowsAsync<KError>(() => c.Approvals.ListAsync())).Code);
        Assert.Equal("K9O_MISSING_PROJECT_CONTEXT", (await Assert.ThrowsAsync<KError>(() => c.ApprovalPolicies.ListAsync())).Code);
        Assert.Equal("K9O_MISSING_PROJECT_CONTEXT", (await Assert.ThrowsAsync<KError>(() => c.Schema.Collections.ListAsync())).Code);
    }
}
