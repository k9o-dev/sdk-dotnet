using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace K9o;

/// <summary>
/// Approval policies namespace: <c>client.ApprovalPolicies</c>, the rules that decide which writes are queued.
/// Wire (project plane): <c>GET</c> and <c>POST /@slug/approval-policies</c>, <c>DELETE /@slug/approval-policies/id</c>.
/// The first matching policy for a write decides.
/// </summary>
public sealed class ApprovalPolicies
{
    private static readonly Regex CollectionRe = new("^[a-zA-Z_][a-zA-Z0-9_]{0,62}$", RegexOptions.CultureInvariant);

    private readonly K9oClient _client;
    internal ApprovalPolicies(K9oClient client) => _client = client;

    private string Path(string tail)
    {
        if (string.IsNullOrEmpty(_client.Config.ProjectSlug))
            throw new KError("K9O_MISSING_PROJECT_CONTEXT", "approvalPolicies operations require ProjectSlug bound on the client");
        return UrlGrammar.ProjectPath(_client.Config.ProjectSlug!, "approval-policies" + tail);
    }

    /// <summary>All policies, oldest first.</summary>
    public async Task<Envelope<List<ApprovalPolicy>>> ListAsync(CancellationToken ct = default)
    {
        var p = Path("");
        return await HttpTransport.RequestAsync<List<ApprovalPolicy>>(_client.Config, HttpMethod.Get, p, null, null, ct).ConfigureAwait(false);
    }

    /// <summary>Create a policy. Identical policies are not de-duplicated.</summary>
    public async Task<Envelope<ApprovalPolicy>> CreateAsync(CreateApprovalPolicyInput input, CancellationToken ct = default)
    {
        var p = Path("");
        if (input == null) throw Approvals.Invalid("input must be an object");
        if (input.Collection == null || !CollectionRe.IsMatch(input.Collection))
            throw Approvals.Invalid("collection must match ^[a-zA-Z_][a-zA-Z0-9_]{0,62}$");
        if (input.Operation != "insert" && input.Operation != "update" && input.Operation != "delete")
            throw Approvals.Invalid("operation must be \"insert\", \"update\" or \"delete\"");
        if (input.Action != "auto_approve" && input.Action != "queue")
            throw Approvals.Invalid("action must be \"auto_approve\" or \"queue\"");
        var predicate = Approvals.RequirePredicate(input.Predicate, "predicate");
        var body = new Dictionary<string, object?>
        {
            ["collection"] = input.Collection,
            ["operation"] = input.Operation,
            ["action"] = input.Action,
            ["predicate"] = predicate,
        };
        return await HttpTransport.RequestAsync<ApprovalPolicy>(_client.Config, HttpMethod.Post, p, body, null, ct).ConfigureAwait(false);
    }

    /// <summary>Delete a policy; answers <c>{ Id }</c>.</summary>
    public async Task<Envelope<DeletedApprovalPolicy>> DeleteAsync(string id, CancellationToken ct = default)
    {
        var p = Path("");
        if (string.IsNullOrEmpty(id)) throw Approvals.Invalid("policy id must be a non-empty string");
        return await HttpTransport.RequestAsync<DeletedApprovalPolicy>(_client.Config, HttpMethod.Delete,
            p + "/" + HttpUtility.UrlEncode(id).Replace("+", "%20"), null, null, ct).ConfigureAwait(false);
    }
}

/// <summary>
/// Policy to create. <c>Collection</c> matches <c>^[a-zA-Z_][a-zA-Z0-9_]{0,62}$</c>; <c>Operation</c> is <c>insert</c>,
/// <c>update</c> or <c>delete</c>; <c>Action</c> is <c>auto_approve</c> or <c>queue</c>; <c>Predicate</c> is a non-null
/// predicate object such as <c>{ ["gt"] = new object[] { "change.row.amount", 1000 } }</c>.
/// </summary>
public sealed class CreateApprovalPolicyInput
{
    public string Collection { get; init; } = "";
    public string Operation { get; init; } = "";
    public string Action { get; init; } = "";
    public IReadOnlyDictionary<string, object?>? Predicate { get; init; }
}

/// <summary>A rule: writes to <c>Collection</c> with <c>Operation</c> that match <c>Predicate</c> get <c>Action</c>.</summary>
public sealed class ApprovalPolicy
{
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    [JsonPropertyName("collection")] public string Collection { get; init; } = "";
    [JsonPropertyName("operation")] public string Operation { get; init; } = "";
    [JsonPropertyName("predicate")] public Dictionary<string, object?>? Predicate { get; init; }
    [JsonPropertyName("action")] public string Action { get; init; } = "";
    [JsonPropertyName("authoredBy")] public string AuthoredBy { get; init; } = "";
    [JsonPropertyName("createdAt")] public string CreatedAt { get; init; } = "";
}

/// <summary>Answer of <c>ApprovalPolicies.DeleteAsync</c>.</summary>
public sealed class DeletedApprovalPolicy
{
    [JsonPropertyName("id")] public string Id { get; init; } = "";
}
