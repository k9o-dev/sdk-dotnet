# K9o.Core

Client for Kilimanjaro. Read and write your data, store files, manage policies and migrations,
deploy and invoke agents or functions, and publish to and subscribe to streams and realtime events.

## Contents

- [Links](#links)
- [Install](#install)
- [Quick start](#quick-start)
- [Authentication and project context](#authentication-and-project-context)
- [Running queries](#running-queries)
- [Query with SQL](#query-with-sql)
- [Search](#search)
- [Deploying a function](#deploying-a-function)
- [Streams](#streams)
- [Approvals](#approvals)
- [Approval policies](#approval-policies)
- [Schema collections](#schema-collections)
- [Generate text](#generate-text)
- [Handling errors](#handling-errors)
- [Appendix](#appendix): [about Kilimanjaro](#about-kilimanjaro)

## Links

- Website: <https://kilimanjaro.dev>
- Documentation / Developer Hub: <https://docs.kilimanjaro.dev>
- Platform API: <https://api.k9o.io>
- Platform Console: <https://console.k9o.io>

## Install

```bash
dotnet add package K9o.Core --version 1.4.0
```

Requires .NET 8 or newer. See [requirements](#requirements).

## Quick start

The examples on this page use a `tasks` collection with `title` and `done` fields.

```csharp
using System.Text.Json.Serialization;
using K9o;

var k9o = K9oClient.Create(new K9oOptions
{
    OrgSlug = "acme",
    ProjectSlug = "tasks-app",
    ApiKey = Environment.GetEnvironmentVariable("K9O_API_KEY"),
});

var result = await k9o.From("tasks")
    .Select()
    .Eq("done", false)
    .Limit(10)
    .ExecuteAsync<TaskItem>();

foreach (var task in result.Data!)
{
    Console.WriteLine($"Output: {task.Id} {task.Title}");
}

public class TaskItem
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("done")] public bool Done { get; set; }
}
```

```text
Output: tsk_01 Write docs
Output: tsk_02 Ship 1.0
```

Row types need `[JsonPropertyName]` attributes, because the wire format is camelCase.

## Authentication and project context

Every request carries your API key. The organization and project tell the platform where to run it.

- `ApiKey` is sent as `Authorization: Bearer <key>`.
- `OrgSlug` builds your organization's host on the default platform.
- `ProjectSlug` selects the project for data, auth and storage calls.
- The SDK does not read environment variables. Read the key yourself, as in the quick start.

The quick start uses the default platform. For a self-hosted or internal gateway, pass its organization host as `BaseUrl`.
Any `<org>.api.<apex>` host works, and no domain is built in. Leave out `OrgSlug`:

```csharp
var k9o = K9oClient.Create(new K9oOptions
{
    BaseUrl = "https://acme.api.corp.example",
    ProjectSlug = "tasks-app",
    ApiKey = Environment.GetEnvironmentVariable("K9O_API_KEY"),
});
```

See [client options](#client-options) for the full list.

## Running queries

Every call returns an `Envelope<T>`. Read rows with `Data` and paging details with `Meta`.

```csharp
var page = await k9o.From("tasks").Select().Limit(2).ExecuteAsync<TaskItem>();
var next = page.Meta.Cursor?.Next;
Console.WriteLine($"Output: next cursor: {next}");
```

```text
Output: next cursor: cur_abc
```

Pass the cursor back with `.Cursor(next)` to get the next page. `Meta.Cursor` is `null` when there is no cursor.

Insert, update and delete use the same builder:

```csharp
var created = await k9o.From("tasks")
    .InsertAsync<TaskItem>(new { title = "Write docs", done = false });
var id = created.Data!.Id;

await k9o.From("tasks").Update(new { done = true }).Eq("id", id).ExecuteAsync<TaskItem>();
await k9o.From("tasks").Delete().Eq("id", id).ExecuteAsync<object>();
```

- `Update` and `Delete` need an `.Eq("id", ...)` filter, or they throw `K9O_INVALID_FIELD_VALUE`.
- Filters available: `Eq`, `Neq`, `Gt`, `Gte`, `Lt`, `Lte`, `Like`, `ILike`, `Is`, `In`, `NotIn`.
- Use `Filter(column, op, value)` for any other operator.
- Every async method accepts an optional `CancellationToken`.

Other areas of the platform are listed in [methods by namespace](#methods-by-namespace).

## Query with SQL

`QueryAsync` runs one read-only SQL statement and returns the rows in `Data`. Values are bound by the database, never put into the SQL text.

```csharp
var res = await k9o.QueryAsync(
    "SELECT id, title FROM tasks WHERE done = :done AND owner = :owner LIMIT 2",
    new Dictionary<string, object?> { ["done"] = false, ["owner"] = "ada" });
Console.WriteLine($"Output: {res.Data!.Count} rows");
Console.WriteLine($"Output: {res.Data[0]["title"].GetString()}");
```

```text
Output: 2 rows
Output: Write docs
```

- Pass an `IReadOnlyDictionary<string, object?>` for named values (`:name` in the SQL) or an `IReadOnlyList<object?>` for positional values (`$1`, `$2`). A list is sent as written.
- Values are strings, numbers, booleans or `null`. Anything else throws `K9O_INVALID_FIELD_VALUE`: serialise it to JSON text first.
- A `:name` with no value in the dictionary throws `K9O_INVALID_FIELD_VALUE` before any request is sent. Text inside quotes, comments and `::` casts is left alone.
- `QueryAsync<T>` binds each row to your own type.
- Read-only: a write is refused with `K9O_RAW_SQL_READ_ONLY`; change data with `From(...)`. One statement per call, at most 100000 characters of SQL and 1000 parameters, row-level security applies, and the caller needs the project-editor role.
- `dataset.<name>` in the SQL reads an analytics dataset.

## Search

`SearchAsync` finds rows whose text column matches a query, best matches first.

```csharp
var hits = await k9o.SearchAsync("articles", new SearchOptions { Column = "body", Q = "solar power", Limit = 5 });
Console.WriteLine($"Output: {hits.Data![0]["title"].GetString()}");
```

```text
Output: Solar power basics
```

`SearchOptions` has `Column` and `Q` (both required), `Language` (default `english`), `Type` (`plain` default, `phrase` or `websearch`), `Limit` and `Cursor`.

The same search is available on the select builder and composes with filters, `Limit` and `Cursor`. `VectorSearch` returns the rows nearest to an embedding, closest first:

```csharp
var near = await k9o.From("articles").Select()
    .Eq("lang", "en")
    .VectorSearch("embedding", new[] { 0.12, -0.4, 0.9 }, metric: "cosine", limit: 3)
    .ExecuteAsync<Article>();
Console.WriteLine($"Output: {near.Data!.Count} rows");
```

```text
Output: 3 rows
```

- `TextSearch(column, query, language = "english", type = "plain")`.
- `VectorSearch(column, embedding, metric = "cosine", limit = null)`: `metric` is `cosine`, `euclidean` or `dot`; `limit` is 1 to 1000.
- A bad column name, an empty query or embedding, or an out-of-range value throws `K9O_INVALID_FIELD_VALUE` before any request.

## Deploying a function

Upload the compiled artifact first. `UploadArtifactAsync` returns the `ArtifactKey` that `DeployAsync` needs.
The kind is `"tar"` (an OCI image archive of the function's container image, up to 256 MiB) and the tier is `sandbox`. The `gateway` tier is for functions owned by platform operators and is not available to most projects. Sandbox functions run in a [Firelock](https://github.com/firelock-sandbox/firelock) microVM sandbox.

```csharp
var archive = await File.ReadAllBytesAsync("hello.tar");
var artifact = await k9o.Functions.UploadArtifactAsync(archive, "tar");

await k9o.Functions.DeployAsync(new DeployFunctionInput
{
    Name = "hello",
    Runtime = "rust",
    Tier = "sandbox",
    ArtifactKey = artifact.Data!.ArtifactKey,
    Version = "1.0.0",
});

var result = await k9o.Functions.InvokeAsync("hello", new { name = "Ada" });
Console.WriteLine($"Output: {result.Data!["greeting"]}");
```

```text
Output: Hello, Ada
```

`InvokeAsync` sends your payload as `{payload}`. Pass `InvokeOptions` as the third argument for `Background` and similar controls.
Optional fields left `null` are not sent.

## Streams

Create a stream, publish an event and read it back live. `maxBytes` is the storage ceiling in bytes: 1, 2, 4, 8, 16, 32 or 64 GiB.

```csharp
var stream = (await k9o.Streams.CreateAsync("Orders", 1L << 30)).Data!;

await k9o.Streams.PublishAsync(stream.Slug, new { id = 1, total = 42 });

await foreach (var e in k9o.Streams.SubscribeAsync(stream.Slug))
{
    Console.WriteLine($"Output: {e.Sequence} {e.Data}");
    break;
}
```

```text
Output: 1 {"id":1,"total":42}
```

- `SubscribeAsync` is an `IAsyncEnumerable`. Pass a `CancellationToken` to stop it, and a sequence as the second argument to resume after it.
- `ReplayAsync` reads a bounded range in one call: `await k9o.Streams.ReplayAsync(stream.Slug, limit: 100)`.
- The server closes each live connection after a short time.
  `SubscribeAsync` reconnects from the last sequence it delivered, so no event is repeated or skipped.
- Publishing needs the project admin role. Subscribing and replaying need admin or auditor.
- While streams are not available, calls fail with `K9O_STREAM_UNAVAILABLE` (HTTP 503).
  A rejected request, such as a bad `subject`, fails with `K9O_STREAM_INVALID`.

## Approvals

Writes sent with `requireApproval` can wait in a queue for a second person. `k9o.Approvals` reads that queue and decides its changes.
Project admins and auditors see every change; other members see only the ones they proposed.

```csharp
var queue = (await k9o.Approvals.ListAsync("pending_approval", 20)).Data!;
var change = queue[0];

Console.WriteLine($"Output: {change.Id} {change.Operation} {change.Collection}");

var decided = (await k9o.Approvals.ApproveAsync(change.Id, "checked the amount")).Data!;

Console.WriteLine($"Output: {decided.Status}");
```

```text
Output: apr_01j9x3 update orders
Output: redeemed
```

- A change is `pending_approval`, `approved`, `rejected`, `expired` or `redeemed` (applied). An `expired` change can still read `pending_approval` until someone decides it.
- `ApproveAsync` can answer 200 with `status` still `pending_approval`. That means your approval is recorded and more approvers are needed; the change is not applied yet.
- `RejectAsync(id, reason)` needs a non-empty `reason`; the SDK checks it before sending.
- `ListAsync(status, limit)` is not paginated: it returns the newest `limit` changes (1 to 200, default 50). `GetAsync(id)` reads one.
- Deciding needs the queue-admin role (project admin or auditor).

Decide many at once with `BulkAsync`. `criteria` is a predicate over each pending change (see [Approval policies](#approval-policies)):

```csharp
var run = (await k9o.Approvals.BulkAsync("reject",
    new Dictionary<string, object?> { ["eq"] = new object[] { "change.table", "orders" } }, "stale")).Data!;

Console.WriteLine($"Output: {run.AffectedCount} {string.Join(",", run.AffectedIds)}");
```

```text
Output: 2 apr_01j9x3,apr_01j9x4
```

- A row that fails (a conflict, an apply error) is skipped silently and left out of `AffectedIds`. Compare `AffectedIds` with the set you intended.
- `reject` needs a `reason`; `criteria` must be a predicate object. `AttestationId` identifies the batch, even when nothing matched.
- It considers the newest 10000 pending changes. Bulk approve is not guaranteed to apply every matched row.

## Approval policies

A policy decides what happens to writes on one collection that match a predicate: `queue` them for approval, or `auto_approve` them.
For a write the first matching policy, oldest first, decides. Policies apply to writes sent with `requireApproval`.

```csharp
var policy = (await k9o.ApprovalPolicies.CreateAsync(new CreateApprovalPolicyInput
{
    Collection = "orders",
    Operation = "update",
    Action = "queue",
    Predicate = new Dictionary<string, object?> { ["gt"] = new object[] { "change.row.amount", 1000 } },
})).Data!;

Console.WriteLine($"Output: {policy.Id} {policy.Action}");

var removed = (await k9o.ApprovalPolicies.DeleteAsync(policy.Id)).Data!;

Console.WriteLine($"Output: {removed.Id}");
```

```text
Output: apo_01j9x5 queue
Output: apo_01j9x5
```

- `Operation` is `insert`, `update` or `delete`; `Action` is `auto_approve` or `queue`.
- `Collection` matches `^[a-zA-Z_][a-zA-Z0-9_]{0,62}$` and `Predicate` is a non-null object. The SDK checks both before sending.
- A predicate has one operator key: `and`, `or`, `not`, `eq`, `neq`, `gt`, `gte`, `lt`, `lte`, `in`, `not_in`, `in_cidr`, `not_in_cidr` or `contains`.
  `and` and `or` take a list of predicates, `not` takes one, and a comparison takes `[left, right]`.
- Operands start with `maker.` or `change.` (for example `change.row.amount`, `change.table`, `change.operation`, `maker.id`).
- `ListAsync()` returns all policies. Identical policies are not de-duplicated. Creating and deleting need project admin or author.

## Schema collections

`k9o.Schema.Collections.ListAsync` lists the project's collections by name, with their size and last migration.

```csharp
var page = await k9o.Schema.Collections.ListAsync(limit: 2);

foreach (var c in page.Data!)
    Console.WriteLine($"Output: {c.Name} {c.RowCount} rows, {c.ColumnCount} columns");
Console.WriteLine($"Output: next {page.Meta.Cursor!.Next}");
```

```text
Output: orders 120 rows, 8 columns
Output: tasks 14 rows, 4 columns
Output: next dGFza3M
```

- Pass `Meta.Cursor.Next` as `cursor` for the next page; it is `null` on the last page. `limit` is 1 to 200 (default 50).
- A row has `Name`, `RowCount`, `RowCountIsExact`, `ColumnCount`, `IndexCount`, `LastMigrationSlug` and `LastMigrationAppliedAt`.
- `RowCount` is a live count that skips soft-deleted rows and ignores row-level security. Without an `org_id` claim on the token, `Meta.Warnings` says so.
- Project admins, auditors, editors, members and viewers can read it.

## Generate text

`Ai.GenerateAsync` runs a prompt through an LLM. Only the prompt is required.

```csharp
var res = (await k9o.Ai.GenerateAsync("Say hello in French.", new GenerateOptions { MaxTokens = 20 })).Data!;
Console.WriteLine($"Output: {res.Content}");
Console.WriteLine($"Output: {res.Usage!.TotalTokens} tokens");
```

```text
Output: Bonjour !
Output: 14 tokens
```

- `GenerateOptions` has `Model`, `MaxTokens`, `Temperature` and `UsageType`. Unset fields are not sent.
- `model` is optional: the org's default LLM is used. With no default the call fails with HTTP 400.
- Models from an outside provider answer 501 `K9O_NOT_IMPLEMENTED` until they are built.

## Handling errors

Every failure is thrown as `K9o.KError`, a subclass of `Exception`.

```csharp
try
{
    await k9o.From("tasks").Select().ExecuteAsync<TaskItem>();
}
catch (KError e)
{
    Console.WriteLine($"Output: code={e.Code}");
    Console.WriteLine($"Output: status={e.Status}");
    Console.WriteLine($"Output: requestId={e.RequestId}");
    Console.WriteLine($"Output: hint={e.Recommendation}");
}
```

```text
Output: code=K9O_PROJECT_NOT_FOUND
Output: status=404
Output: requestId=req_123
Output: hint=Check the project slug.
```

Gateway errors keep the code the platform returned. See [error members](#error-members) for every member and SDK code.

## Appendix

- [Client options](#client-options)
- [Methods by namespace](#methods-by-namespace)
- [Error members](#error-members)
- [Retries](#retries)
- [Regional hosts](#regional-hosts)
- [Requirements](#requirements)
- [Related packages](#related-packages)
- [About Kilimanjaro](#about-kilimanjaro)

### Client options

Set these on `K9oOptions`. See [authentication and project context](#authentication-and-project-context) for how to combine them.

| Option | Meaning |
|---|---|
| `ApiKey` | Sent as `Authorization: Bearer <key>` on every request. |
| `OrgSlug` | Your organization. On a platform host (`api.<apex>`) it builds `<OrgSlug>.api.<apex>`. |
| `ProjectSlug` | Project for data, auth and storage calls. Required for those calls. |
| `BaseUrl` | Host root, scheme and host only. Defaults to `https://api.k9o.io`. |
| `DefaultHeaders` | Extra headers sent on every request. |
| `HttpClient` | Your own `HttpClient`, for timeouts and handlers. |
| `RetryConfig` | A `RetryConfig`. See [retries](#retries). |

`OrgSlug` only changes the URL when `BaseUrl` is a platform host (`api.<apex>`). On any other host it is sent as a header only.

### Methods by namespace

Each namespace is a property or accessor on the client. Method names below are relative to the client, `k9o`.

#### Data

Query and change rows in a collection.

| Method | What it does |
|---|---|
| `From(c).Select()` | Start a query; end it with execute / ExecuteAsync |
| `From(c).InsertAsync<T>(record)` | Insert one row |
| `From(c).Update(patch)` | Update the row named by an id filter |
| `From(c).Delete()` | Delete the row named by an id filter |
| `From(c).Select().TextSearch(column, query)` | Full-text search over a text column |
| `From(c).Select().VectorSearch(column, embedding)` | Rows nearest to an embedding, closest first |
| `QueryAsync(sql, params)` | Run one read-only SQL statement; `params` is a list or a dictionary |
| `SearchAsync(c, options)` | Full-text search over a collection, best matches first |

#### Projects

List and manage projects in your organization.

| Method | What it does |
|---|---|
| `Projects.ListAsync()` | List projects |
| `Projects.CreateAsync(input)` | Create a project. `CreateProjectInput` needs `DisplayName`; `Slug`, `Region`, `CustodyMode` are optional |
| `Projects.GetAsync(slug)` | Read one project |
| `Projects.UpdateAsync(slug, patch)` | Change a project's mutable fields with `UpdateProjectInput` |

#### Gate

Sign-in and session calls for the end users of a project. Needs a project slug.

| Method | What it does |
|---|---|
| `Gate.SignUpAsync(input)` | Register a user |
| `Gate.SignInAsync(input)` | Sign in with email and password |
| `Gate.SignOutAsync(input)` | End a session. `SignOutInput` needs the refresh token |
| `Gate.WhoamiAsync()` | Return the current user |
| `Gate.RefreshTokenAsync(input)` | Exchange a refresh token |

#### Storage

Buckets and objects. Needs a project slug.

| Method | What it does |
|---|---|
| `Storage.ListBucketsAsync()` | List buckets |
| `Storage.GetBucketAsync(name)` | Read one bucket |
| `Storage.CreateBucketAsync(name)` | Create a bucket |
| `Storage.UpdateBucketAsync(name, patch)` | Change bucket settings |
| `Storage.DeleteBucketAsync(name)` | Delete a bucket; pass empty = true to remove its objects too |
| `Storage.From(bucket).UploadAsync(path, data)` | Upload text, bytes or a stream |
| `Storage.From(bucket).CreateSignedUrlAsync(path, options)` | Create a temporary download link (1 to 604800 seconds) |
| `Storage.From(bucket).ListAsync(prefix)` | List objects |
| `Storage.From(bucket).RemoveAsync(path)` | Delete an object |

#### Streams

Publish and read events on user streams. Needs a project slug.

| Method | What it does |
|---|---|
| `Streams.CreateAsync(displayName, maxBytes, retentionSeconds)` | Create a stream. `retentionSeconds` may be `null` for the default |
| `Streams.ListAsync()` | List streams |
| `Streams.GetAsync(slug)` | Read one stream |
| `Streams.DeleteAsync(slug)` | Retire a stream |
| `Streams.PublishAsync(slug, data, subject)` | Append an event. `subject` is an optional routing suffix |
| `Streams.ReplayAsync(slug, fromSequence, untilSequence, limit)` | Read a range. The result has `Events`, `NextCursor`, `HasMore` |
| `Streams.SubscribeAsync(slug, fromSequence, ct)` | Live events (`Sequence`, `Subject`, `Data`, `PublishedAt`); reconnects from the last sequence |

#### Approvals

Pending changes. Needs a project slug.

| Method | What it does |
|---|---|
| `Approvals.ListAsync(status, limit)` | Newest pending changes. `limit` 1 to 200; not paginated |
| `Approvals.GetAsync(id)` | Read one change |
| `Approvals.ApproveAsync(id, reason)` | Approve. `reason` is optional. Check `Status`: `pending_approval` means more approvers are needed |
| `Approvals.RejectAsync(id, reason)` | Reject. `reason` is required |
| `Approvals.BulkAsync(action, criteria, reason)` | Approve or reject every change matching a predicate. Compare `AffectedIds` with what you intended |

#### Approval policies

| Method | What it does |
|---|---|
| `ApprovalPolicies.ListAsync()` | All policies, oldest first |
| `ApprovalPolicies.CreateAsync(input)` | Create from a `CreateApprovalPolicyInput` (`Collection`, `Operation`, `Action`, `Predicate`) |
| `ApprovalPolicies.DeleteAsync(id)` | Delete a policy |

#### Schema

| Method | What it does |
|---|---|
| `Schema.Collections.ListAsync(limit, cursor)` | List collections by name, one page at a time |

#### Realtime

Live events over server-sent events. There is no replay. After a `resync` frame, re-read current state and subscribe again.

| Method | What it does |
|---|---|
| `Realtime.SubscribeAsync(topic, filter)` | Stream events (`Topic`, `EventType`, `Payload`, `OccurredAt`); cancel the token to stop |
| `Realtime.Topic.Collection(name)` | Topic for changes in a collection |
| `Realtime.Topic.WorkflowRuns()` | Topic for workflow run events |
| `Realtime.Topic.AuditOrg(family)` | Topic for organization audit events |

#### Studio: workflows

Create and run workflows.

| Method | What it does |
|---|---|
| `Studio.Workflows.ListAsync(status, limit, cursor)` | List workflows |
| `Studio.Workflows.CreateAsync(input)` | Create a workflow |
| `Studio.Workflows.ReadAsync(id)` | Read one workflow |
| `Studio.Workflows.UpdateAsync(id, updates)` | Change a workflow |
| `Studio.Workflows.DeleteAsync(id)` | Delete a workflow |
| `Studio.Workflows.TriggerAsync(id, context)` | Start a run. Sends `{triggerContext}` |
| `Studio.Workflows.Runs.ListAsync(id, limit, cursor)` | List runs of a workflow |
| `Studio.Workflows.Runs.ReadAsync(id, runId)` | Read one run |
| `Studio.Workflows.Runs.CancelAsync(id, runId)` | Cancel a run |

#### Studio: dashboards

Create and pin dashboards.

| Method | What it does |
|---|---|
| `Studio.Dashboards.ListAsync(status)` | List dashboards |
| `Studio.Dashboards.CreateAsync(input)` | Create a dashboard |
| `Studio.Dashboards.ReadAsync(id)` | Read one dashboard |
| `Studio.Dashboards.UpdateAsync(id, updates)` | Change a dashboard |
| `Studio.Dashboards.DeleteAsync(id)` | Delete a dashboard |
| `Studio.Dashboards.PinAsync(id)` | Pin a dashboard |
| `Studio.Dashboards.UnpinAsync(id)` | Unpin a dashboard |

#### Compute

Language models available to the project.

| Method | What it does |
|---|---|
| `Compute.Llms.ListAsync()` | List models |
| `Compute.Llms.ReadAsync(modelId)` | Read one model |
| `Compute.Llms.InvokeAsync(modelId, input)` | Run a prompt. The result has `Content` and `Usage` |
| `Compute.Llms.InvokeStreamAsync(modelId, input)` | Run a prompt. Chunks are OpenAI-style: `Choices[0].Delta.Content` |

#### AI

Text embeddings.

| Method | What it does |
|---|---|
| `Ai.EmbedAsync(input)` | Embed one string or a list of strings |
| `Ai.EmbedAsync(input, model)` | Embed with a named model |
| `Ai.GenerateAsync(prompt, options)` | Run a prompt with the default or a named LLM. `options` is optional |

#### Functions

Deploy and call functions.

| Method | What it does |
|---|---|
| `Functions.UploadArtifactAsync(bytes, kind)` | Upload `"wasm"` or `"tar"` bytes. Returns the `ArtifactKey` |
| `Functions.DeployAsync(input)` | Deploy from an artifact: `Name`, `Runtime`, `Tier`, `ArtifactKey`, `Version` |
| `Functions.ListAsync(status, limit, cursor)` | List functions |
| `Functions.GetAsync(name)` | Read one function |
| `Functions.DeleteAsync(name)` | Delete a function |
| `Functions.InvokeAsync(name, payload, options)` | Call a function. `options` is optional; the payload is sent as `{payload}` |

#### Migrations

Change the project's schema.

| Method | What it does |
|---|---|
| `Migrations.ApplyAsync(list)` | Apply migrations now |
| `Migrations.ListAsync()` | List applied migrations |
| `Migrations.GetAsync(version)` | Read one applied migration |
| `Migrations.QueueAsync(list)` | Queue migrations for approval |
| `Migrations.ListPendingAsync()` | List queued migrations |
| `Migrations.ApplyPendingAsync(id)` | Apply a queued migration |
| `Migrations.RejectPendingAsync(id, reason)` | Reject a queued migration |
| `Migrations.DeletePendingAsync(id)` | Remove a queued migration |

#### Policies

Policy bundles.

| Method | What it does |
|---|---|
| `Policies.CurrentAsync()` | Read the active bundle |
| `Policies.ListAsync(status)` | List bundles, filtered by `status` only |
| `Policies.CreateAsync(input)` | Create a bundle from a `PolicyInput` (`Policy`, optional `Rules`, `EditorMode`) |
| `Policies.ValidateAsync(input)` | Check a policy document without saving |
| `Policies.GetAsync(id)` | Read one bundle |
| `Policies.UpdateAsync(id, input)` | Replace a bundle's policy document |
| `Policies.DeleteAsync(id)` | Delete a bundle |
| `Policies.PublishAsync(id)` | Make a bundle active |

#### Audit

The project audit log.

| Method | What it does |
|---|---|
| `Audit.QueryAsync(opts)` | `AuditQueryOpts`: `EventType`, `ActorId`, `After`, `Before`, `Limit`, `Cursor` |

#### Organization

Members, groups, roles, identity providers, SMTP and defaults.

| Method | What it does |
|---|---|
| `Org.GetAsync()` | Read the organization |
| `Org.Members.ListAsync(status)` | List members |
| `Org.Members.InviteAsync(email, role)` | Invite a member |
| `Org.Members.RemoveAsync(userId)` | Remove a member |
| `Org.Groups.ListAsync()` | List groups |
| `Org.Groups.GetAsync(groupId)` | Read one group |
| `Org.Gate.Roles.ListAsync()` | List role grants |
| `Org.Gate.Roles.GrantAsync(userId, role)` | Grant a role |
| `Org.Gate.Roles.GetAsync(grantId)` | Read one role grant |
| `Org.Gate.Roles.RevokeAsync(grantId)` | Revoke a role grant |
| `Org.Gate.IdpProviders.ListAsync()` | List identity providers |
| `Org.Gate.IdpProviders.CreateAsync(input)` | Add an identity provider with `CreateIdpInput` |
| `Org.Gate.IdpProviders.GetAsync(id)` | Read an identity provider |
| `Org.Gate.IdpProviders.ConfigureAsync(id, patch)` | Change an identity provider |
| `Org.Gate.IdpProviders.DeleteAsync(id)` | Remove an identity provider |
| `Org.Smtp.SetAsync(input)` | Save SMTP settings (`PasswordRef`, `FromAddress`) |
| `Org.Smtp.TestAsync(to, subject)` | Send a test email |
| `Org.Smtp.UnsetAsync()` | Remove SMTP settings |
| `Org.AuditLog.QueryAsync(type, limit)` | Search the organization audit log |
| `Org.DefaultLlm.GetAsync()` | Read the default model. Returns an object, not a string |
| `Org.DefaultLlm.SetAsync(modelId)` | Set the default model |
| `Orgs.ListAsync()` | List organizations |
| `Orgs.GetAsync(orgSlug)` | Read one organization |
| `Orgs.CurrentAsync()` | Read the current organization |
| `Orgs.RestoreAsync(orgId)` | Restore a deleted organization |

#### Me

The signed-in operator's own account.

| Method | What it does |
|---|---|
| `Me.Profile.GetAsync()` | Read your profile |
| `Me.Profile.UpdateAsync(displayName)` | Change your display name |
| `Me.Sessions.ListAsync()` | List sessions |
| `Me.Sessions.RevokeAsync(sessionId)` | End one session |
| `Me.Sessions.RevokeAllAsync()` | End all sessions |
| `Me.ApiTokens.ListAsync()` | List API tokens |
| `Me.ApiTokens.CreateAsync(label, scopeManifest, ttlSeconds)` | Create an API token |
| `Me.ApiTokens.DeleteAsync(tokenId)` | Delete an API token |

#### Vault secrets

Store and reveal secrets. Revealing needs a one-time challenge.

| Method | What it does |
|---|---|
| `Vault.Secrets.ListAsync(...)` | List secrets, optionally by prefix |
| `Vault.Secrets.GetAsync(name, region)` | Read one secret's metadata. To find a secret by exact name, use `ListAsync` with the name as `prefix` |
| `Vault.Secrets.CreateAsync(...)` | Create a secret |
| `Vault.Secrets.RevealChallengeAsync(name, region)` | Get a one-time reveal challenge |
| `Vault.Secrets.RevealAsync(name, region, challenge)` | Read the secret value |
| `Vault.Secrets.UpdateAsync(...)` | Change a secret |
| `Vault.Secrets.RotateAsync(name, region)` | Rotate a secret |
| `Vault.Secrets.MoveAsync(name, region, newPath)` | Move a secret to a new path |
| `Vault.Secrets.DeleteAsync(name, region)` | Delete a secret |

### Error members

#### Members

| Member | Meaning |
|---|---|
| `Code` | Error code, for example `K9O_PROJECT_NOT_FOUND` |
| `Message` | Human-readable message |
| `Status` | HTTP status, or `null` for errors raised inside the SDK |
| `RequestId` | Trace id from the gateway, if any |
| `Recommendation` | Suggested next step, if any |
| `Context` | Read-only dictionary with per-code details |

#### Codes raised by the SDK

| Code | When |
|---|---|
| `K9O_INVALID_BASE_URL` | `BaseUrl` is malformed, not http(s), or has a path, query or fragment |
| `K9O_INVALID_FIELD_VALUE` | A slug, collection name or required argument is invalid |
| `K9O_MISSING_PROJECT_CONTEXT` | A project-scoped call was made without `ProjectSlug` |
| `K9O_UPSTREAM_UNAVAILABLE` | The request could not reach the platform |
| `K9O_INTERNAL_ERROR` | The response was not a valid envelope or error body |
| `K9O_INVALID_FILTER` | A realtime filter could not be encoded |

### Retries

Idempotent requests (GET, HEAD, OPTIONS, DELETE) are retried on transport errors and on 429, 502, 503 and 504 responses.

| Setting | Default |
|---|---|
| `MaxRetries` | 3 |
| `Backoff` | `"exponential"` (also `"linear"` or `"none"`) |
| `InitialDelayMs` | 100 |
| `MaxDelayMs` | 5000 |

Override with `RetryConfig = new RetryConfig { MaxRetries = 5 }` and any other settings you want to change.

### Regional hosts

An organization host such as `https://acme.api.k9o.io` redirects to the organization's regional host.

- The SDK finds the regional host with one unauthenticated `HEAD` request.
- It sends all later requests to that host.
- The result is cached for the life of the client.

### Requirements

| Requirement | Version |
|---|---|
| .NET | 8 or newer |
| HTTP and JSON | `HttpClient` and `System.Text.Json`, no other dependencies |

### Related packages

| Package | Purpose |
|---|---|
| `K9o.Kibo` | Client-side encryption against your own KMS |

### About Kilimanjaro

Kilimanjaro is a modular developer platform for building secure enterprise applications.
You define AI agents, functions and data pipelines on the platform.
The SDKs let you deploy and invoke them, read and write your data, publish to and subscribe to streams, sign users in,
and encrypt with keys you hold.

It provides four building blocks:

- **Keep**: AI security and governance. Guardrails and policy for the agents you run.
- **Pulse**: data intelligence and real-time stream processing. Streams, analytics and live feeds.
- **Gate**: customer identity and access management. Sign-in and sessions, and automatic data scoping
  so a signed-in user reaches only their own data.
- **Kibo**: cryptographic keys and secrets management. Client-side encryption with keys you hold, and a secrets vault.

## License

Apache-2.0 © Formelo Inc. See `LICENSE` and `NOTICE`.
