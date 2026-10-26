# Changelog — K9o.Core (.NET)

## 1.3.0 — 2026-10-26

Every method now matches the gateway and is checked by a contract test.

### Changed

- `Projects.CreateAsync` takes `CreateProjectInput` with `DisplayName` (required) and optional `Slug`, `Region`, `CustodyMode`. `GetAsync` and `UpdateAsync` use the projects routes.
- `Functions.DeployAsync` takes `Name`, `Runtime`, `Tier` (`gateway` or `sandbox`), `ArtifactKey`, `Version` and `Description`. Inline code is gone.
- `Functions.InvokeAsync` sends `{payload}` and accepts `InvokeOptions`. `Functions.ListAsync` accepts `limit` and `cursor`.
- Policies `CreateAsync`, `UpdateAsync` and `ValidateAsync` take a `PolicyInput` document. `Policies.ListAsync` takes only `status`.
- `Audit.QueryAsync` filters are `EventType`, `ActorId`, `After`, `Before`, `Limit`, `Cursor`.
- Role grants use `/role-grants` and identity providers use `/identity-providers`, with a typed `CreateIdpInput`.
- `Org.DefaultLlm` returns an object. `Org.Smtp.SetAsync` takes `PasswordRef` and `FromAddress`; `TestAsync` takes `to` and `subject`.
- `Me.ApiTokens.CreateAsync` takes `label`, `scopeManifest` and `ttlSeconds`. `Me.Sessions.RevokeAsync` and `RevokeAllAsync` use the session routes.
- `Compute.Llms.InvokeAsync` parameters are flat and the result has `Content` and `Usage`. Stream chunks are OpenAI-style.
- `RealtimeEvent` is `{Topic, EventType, Payload, OccurredAt}`. `Studio.Workflows.TriggerAsync` sends `{triggerContext}`.
- Optional fields left `null` are not sent.
- The API host is read from the base URL: any `<org>.api.<apex>` host works and no domain is built in.

### Removed

- `Gate.VerifySessionAsync`, `Gate.HostedAsync`, `Realtime.ReplayAsync` and `Realtime.SubscribePollAsync`.
- `Me.Notifications`, `Me.AuditLog`, `Me.DataExport`.
- `Org.Groups.CreateAsync`, `Org.Groups.DeleteAsync`, `Org.Members.GetAsync`, `Org.Smtp.GetAsync`.
- The `modality` filter of `Compute.Llms.ListAsync` and the `triggerType` filter of `Studio.Workflows.ListAsync`.

### Added

- `Functions.UploadArtifactAsync(bytes, "wasm" | "tar")` returns the `ArtifactKey` for `DeployAsync`.

## 1.2.0 — 2026-10-25

- The README now introduces the Kilimanjaro products (Keep, Pulse, Gate, Kibo). No change to the public API.

## 1.1.1 — 2026-10-25

- Documentation links corrected: the documentation hub is https://docs.kilimanjaro.dev, with the website, platform API and console listed in a Links section. Package metadata points at the same places.

## 1.1.0 — 2026-10-25

- Documentation rewritten with runnable examples and expected output; internal cross-references removed from the published sources. No change to the public API.
- Fixed: boolean filter values are sent as `true` / `false`. `Eq("done", false)` previously sent `False`.
- Error text for `update()` / `delete()` without an `id` filter no longer mentions a version or a roadmap.

## 1.0.0 — 2026-10-23

First public release. See the README for the full API overview.
