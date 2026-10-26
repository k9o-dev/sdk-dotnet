using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace K9o;

/// <summary>
/// Pending-change queue namespace: <c>client.Approvals</c>.
///
/// Wire (project plane): <c>ListAsync</c> is <c>GET /@slug/pending-approvals</c>; <c>GetAsync</c> is
/// <c>GET .../pending-approvals/id</c>; <c>ApproveAsync</c> and <c>RejectAsync</c> are <c>POST .../id/approve</c> and
/// <c>.../id/reject</c>; <c>BulkAsync</c> is <c>POST .../pending-approvals/bulk</c>.
///
/// <c>ListAsync</c> is not paginated: it returns the newest <c>limit</c> changes (1 to 200, default 50). Project admins
/// and auditors see the whole queue; other members see only the changes they proposed.
/// </summary>
public sealed class Approvals
{
    private readonly K9oClient _client;
    internal Approvals(K9oClient client) => _client = client;

    internal static KError Invalid(string message, IDictionary<string, object?>? context = null) =>
        new("K9O_INVALID_FIELD_VALUE", message, context, null, null, null);

    private string Path(string tail)
    {
        if (string.IsNullOrEmpty(_client.Config.ProjectSlug))
            throw new KError("K9O_MISSING_PROJECT_CONTEXT", "approvals operations require ProjectSlug bound on the client");
        return UrlGrammar.ProjectPath(_client.Config.ProjectSlug!, "pending-approvals" + tail);
    }

    private static string RequireId(string? id)
    {
        if (string.IsNullOrEmpty(id)) throw Invalid("approval id must be a non-empty string");
        return HttpUtility.UrlEncode(id).Replace("+", "%20");
    }

    internal static string RequireReason(string? reason)
    {
        if (reason == null || reason.Trim().Length == 0) throw Invalid("reason is required and must be a non-empty string");
        return reason;
    }

    internal static IReadOnlyDictionary<string, object?> RequirePredicate(IReadOnlyDictionary<string, object?>? value, string field)
    {
        if (value == null) throw Invalid($"{field} must be a predicate object");
        return value;
    }

    /// <summary>
    /// List pending changes, newest first. <paramref name="status"/> (optional) is <c>pending_approval</c>, <c>approved</c>,
    /// <c>rejected</c>, <c>expired</c> or <c>redeemed</c>; <paramref name="limit"/> (optional) is 1 to 200. Not paginated:
    /// only <paramref name="limit"/> caps the result.
    /// </summary>
    public async Task<Envelope<List<PendingChange>>> ListAsync(string? status = null, int? limit = null, CancellationToken ct = default)
    {
        if (limit is < 1 or > 200)
            throw Invalid("limit must be a whole number from 1 to 200", new Dictionary<string, object?> { ["limit"] = limit });
        var p = Path("");
        var q = new Dictionary<string, string>();
        if (status != null) q["status"] = status;
        if (limit != null) q["limit"] = limit.Value.ToString();
        return await HttpTransport.RequestAsync<List<PendingChange>>(_client.Config, HttpMethod.Get, p, null,
            q.Count > 0 ? q : null, ct).ConfigureAwait(false);
    }

    /// <summary>One pending change. Members who are not queue admins can read only their own.</summary>
    public async Task<Envelope<PendingChange>> GetAsync(string id, CancellationToken ct = default)
    {
        var p = Path("/" + RequireId(id));
        return await HttpTransport.RequestAsync<PendingChange>(_client.Config, HttpMethod.Get, p, null, null, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Approve a pending change; <paramref name="reason"/> (optional) is stored with the decision. Read <c>Status</c> on the
    /// answer: <c>pending_approval</c> means the approval was recorded but more approvers are needed and the change has not
    /// been applied yet.
    /// </summary>
    public async Task<Envelope<PendingChange>> ApproveAsync(string id, string? reason = null, CancellationToken ct = default)
    {
        var p = Path("/" + RequireId(id) + "/approve");
        var body = new Dictionary<string, object?>();
        if (reason != null) body["reason"] = reason;
        return await HttpTransport.RequestAsync<PendingChange>(_client.Config, HttpMethod.Post, p, body, null, ct).ConfigureAwait(false);
    }

    /// <summary>Reject a pending change. <paramref name="reason"/> is required and must be non-empty.</summary>
    public async Task<Envelope<PendingChange>> RejectAsync(string id, string reason, CancellationToken ct = default)
    {
        var p = Path("/" + RequireId(id) + "/reject");
        var body = new Dictionary<string, object?> { ["reason"] = RequireReason(reason) };
        return await HttpTransport.RequestAsync<PendingChange>(_client.Config, HttpMethod.Post, p, body, null, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Approve or reject every pending change matching <paramref name="criteria"/>. <paramref name="action"/> is <c>approve</c>
    /// or <c>reject</c>; <paramref name="criteria"/> is a predicate such as <c>{ ["eq"] = new object[] { "change.table", "orders" } }</c>;
    /// <paramref name="reason"/> is required for <c>reject</c>. Rows that fail are skipped without a report, so compare
    /// <c>AffectedIds</c> with what you intended.
    /// </summary>
    public async Task<Envelope<BulkApprovalsResult>> BulkAsync(string action, IReadOnlyDictionary<string, object?> criteria, string? reason = null, CancellationToken ct = default)
    {
        if (action != "approve" && action != "reject") throw Invalid("action must be \"approve\" or \"reject\"");
        var pred = RequirePredicate(criteria, "criteria");
        var body = new Dictionary<string, object?> { ["action"] = action, ["criteria"] = pred };
        if (action == "reject") body["reason"] = RequireReason(reason);
        else if (reason != null) body["reason"] = reason;
        var p = Path("/bulk");
        return await HttpTransport.RequestAsync<BulkApprovalsResult>(_client.Config, HttpMethod.Post, p, body, null, ct).ConfigureAwait(false);
    }
}

/// <summary>A change waiting for (or past) a decision.</summary>
public sealed class PendingChange
{
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    /// <summary><c>pending_approval</c>, <c>approved</c>, <c>rejected</c>, <c>expired</c> or <c>redeemed</c>.</summary>
    [JsonPropertyName("status")] public string Status { get; init; } = "";
    [JsonPropertyName("collection")] public string Collection { get; init; } = "";
    /// <summary><c>insert</c>, <c>update</c> or <c>delete</c>.</summary>
    [JsonPropertyName("operation")] public string Operation { get; init; } = "";
    [JsonPropertyName("makerId")] public string MakerId { get; init; } = "";
    [JsonPropertyName("proposedPayload")] public Dictionary<string, object?>? ProposedPayload { get; init; }
    [JsonPropertyName("targetRecordId")] public string? TargetRecordId { get; init; }
    [JsonPropertyName("approverId")] public string? ApproverId { get; init; }
    [JsonPropertyName("decidedAt")] public string? DecidedAt { get; init; }
    [JsonPropertyName("decisionReason")] public string? DecisionReason { get; init; }
    [JsonPropertyName("createdAt")] public string CreatedAt { get; init; } = "";
    [JsonPropertyName("expiresAt")] public string? ExpiresAt { get; init; }
}

/// <summary>
/// Answer of <c>BulkAsync</c>. Rows that fail (conflict, apply error) are skipped silently and left out of
/// <c>AffectedIds</c>: compare it with the set you meant to decide.
/// </summary>
public sealed class BulkApprovalsResult
{
    [JsonPropertyName("attestationId")] public string AttestationId { get; init; } = "";
    [JsonPropertyName("action")] public string Action { get; init; } = "";
    [JsonPropertyName("affectedCount")] public int AffectedCount { get; init; }
    [JsonPropertyName("affectedIds")] public List<string> AffectedIds { get; init; } = new();
    [JsonPropertyName("reason")] public string? Reason { get; init; }
}
