# K9o.Core

.NET SDK for the Kilimanjaro (k9o) platform: a typed client for the data API, projects, auth, storage, realtime and more.

## Contents

- [Install](#install)
- [Quick start](#quick-start)
- [Authentication and project context](#authentication-and-project-context)
- [Running queries](#running-queries)
- [Handling errors](#handling-errors)
- [Appendix](#appendix)

## Install

```bash
dotnet add package K9o.Core --version 1.1.0
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
    Console.WriteLine($"{task.Id} {task.Title}");
}

public class TaskItem
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("title")] public string Title { get; set; } = "";
    [JsonPropertyName("done")] public bool Done { get; set; }
}
```

```text
tsk_01 Write docs
tsk_02 Ship 1.0
```

Row types need `[JsonPropertyName]` attributes, because the wire format is camelCase.

## Authentication and project context

Every request carries your API key. The organization and project tell the platform where to run it.

- `ApiKey` is sent as `Authorization: Bearer <key>`.
- `OrgSlug` picks your organization's host on the default platform.
- `ProjectSlug` selects the project for data, auth and storage calls.
- The SDK does not read environment variables. Read the key yourself, as in the quick start.

The quick start uses the default platform. For a custom or self-hosted host, pass `BaseUrl` and leave out `OrgSlug`:

```csharp
var k9o = K9oClient.Create(new K9oOptions
{
    BaseUrl = "https://k9o.example.internal",
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
Console.WriteLine($"next cursor: {next}");
```

```text
next cursor: cur_abc
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

## Handling errors

Every failure is thrown as `K9o.KError`, a subclass of `Exception`.

```csharp
try
{
    await k9o.From("tasks").Select().ExecuteAsync<TaskItem>();
}
catch (KError e)
{
    Console.WriteLine($"code={e.Code}");
    Console.WriteLine($"status={e.Status}");
    Console.WriteLine($"requestId={e.RequestId}");
    Console.WriteLine($"hint={e.Recommendation}");
}
```

```text
code=K9O_PROJECT_NOT_FOUND
status=404
requestId=req_123
hint=Check the project slug.
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

### Client options

Set these on `K9oOptions`. See [authentication and project context](#authentication-and-project-context) for how to combine them.

| Option | Meaning |
|---|---|
| `ApiKey` | Sent as `Authorization: Bearer <key>` on every request. |
| `OrgSlug` | Your organization. On the default platform it selects `https://<OrgSlug>.api.k9o.io`. |
| `ProjectSlug` | Project for data, auth and storage calls. Required for those calls. |
| `BaseUrl` | Host root, scheme and host only. Defaults to `https://api.k9o.io`. |
| `DefaultHeaders` | Extra headers sent on every request. |
| `HttpClient` | Your own `HttpClient`, for timeouts and handlers. |
| `RetryConfig` | A `RetryConfig`. See [retries](#retries). |

`OrgSlug` only changes the URL when `BaseUrl` is the platform apex. On any other host it is not applied to the URL.

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

#### Projects

List and manage projects in your organization.

| Method | What it does |
|---|---|
| `Projects.ListAsync()` | List projects |
| `Projects.CreateAsync(input)` | Create a project |
| `Projects.GetAsync(slug)` | Read one project |
| `Projects.UpdateAsync(slug, patch)` | Change a project's mutable fields |

#### Gate

Sign-in and session calls for the end users of a project. Needs a project slug.

| Method | What it does |
|---|---|
| `Gate.SignUpAsync(input)` | Register a user |
| `Gate.SignInAsync(input)` | Sign in with email and password |
| `Gate.SignOutAsync(input)` | End a session |
| `Gate.WhoamiAsync()` | Return the current user |
| `Gate.RefreshTokenAsync(input)` | Exchange a refresh token |
| `Gate.VerifySessionAsync()` | Check that the session is valid |
| `Gate.HostedAsync()` | Get the hosted sign-in URL and WebAuthn rpId (no credential sent) |

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

#### Realtime

Live events over server-sent events, with a polling fallback.

| Method | What it does |
|---|---|
| `Realtime.SubscribeAsync(topic, filter)` | Stream events; close the stream when done |
| `Realtime.ReplayAsync(input)` | Fetch past events from a sequence number |
| `Realtime.SubscribePollAsync(topic, fromSequence, filter)` | Poll for events instead of streaming |
| `Realtime.Topic.Collection(name)` | Topic for changes in a collection |
| `Realtime.Topic.WorkflowRuns()` | Topic for workflow run events |
| `Realtime.Topic.AuditOrg(family)` | Topic for organization audit events |

#### Studio: workflows

Create and run workflows.

| Method | What it does |
|---|---|
| `Studio.Workflows.ListAsync(status, triggerType)` | List workflows |
| `Studio.Workflows.CreateAsync(input)` | Create a workflow |
| `Studio.Workflows.ReadAsync(id)` | Read one workflow |
| `Studio.Workflows.UpdateAsync(id, updates)` | Change a workflow |
| `Studio.Workflows.DeleteAsync(id)` | Delete a workflow |
| `Studio.Workflows.TriggerAsync(id, context)` | Start a run |
| `Studio.Workflows.Runs.ListAsync(id, status)` | List runs of a workflow |
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
| `Compute.Llms.ListAsync(modality)` | List models |
| `Compute.Llms.ReadAsync(modelId)` | Read one model |
| `Compute.Llms.InvokeAsync(modelId, input)` | Run a prompt and get the full answer |
| `Compute.Llms.InvokeStreamAsync(modelId, input)` | Run a prompt and stream chunks |

#### AI

Text embeddings.

| Method | What it does |
|---|---|
| `Ai.EmbedAsync(input)` | Embed one string or a list of strings |
| `Ai.EmbedAsync(input, model)` | Embed with a named model |

#### Functions

Deploy and call functions.

| Method | What it does |
|---|---|
| `Functions.DeployAsync(input)` | Deploy a function |
| `Functions.ListAsync(status)` | List functions |
| `Functions.GetAsync(name)` | Read one function |
| `Functions.DeleteAsync(name)` | Delete a function |
| `Functions.InvokeAsync(name, payload)` | Call a function |

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
| `Policies.ListAsync(status)` | List bundles |
| `Policies.CreateAsync(name, rules)` | Create a bundle |
| `Policies.ValidateAsync(rules)` | Check rules without saving |
| `Policies.GetAsync(id)` | Read one bundle |
| `Policies.UpdateAsync(id, patch)` | Change a bundle |
| `Policies.DeleteAsync(id)` | Delete a bundle |
| `Policies.PublishAsync(id)` | Make a bundle active |

#### Audit

The project audit log.

| Method | What it does |
|---|---|
| `Audit.QueryAsync(opts)` | Search audit events by time, type, actor or target |

#### Organization

Members, groups, roles, identity providers, SMTP and defaults.

| Method | What it does |
|---|---|
| `Org.GetAsync()` | Read the organization |
| `Org.Members.ListAsync(status)` | List members |
| `Org.Members.InviteAsync(email, role)` | Invite a member |
| `Org.Members.GetAsync(userId)` | Read one member |
| `Org.Members.RemoveAsync(userId)` | Remove a member |
| `Org.Groups.ListAsync()` | List groups |
| `Org.Groups.CreateAsync(name)` | Create a group |
| `Org.Groups.GetAsync(groupId)` | Read one group |
| `Org.Groups.DeleteAsync(groupId)` | Delete a group |
| `Org.Gate.Roles.ListAsync(userId, role)` | List role grants |
| `Org.Gate.Roles.GrantAsync(userId, role)` | Grant a role |
| `Org.Gate.Roles.RevokeAsync(grantId)` | Revoke a role grant |
| `Org.Gate.IdpProviders.ListAsync()` | List identity providers |
| `Org.Gate.IdpProviders.CreateAsync(name, kind, config)` | Add an identity provider |
| `Org.Gate.IdpProviders.ConfigureAsync(id, patch)` | Change an identity provider |
| `Org.Gate.IdpProviders.DeleteAsync(id)` | Remove an identity provider |
| `Org.Smtp.GetAsync()` | Read SMTP settings |
| `Org.Smtp.SetAsync(input)` | Save SMTP settings |
| `Org.Smtp.TestAsync(to, fromOverride)` | Send a test email |
| `Org.Smtp.UnsetAsync()` | Remove SMTP settings |
| `Org.AuditLog.QueryAsync(opts)` | Search the organization audit log |
| `Org.DefaultLlm.GetAsync()` | Read the default model |
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
| `Me.ApiTokens.CreateAsync(name, expiresInDays, scope)` | Create an API token |
| `Me.ApiTokens.DeleteAsync(tokenId)` | Delete an API token |
| `Me.Notifications.ListAsync(unreadOnly, limit)` | List notifications |
| `Me.Notifications.MarkReadAsync(id)` | Mark one as read |
| `Me.Notifications.MarkAllReadAsync()` | Mark all as read |
| `Me.AuditLog.QueryAsync(opts)` | Search your own audit events |
| `Me.DataExport.RequestAsync(format)` | Request a data export |
| `Me.DataExport.StatusAsync(jobId)` | Check an export job |

#### Vault secrets

Store and reveal secrets. Revealing needs a one-time challenge.

| Method | What it does |
|---|---|
| `Vault.Secrets.ListAsync(...)` | List secrets, optionally by prefix |
| `Vault.Secrets.GetAsync(name, region)` | Read secret metadata |
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

## License

Apache-2.0 © Formelo Inc. See `LICENSE` and `NOTICE`.
