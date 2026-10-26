using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Web;

namespace K9o;

/// <summary>
/// Studio namespace (workflows + dashboards).
/// .NET idiomatic: PascalCase Async methods + throw
/// <see cref="KError"/> on error + System.Text.Json records-as-classes.
/// </summary>
public sealed class Studio
{
    private readonly K9oClient _client;

    public Workflows Workflows { get; }
    public Dashboards Dashboards { get; }

    internal Studio(K9oClient client)
    {
        _client = client;
        Workflows = new Workflows(client);
        Dashboards = new Dashboards(client);
    }

    internal static string RequireProjectSlug(HttpClientConfig config, string surface)
    {
        if (string.IsNullOrEmpty(config.ProjectSlug))
        {
            throw new KError("K9O_MISSING_PROJECT_CONTEXT",
                $"{surface} operations require ProjectSlug bound on the client");
        }
        return config.ProjectSlug!;
    }
}

// ----------------------------------------------------------------------
// Workflows
// ----------------------------------------------------------------------

public sealed class Workflow
{
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    [JsonPropertyName("projectId")] public string ProjectId { get; init; } = "";
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("description")] public string Description { get; init; } = "";
    [JsonPropertyName("status")] public string Status { get; init; } = "";
    [JsonPropertyName("triggerType")] public string TriggerType { get; init; } = "";
    [JsonPropertyName("triggerConfig")] public Dictionary<string, object?>? TriggerConfig { get; init; }
    [JsonPropertyName("actions")] public List<Dictionary<string, object?>>? Actions { get; init; }
    [JsonPropertyName("rbacBinding")] public Dictionary<string, object?>? RbacBinding { get; init; }
    [JsonPropertyName("authoredBy")] public string AuthoredBy { get; init; } = "";
    [JsonPropertyName("authoredAt")] public string AuthoredAt { get; init; } = "";
    [JsonPropertyName("updatedBy")] public string UpdatedBy { get; init; } = "";
    [JsonPropertyName("updatedAt")] public string UpdatedAt { get; init; } = "";
}

public sealed class WorkflowRun
{
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    [JsonPropertyName("workflowId")] public string WorkflowId { get; init; } = "";
    [JsonPropertyName("projectId")] public string ProjectId { get; init; } = "";
    [JsonPropertyName("status")] public string Status { get; init; } = "";
    [JsonPropertyName("triggerContext")] public Dictionary<string, object?>? TriggerContext { get; init; }
    [JsonPropertyName("perStepStatus")] public List<Dictionary<string, object?>>? PerStepStatus { get; init; }
    [JsonPropertyName("costAccountingTotal")] public double? CostAccountingTotal { get; init; }
    [JsonPropertyName("errorContext")] public Dictionary<string, object?>? ErrorContext { get; init; }
    [JsonPropertyName("startedAt")] public string StartedAt { get; init; } = "";
    [JsonPropertyName("completedAt")] public string? CompletedAt { get; init; }
    [JsonPropertyName("firedBy")] public string FiredBy { get; init; } = "";
}

public sealed class CreateWorkflowInput
{
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("description")] public string? Description { get; init; }
    [JsonPropertyName("status")] public string? Status { get; init; }
    [JsonPropertyName("triggerType")] public required string TriggerType { get; init; }
    [JsonPropertyName("triggerConfig")] public required Dictionary<string, object?> TriggerConfig { get; init; }
    [JsonPropertyName("actions")] public required List<Dictionary<string, object?>> Actions { get; init; }
    [JsonPropertyName("rbacBinding")] public Dictionary<string, object?>? RbacBinding { get; init; }
}

public sealed class Workflows
{
    private readonly K9oClient _client;
    public WorkflowRuns Runs { get; }

    internal Workflows(K9oClient client)
    {
        _client = client;
        Runs = new WorkflowRuns(client);
    }

    /// <summary>List workflows; the route reads <c>status</c>, <c>limit</c> and <c>cursor</c>.</summary>
    public async Task<Envelope<List<Workflow>>> ListAsync(string? status = null, int? limit = null, string? cursor = null, CancellationToken ct = default)
    {
        var slug = Studio.RequireProjectSlug(_client.Config, "studio.workflows");
        var q = new Dictionary<string, string>();
        if (status != null) q["status"] = status;
        if (limit != null) q["limit"] = limit.Value.ToString();
        if (cursor != null) q["cursor"] = cursor;
        return await HttpTransport.RequestAsync<List<Workflow>>(
            _client.Config, HttpMethod.Get, UrlGrammar.ProjectPath(slug, "workflows"), null, q.Count > 0 ? q : null, ct).ConfigureAwait(false);
    }

    public async Task<Envelope<Workflow>> CreateAsync(CreateWorkflowInput input, CancellationToken ct = default)
    {
        var slug = Studio.RequireProjectSlug(_client.Config, "studio.workflows");
        return await HttpTransport.RequestAsync<Workflow>(
            _client.Config, HttpMethod.Post, UrlGrammar.ProjectPath(slug, "workflows"), input, null, ct).ConfigureAwait(false);
    }

    public async Task<Envelope<Workflow>> ReadAsync(string workflowId, CancellationToken ct = default)
    {
        var slug = Studio.RequireProjectSlug(_client.Config, "studio.workflows");
        return await HttpTransport.RequestAsync<Workflow>(
            _client.Config, HttpMethod.Get, UrlGrammar.ProjectPath(slug, $"workflows/{HttpUtility.UrlEncode(workflowId)}"), null, null, ct).ConfigureAwait(false);
    }

    public async Task<Envelope<Workflow>> UpdateAsync(string workflowId, Dictionary<string, object?> updates, CancellationToken ct = default)
    {
        var slug = Studio.RequireProjectSlug(_client.Config, "studio.workflows");
        return await HttpTransport.RequestAsync<Workflow>(
            _client.Config, HttpMethod.Patch, UrlGrammar.ProjectPath(slug, $"workflows/{HttpUtility.UrlEncode(workflowId)}"), updates, null, ct).ConfigureAwait(false);
    }

    public async Task<Envelope<Dictionary<string, object?>>> DeleteAsync(string workflowId, CancellationToken ct = default)
    {
        var slug = Studio.RequireProjectSlug(_client.Config, "studio.workflows");
        return await HttpTransport.RequestAsync<Dictionary<string, object?>>(
            _client.Config, HttpMethod.Delete, UrlGrammar.ProjectPath(slug, $"workflows/{HttpUtility.UrlEncode(workflowId)}"), null, null, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Manual-trigger a run (valid for <c>triggerType: manual</c> workflows only). The argument is sent as
    /// <c>{ triggerContext }</c>.
    /// </summary>
    public async Task<Envelope<WorkflowRun>> TriggerAsync(string workflowId, Dictionary<string, object?>? triggerContext = null, CancellationToken ct = default)
    {
        var slug = Studio.RequireProjectSlug(_client.Config, "studio.workflows");
        return await HttpTransport.RequestAsync<WorkflowRun>(
            _client.Config, HttpMethod.Post,
            UrlGrammar.ProjectPath(slug, $"workflows/{HttpUtility.UrlEncode(workflowId)}/runs"),
            new Dictionary<string, object?> { ["triggerContext"] = triggerContext ?? new Dictionary<string, object?>() }, null, ct).ConfigureAwait(false);
    }
}

public sealed class WorkflowRuns
{
    private readonly K9oClient _client;
    internal WorkflowRuns(K9oClient client) { _client = client; }

    /// <summary>List a workflow's runs (cursor pagination; the route reads <c>limit</c> and <c>cursor</c>).</summary>
    public async Task<Envelope<List<WorkflowRun>>> ListAsync(string workflowId, int? limit = null, string? cursor = null, CancellationToken ct = default)
    {
        var slug = Studio.RequireProjectSlug(_client.Config, "studio.workflows");
        var q = new Dictionary<string, string>();
        if (limit != null) q["limit"] = limit.Value.ToString();
        if (cursor != null) q["cursor"] = cursor;
        return await HttpTransport.RequestAsync<List<WorkflowRun>>(
            _client.Config, HttpMethod.Get,
            UrlGrammar.ProjectPath(slug, $"workflows/{HttpUtility.UrlEncode(workflowId)}/runs"), null, q.Count > 0 ? q : null, ct).ConfigureAwait(false);
    }

    public async Task<Envelope<WorkflowRun>> ReadAsync(string workflowId, string runId, CancellationToken ct = default)
    {
        var slug = Studio.RequireProjectSlug(_client.Config, "studio.workflows");
        return await HttpTransport.RequestAsync<WorkflowRun>(
            _client.Config, HttpMethod.Get,
            UrlGrammar.ProjectPath(slug, $"workflows/{HttpUtility.UrlEncode(workflowId)}/runs/{HttpUtility.UrlEncode(runId)}"), null, null, ct).ConfigureAwait(false);
    }

    public async Task<Envelope<WorkflowRun>> CancelAsync(string workflowId, string runId, CancellationToken ct = default)
    {
        var slug = Studio.RequireProjectSlug(_client.Config, "studio.workflows");
        return await HttpTransport.RequestAsync<WorkflowRun>(
            _client.Config, HttpMethod.Post,
            UrlGrammar.ProjectPath(slug, $"workflows/{HttpUtility.UrlEncode(workflowId)}/runs/{HttpUtility.UrlEncode(runId)}/cancel"), null, null, ct).ConfigureAwait(false);
    }
}

// ----------------------------------------------------------------------
// Dashboards
// ----------------------------------------------------------------------

public sealed class Dashboard
{
    [JsonPropertyName("id")] public string Id { get; init; } = "";
    [JsonPropertyName("projectId")] public string ProjectId { get; init; } = "";
    [JsonPropertyName("name")] public string Name { get; init; } = "";
    [JsonPropertyName("description")] public string Description { get; init; } = "";
    [JsonPropertyName("status")] public string Status { get; init; } = "";
    [JsonPropertyName("layoutGrid")] public Dictionary<string, object?>? LayoutGrid { get; init; }
    [JsonPropertyName("widgets")] public List<Dictionary<string, object?>>? Widgets { get; init; }
    [JsonPropertyName("filterConfig")] public Dictionary<string, object?>? FilterConfig { get; init; }
    [JsonPropertyName("pinnedToWorkspace")] public bool PinnedToWorkspace { get; init; }
    [JsonPropertyName("rbacBinding")] public Dictionary<string, object?>? RbacBinding { get; init; }
    [JsonPropertyName("authoredBy")] public string AuthoredBy { get; init; } = "";
    [JsonPropertyName("authoredAt")] public string AuthoredAt { get; init; } = "";
    [JsonPropertyName("updatedBy")] public string UpdatedBy { get; init; } = "";
    [JsonPropertyName("updatedAt")] public string UpdatedAt { get; init; } = "";
}

public sealed class CreateDashboardInput
{
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("description")] public string? Description { get; init; }
    [JsonPropertyName("status")] public string? Status { get; init; }
    [JsonPropertyName("layoutGrid")] public Dictionary<string, object?>? LayoutGrid { get; init; }
    [JsonPropertyName("widgets")] public List<Dictionary<string, object?>>? Widgets { get; init; }
    [JsonPropertyName("filterConfig")] public Dictionary<string, object?>? FilterConfig { get; init; }
    [JsonPropertyName("rbacBinding")] public Dictionary<string, object?>? RbacBinding { get; init; }
}

public sealed class Dashboards
{
    private readonly K9oClient _client;
    internal Dashboards(K9oClient client) { _client = client; }

    /// <summary>List dashboards; the route reads <c>status</c>, <c>pinnedOnly</c>, <c>limit</c> and <c>cursor</c>.</summary>
    public async Task<Envelope<List<Dashboard>>> ListAsync(string? status = null, bool? pinnedOnly = null, int? limit = null, string? cursor = null, CancellationToken ct = default)
    {
        var slug = Studio.RequireProjectSlug(_client.Config, "studio.dashboards");
        var q = new Dictionary<string, string>();
        if (status != null) q["status"] = status;
        if (pinnedOnly != null) q["pinnedOnly"] = pinnedOnly.Value ? "true" : "false";
        if (limit != null) q["limit"] = limit.Value.ToString();
        if (cursor != null) q["cursor"] = cursor;
        return await HttpTransport.RequestAsync<List<Dashboard>>(
            _client.Config, HttpMethod.Get, UrlGrammar.ProjectPath(slug, "dashboards"), null, q.Count > 0 ? q : null, ct).ConfigureAwait(false);
    }

    public async Task<Envelope<Dashboard>> CreateAsync(CreateDashboardInput input, CancellationToken ct = default)
    {
        var slug = Studio.RequireProjectSlug(_client.Config, "studio.dashboards");
        return await HttpTransport.RequestAsync<Dashboard>(
            _client.Config, HttpMethod.Post, UrlGrammar.ProjectPath(slug, "dashboards"), input, null, ct).ConfigureAwait(false);
    }

    public async Task<Envelope<Dashboard>> ReadAsync(string dashboardId, CancellationToken ct = default)
    {
        var slug = Studio.RequireProjectSlug(_client.Config, "studio.dashboards");
        return await HttpTransport.RequestAsync<Dashboard>(
            _client.Config, HttpMethod.Get,
            UrlGrammar.ProjectPath(slug, $"dashboards/{HttpUtility.UrlEncode(dashboardId)}"), null, null, ct).ConfigureAwait(false);
    }

    public async Task<Envelope<Dashboard>> UpdateAsync(string dashboardId, Dictionary<string, object?> updates, CancellationToken ct = default)
    {
        var slug = Studio.RequireProjectSlug(_client.Config, "studio.dashboards");
        return await HttpTransport.RequestAsync<Dashboard>(
            _client.Config, HttpMethod.Patch,
            UrlGrammar.ProjectPath(slug, $"dashboards/{HttpUtility.UrlEncode(dashboardId)}"), updates, null, ct).ConfigureAwait(false);
    }

    public async Task<Envelope<Dictionary<string, object?>>> DeleteAsync(string dashboardId, CancellationToken ct = default)
    {
        var slug = Studio.RequireProjectSlug(_client.Config, "studio.dashboards");
        return await HttpTransport.RequestAsync<Dictionary<string, object?>>(
            _client.Config, HttpMethod.Delete,
            UrlGrammar.ProjectPath(slug, $"dashboards/{HttpUtility.UrlEncode(dashboardId)}"), null, null, ct).ConfigureAwait(false);
    }

    public async Task<Envelope<Dashboard>> PinAsync(string dashboardId, CancellationToken ct = default)
    {
        var slug = Studio.RequireProjectSlug(_client.Config, "studio.dashboards");
        return await HttpTransport.RequestAsync<Dashboard>(
            _client.Config, HttpMethod.Post,
            UrlGrammar.ProjectPath(slug, $"dashboards/{HttpUtility.UrlEncode(dashboardId)}/pin"), null, null, ct).ConfigureAwait(false);
    }

    public async Task<Envelope<Dashboard>> UnpinAsync(string dashboardId, CancellationToken ct = default)
    {
        var slug = Studio.RequireProjectSlug(_client.Config, "studio.dashboards");
        return await HttpTransport.RequestAsync<Dashboard>(
            _client.Config, HttpMethod.Post,
            UrlGrammar.ProjectPath(slug, $"dashboards/{HttpUtility.UrlEncode(dashboardId)}/unpin"), null, null, ct).ConfigureAwait(false);
    }
}
